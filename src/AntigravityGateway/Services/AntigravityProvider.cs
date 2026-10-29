using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AntigravityGateway.Configuration;
using AntigravityGateway.Models.Gemini;

namespace AntigravityGateway.Services;

/// <summary>
/// Antigravity 原生 Provider：
/// 直接代理 Antigravity 登录账号与 daily-cloudcode-pa / cloudcode-pa 上游，
/// 采用 aicode-consumers 项目，支持全部 Antigravity 模型（Gemini 3.8 Flash, Gemini 2.5 Pro, Claude Sonnet, etc.）。
/// </summary>
public sealed class AntigravityProvider : IGeminiProvider
{
    private readonly ProviderOptions _opts;
    private readonly AntigravityAccountService _account;
    private readonly HttpClient _http;
    private readonly ILogger<AntigravityProvider> _log;
    private readonly SemaphoreSlim _requestGate;
    private readonly object _cooldownLock = new();
    private readonly TimeSpan _minInterval;
    private DateTimeOffset _nextAllowedAt = DateTimeOffset.MinValue;
    private int _lastResetAfter;
    private List<string> _models;

    public AntigravityProvider(
        ProviderOptions opts,
        AntigravityAccountService account,
        ILogger<AntigravityProvider> log)
    {
        _opts = opts;
        _account = account;
        _log = log;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(300) };
        var conc = Math.Max(1, opts.MaxConcurrency);
        _requestGate = new SemaphoreSlim(conc, conc);
        _minInterval = TimeSpan.FromSeconds(Math.Max(0, opts.MinInterval));

        _models = opts.Models.Count > 0
            ? new List<string>(opts.Models)
            : new List<string>
            {
                "gemini-3.8-flash-high",
                "gemini-3.8-flash",
                "gemini-3.8-flash-medium",
                "gemini-3.8-flash-low",
                "gemini-3.5-flash-lite",
                "gemini-2.5-pro",
                "gemini-2.5-flash",
                "claude-sonnet-4-6",
                "claude-opus-4-6-thinking",
                "gpt-oss-120b-medium",
                "gpt-4o"
            };

        _log.LogInformation("AntigravityProvider initialized. Project={Project}, BaseUrl={Url}, Concurrency={Conc}",
            Project, BaseUrl, conc);
    }

    public string Name => "Antigravity";
    public string Project => string.IsNullOrWhiteSpace(_opts.Project) ? "aicode-consumers" : _opts.Project;
    public string Location => "global";
    public IReadOnlyList<string> Models => _models;

    private string BaseUrl => _opts.BaseUrl is { Length: > 0 } custom
        ? custom.TrimEnd('/')
        : "https://daily-cloudcode-pa.googleapis.com/v1internal";

    private string UserAgent => _opts.UserAgent is { Length: > 0 } ua
        ? ua
        : "antigravity/2.24.0 (win32; x64)";

    private int MaxAttempts => _opts.MaxAttempts > 0 ? _opts.MaxAttempts : 4;

    private sealed class CloudCodePayload
    {
        [JsonPropertyName("project")] public string Project { get; set; } = "aicode-consumers";
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("request")] public GeminiGenerateContentRequest Request { get; set; } = new();
    }

    private string ResolveUpstreamModel(string requested)
    {
        var m = requested.Trim().ToLowerInvariant();
        return m switch
        {
            "gemini-3.8-flash" => "gemini-3.8-flash-high",
            "gemini-3.8" => "gemini-3.8-flash-high",
            "gpt-4o" => "gemini-3.8-flash-high",
            "gpt-4" => "gemini-3.8-flash-high",
            "claude-3-5-sonnet" => "claude-sonnet-4-6",
            "claude-3-7-sonnet" => "claude-sonnet-4-6",
            _ => m
        };
    }

    // ---------- 非流式聚合 ----------

    public async Task<GeminiGenerateContentResponse> GenerateAsync(
        string model, GeminiGenerateContentRequest request, CancellationToken ct)
    {
        // 上游 daily-cloudcode-pa 专为 stream 设计（/generateContent 为 404），
        // 故非流式通过消费完整 stream 汇聚 candidates、parts 与 usage，确保 100% 成功率与完整性。
        var textBuilder = new System.Text.StringBuilder();
        var parts = new List<GeminiPart>();
        string? finishReason = null;
        GeminiUsageMetadata? lastUsage = null;
        string? modelVersion = null;

        await foreach (var chunk in GenerateStreamAsync(model, request, ct))
        {
            if (chunk.ModelVersion != null) modelVersion = chunk.ModelVersion;
            if (chunk.UsageMetadata != null) lastUsage = chunk.UsageMetadata;

            var cand = chunk.Candidates?.FirstOrDefault();
            if (cand == null) continue;

            if (cand.FinishReason != null) finishReason = cand.FinishReason;

            if (cand.Content?.Parts != null)
            {
                foreach (var part in cand.Content.Parts)
                {
                    if (!string.IsNullOrEmpty(part.Text))
                    {
                        textBuilder.Append(part.Text);
                    }
                    if (part.FunctionCall != null)
                    {
                        parts.Add(part);
                    }
                }
            }
        }

        if (textBuilder.Length > 0)
        {
            parts.Insert(0, new GeminiPart { Text = textBuilder.ToString() });
        }

        return new GeminiGenerateContentResponse
        {
            ModelVersion = modelVersion,
            UsageMetadata = lastUsage,
            Candidates = new List<GeminiCandidate>
            {
                new()
                {
                    Content = new GeminiContent
                    {
                        Role = "model",
                        Parts = parts
                    },
                    FinishReason = finishReason ?? (parts.Any(p => p.FunctionCall != null) ? "FUNCTION_CALL" : "STOP")
                }
            }
        };
    }

    // ---------- 流式核心 ----------

    public async IAsyncEnumerable<GeminiGenerateContentStreamChunk> GenerateStreamAsync(
        string model, GeminiGenerateContentRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        var upstreamModel = ResolveUpstreamModel(model);
        var url = $"{BaseUrl}:streamGenerateContent?alt=sse";

        var payload = new CloudCodePayload
        {
            Project = Project,
            Model = upstreamModel,
            Request = request
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions.Gemini);

        await _requestGate.WaitAsync(ct);
        try
        {
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                var token = await _account.GetTokenAsync(false, ct);

                using var msg = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                };
                msg.Headers.Authorization = new("Bearer", token.Value);
                msg.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

                HttpResponseMessage? resp = null;
                Stream? stream = null;
                StreamReader? reader = null;

                try
                {
                    await WaitForCooldownAsync(ct);
                    resp = await _http.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct);

                    // 401 反应式刷新与单次重试
                    if (resp.StatusCode == HttpStatusCode.Unauthorized && attempt == 1)
                    {
                        _log.LogWarning("Antigravity upstream returned 401; forcing token refresh and retrying once");
                        await _account.ForceRefreshAsync(ct);
                        resp.Dispose();
                        continue;
                    }

                    if (resp.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        var errBody = await resp.Content.ReadAsStringAsync(ct);
                        var delay = ParseRetryDelay(errBody);
                        ApplyThrottleCooldown((int)delay.TotalSeconds);
                        _log.LogWarning("Antigravity 429 rate limit; retry {Attempt}/{Max} after {Delay}s: {Body}",
                            attempt, MaxAttempts, delay.TotalSeconds, Truncate(errBody, 200));
                        resp.Dispose();
                        if (attempt < MaxAttempts)
                        {
                            await Task.Delay(delay, ct);
                            continue;
                        }
                        throw new UpstreamException(HttpStatusCode.TooManyRequests, errBody);
                    }

                    if (!resp.IsSuccessStatusCode)
                    {
                        var errBody = await resp.Content.ReadAsStringAsync(ct);
                        resp.Dispose();
                        throw new UpstreamException(resp.StatusCode, errBody);
                    }

                    ApplySuccessCooldown();

                    stream = await resp.Content.ReadAsStreamAsync(ct);
                    reader = new StreamReader(stream);
                }
                catch (Exception ex) when (ex is not UpstreamException && attempt < MaxAttempts && IsTransient(ex))
                {
                    _log.LogWarning(ex, "Transient connection error during send; retry {Attempt}/{Max}", attempt, MaxAttempts);
                    resp?.Dispose();
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt), 15)), ct);
                    continue;
                }

                if (reader is null) continue;

                string? thoughtSignatureBuffer = null;

                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync(ct);
                        if (line is null) break;
                        line = line.Trim();

                        if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var data = line[5..].Trim();
                        if (string.IsNullOrEmpty(data) || data == "[DONE]") continue;

                        GeminiGenerateContentStreamChunk? chunk = null;
                        try
                        {
                            using var doc = JsonDocument.Parse(data);
                            var root = doc.RootElement;
                            var unwrapped = root.TryGetProperty("response", out var r) ? r : root;

                            // 捕获前置思考签名
                            if (unwrapped.TryGetProperty("candidates", out var cands) && cands.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var c in cands.EnumerateArray())
                                {
                                    if (c.TryGetProperty("content", out var cnt) && cnt.TryGetProperty("parts", out var prts))
                                    {
                                        foreach (var p in prts.EnumerateArray())
                                        {
                                            if (p.TryGetProperty("thoughtSignature", out var ts) && ts.ValueKind == JsonValueKind.String)
                                            {
                                                thoughtSignatureBuffer = ts.GetString();
                                            }
                                        }
                                    }
                                }
                            }

                            chunk = JsonSerializer.Deserialize<GeminiGenerateContentStreamChunk>(unwrapped, JsonOptions.Gemini);

                            // 如果当前 chunk 包含 functionCall 且没有 thoughtSignature，注入前置捕获的签名
                            if (chunk?.Candidates != null && !string.IsNullOrEmpty(thoughtSignatureBuffer))
                            {
                                foreach (var c in chunk.Candidates)
                                {
                                    if (c.Content?.Parts != null)
                                    {
                                        foreach (var p in c.Content.Parts)
                                        {
                                            if (p.FunctionCall != null && string.IsNullOrEmpty(p.ThoughtSignature))
                                            {
                                                p.ThoughtSignature = thoughtSignatureBuffer;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _log.LogWarning(ex, "Failed to parse SSE data chunk: {Data}", Truncate(data, 100));
                        }

                        if (chunk != null)
                        {
                            yield return chunk;
                        }
                    }
                    yield break; // 正常读取完毕
                }
                finally
                {
                    reader.Dispose();
                    if (stream != null) await stream.DisposeAsync();
                    resp?.Dispose();
                }
            }

            throw new UpstreamException(HttpStatusCode.BadGateway, "Antigravity upstream failed after retries.");
        }
        finally
        {
            _requestGate.Release();
        }
    }

    // ---------- 动态模型发现 ----------

    public async Task<List<string>> RefreshAvailableModelsAsync(CancellationToken ct = default)
    {
        var url = $"{BaseUrl}:fetchAvailableModels";
        try
        {
            var token = await _account.GetTokenAsync(false, ct);
            using var msg = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
            };
            msg.Headers.Authorization = new("Bearer", token.Value);
            msg.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

            using var resp = await _http.SendAsync(msg, ct);
            if (resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("models", out var modelsObj) && modelsObj.ValueKind == JsonValueKind.Object)
                {
                    var found = new List<string>();
                    foreach (var prop in modelsObj.EnumerateObject())
                    {
                        found.Add(prop.Name);
                    }
                    if (found.Count > 0)
                    {
                        // 补充标准别名
                        if (!found.Contains("gemini-3.8-flash")) found.Insert(0, "gemini-3.8-flash");
                        if (!found.Contains("gpt-4o")) found.Add("gpt-4o");
                        _models = found;
                        _log.LogInformation("Discovered {Count} active models from Antigravity upstream", found.Count);
                        return found;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to fetch available models from upstream; using defaults");
        }
        return _models;
    }

    private static bool IsTransient(Exception ex) => ex is HttpRequestException or IOException or TimeoutException;

    private static TimeSpan ParseRetryDelay(string body)
    {
        var match = System.Text.RegularExpressions.Regex.Match(body, @"reset after (\d+)\s*s", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var s))
        {
            return TimeSpan.FromSeconds(Math.Clamp(s, 5, 120));
        }
        return TimeSpan.FromSeconds(15);
    }

    private async ValueTask WaitForCooldownAsync(CancellationToken ct)
    {
        DateTimeOffset until;
        lock (_cooldownLock) { until = _nextAllowedAt; }
        var delta = until - DateTimeOffset.UtcNow;
        if (delta > TimeSpan.Zero) await Task.Delay(delta, ct);
    }

    private void ApplySuccessCooldown()
    {
        lock (_cooldownLock)
        {
            var secs = Math.Max((int)_minInterval.TotalSeconds, _lastResetAfter);
            var target = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(secs);
            if (target > _nextAllowedAt) _nextAllowedAt = target;
        }
    }

    private void ApplyThrottleCooldown(int resetSecs)
    {
        lock (_cooldownLock)
        {
            _lastResetAfter = Math.Max(_lastResetAfter, resetSecs);
            var target = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(resetSecs);
            if (target > _nextAllowedAt) _nextAllowedAt = target;
        }
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Trim();
        return s.Length <= max ? s : s[..max] + "...";
    }
}
