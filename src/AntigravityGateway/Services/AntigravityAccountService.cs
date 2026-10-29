using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using AntigravityGateway.Configuration;

namespace AntigravityGateway.Services;

public readonly record struct AccessToken(string Value, DateTimeOffset ExpiresUtc, int TtlSeconds)
{
    public static readonly AccessToken Empty = new("", DateTimeOffset.MinValue, 0);

    public bool IsValidAt(DateTimeOffset now, int marginSeconds = 60) =>
        !string.IsNullOrEmpty(Value) && ExpiresUtc - now > TimeSpan.FromSeconds(marginSeconds);
}

public sealed class AccountStatus
{
    public string Email { get; set; } = "unknown";
    public string Source { get; set; } = "unknown";
    public bool HasToken { get; set; }
    public DateTimeOffset? ExpiresUtc { get; set; }
    public int? RemainingSeconds => ExpiresUtc.HasValue ? (int)Math.Max(0, (ExpiresUtc.Value - DateTimeOffset.UtcNow).TotalSeconds) : null;
    public string? Scopes { get; set; }
}

/// <summary>
/// Antigravity 账号与凭据管理服务：
/// 1. 自动从 Windows Credential Manager（gemini:antigravity）探测当前登录的 Antigravity 账号；
/// 2. 具备多级降级兜底：Windows Credential Manager -> ~/.gemini/oauth_creds.json -> 环境变量 -> ADC；
/// 3. 管理双 Token 永续生命周期：基于 Google OAuth 官方 Antigravity 客户端及 Refresh Token 无限静默换取 Access Token；
/// 4. 支持懒刷新、主动保活刷新（后台服务每分钟检查）与 401 反应式刷新重试。
/// </summary>
public sealed class AntigravityAccountService
{
    public const string DefaultAntigravityClientId =
        "1071006060591-tmhssin2h21lcre235vtolojh4g403ep.apps.googleusercontent.com";
    public const string DefaultAntigravityClientSecret =
        "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";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    private readonly HttpClient _http;
    private readonly AuthOptions _auth;
    private readonly ILogger<AntigravityAccountService> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _credsGate = new(1, 1);

    private ResolvedCredentials? _creds;
    private AccessToken _current = AccessToken.Empty;

    public AntigravityAccountService(AuthOptions auth, ILogger<AntigravityAccountService> log)
    {
        _auth = auth;
        _log = log;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    private sealed record ResolvedCredentials(
        string ClientId,
        string ClientSecret,
        string RefreshToken,
        string Email,
        string Source,
        string? InitialAccessToken,
        DateTimeOffset? InitialExpiry);

    public AccountStatus GetStatus()
    {
        return new AccountStatus
        {
            Email = _creds?.Email ?? "detecting...",
            Source = _creds?.Source ?? _auth.Source,
            HasToken = !string.IsNullOrEmpty(_current.Value),
            ExpiresUtc = _current.ExpiresUtc == DateTimeOffset.MinValue ? null : _current.ExpiresUtc,
            Scopes = "https://www.googleapis.com/auth/aicode https://www.googleapis.com/auth/cloud-platform"
        };
    }

    /// <summary>获取有效的 Access Token；force=true 时强制触发刷新。</summary>
    public async Task<AccessToken> GetTokenAsync(bool force = false, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        if (!force && _current.IsValidAt(now))
        {
            return _current;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (!force && _current.IsValidAt(DateTimeOffset.UtcNow))
            {
                return _current;
            }

            _current = await RefreshCoreAsync(ct);
            return _current;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<AccessToken> ForceRefreshAsync(CancellationToken ct = default) => GetTokenAsync(true, ct);

    /// <summary>供后台保活服务定期检查：剩余有效期不足阈值时提前续期。</summary>
    public Task RefreshIfExpiringSoonAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var t = _current;
        var threshold = TimeSpan.FromSeconds(Math.Max(t.TtlSeconds / 10, 300)); // 剩余不足 10% 或少于 5 分钟
        if (!t.IsValidAt(now, 0) || t.ExpiresUtc - now < threshold)
        {
            _log.LogInformation("Token approaching expiry (expires in {Secs:F0}s); initiating proactive background renewal",
                (t.ExpiresUtc - now).TotalSeconds);
            return GetTokenAsync(force: true, ct);
        }
        return Task.CompletedTask;
    }

    private async Task<AccessToken> RefreshCoreAsync(CancellationToken ct)
    {
        var creds = await GetCredentialsAsync(ct);

        // 如果首次加载且已有可用的初始 token，且未过期，直接使用
        if (_current == AccessToken.Empty && !string.IsNullOrEmpty(creds.InitialAccessToken) && creds.InitialExpiry.HasValue)
        {
            var initialExp = creds.InitialExpiry.Value;
            if (initialExp > DateTimeOffset.UtcNow.AddSeconds(60))
            {
                var initialTtl = (int)Math.Max(60, (initialExp - DateTimeOffset.UtcNow).TotalSeconds);
                _log.LogInformation("Using active token from {Source} for account {Email} (valid until {Exp:O})",
                    creds.Source, creds.Email, initialExp);
                return new AccessToken(creds.InitialAccessToken, initialExp, initialTtl);
            }
        }

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = creds.ClientId,
            ["client_secret"] = creds.ClientSecret,
            ["refresh_token"] = creds.RefreshToken,
            ["grant_type"] = "refresh_token"
        });

        using var resp = await _http.PostAsync(TokenEndpoint, form, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            var hint = body.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase)
                ? "Antigravity refresh token 已失效或被撤销，请在 Antigravity 重新登录账号"
                : "请检查 Antigravity 凭据配置";
            throw new TokenRefreshException($"Token refresh failed ({(int)resp.StatusCode}): {hint}. {body}");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var access = root.GetProperty("access_token").GetString();
        if (string.IsNullOrEmpty(access))
        {
            throw new TokenRefreshException("OAuth token endpoint returned no access_token");
        }

        var ttl = root.TryGetProperty("expires_in", out var exp) && exp.TryGetInt32(out var e) ? e : 3600;
        ttl = Math.Max(ttl, 60);
        var expiresUtc = DateTimeOffset.UtcNow.AddSeconds(ttl - 60); // 预留 60s 安全保护期

        _log.LogInformation("Antigravity token successfully refreshed for {Email} (valid ~{Ttl}s, until {Exp:yyyy-MM-dd HH:mm:ss})",
            creds.Email, ttl, expiresUtc.ToLocalTime());

        return new AccessToken(access, expiresUtc, ttl);
    }

    private async ValueTask<ResolvedCredentials> GetCredentialsAsync(CancellationToken ct)
    {
        if (_creds is not null) return _creds;

        await _credsGate.WaitAsync(ct);
        try
        {
            if (_creds is not null) return _creds;
            _creds = ResolveCredentials();
            return _creds;
        }
        finally
        {
            _credsGate.Release();
        }
    }

    private ResolvedCredentials ResolveCredentials()
    {
        // 1. 显式环境变量优先
        if (!string.IsNullOrWhiteSpace(_auth.RefreshToken))
        {
            var clientId = string.IsNullOrWhiteSpace(_auth.ClientId) ? DefaultAntigravityClientId : _auth.ClientId;
            var clientSecret = string.IsNullOrWhiteSpace(_auth.ClientSecret) ? DefaultAntigravityClientSecret : _auth.ClientSecret;
            _log.LogInformation("Loaded Antigravity refresh credentials from environment variables");
            return new ResolvedCredentials(clientId, clientSecret, _auth.RefreshToken, "env-configured", "Environment", null, null);
        }

        // 2. Windows 凭据管理器探测 (Target: gemini:antigravity)
        if (OperatingSystem.IsWindows() && !_auth.Source.Equals("File", StringComparison.OrdinalIgnoreCase))
        {
            var winCred = TryReadWindowsCredential(_auth.CredentialTarget);
            if (winCred is not null)
            {
                _log.LogInformation("Discovered Antigravity account from Windows Credential Manager [{Target}]: {Email}",
                    _auth.CredentialTarget, winCred.Email);
                return winCred;
            }
        }

        // 3. ~/.gemini/oauth_creds.json 文件探测
        var tokenFile = !string.IsNullOrEmpty(_auth.TokenFile) ? _auth.TokenFile : DefaultTokenFilePath();
        if (File.Exists(tokenFile))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(tokenFile));
                var root = doc.RootElement;
                var rt = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
                var at = root.TryGetProperty("access_token", out var a) ? a.GetString() : null;
                var cid = root.TryGetProperty("client_id", out var c) ? c.GetString() : null;
                var sec = root.TryGetProperty("client_secret", out var s) ? s.GetString() : null;

                if (!string.IsNullOrEmpty(rt))
                {
                    var email = ExtractEmailFromIdToken(root.TryGetProperty("id_token", out var idt) ? idt.GetString() : null) ?? "local-gemini-user";
                    _log.LogInformation("Loaded Antigravity credentials from {File} for account {Email}", tokenFile, email);
                    return new ResolvedCredentials(
                        string.IsNullOrEmpty(cid) ? DefaultAntigravityClientId : cid,
                        string.IsNullOrEmpty(sec) ? DefaultAntigravityClientSecret : sec,
                        rt,
                        email,
                        "File (~/.gemini/oauth_creds.json)",
                        at,
                        null);
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to parse token file {Path}", tokenFile);
            }
        }

        // 4. gcloud ADC 文件探测
        var adcFile = !string.IsNullOrEmpty(_auth.AdcFile) ? _auth.AdcFile : DefaultAdcFilePath();
        if (File.Exists(adcFile))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(adcFile));
                var root = doc.RootElement;
                var cid = root.TryGetProperty("client_id", out var c) ? c.GetString() : null;
                var sec = root.TryGetProperty("client_secret", out var s) ? s.GetString() : null;
                var rt = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
                var acct = root.TryGetProperty("account", out var acc) ? acc.GetString() : "adc-user";

                if (!string.IsNullOrEmpty(cid) && !string.IsNullOrEmpty(sec) && !string.IsNullOrEmpty(rt))
                {
                    _log.LogInformation("Loaded ADC credentials from {File} for account {Email}", adcFile, acct);
                    return new ResolvedCredentials(cid, sec, rt, acct ?? "adc-user", "ADC File", null, null);
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to parse ADC file {Path}", adcFile);
            }
        }

        throw new TokenRefreshException(
            "未找到有效的 Antigravity 登录凭据！请确保本机已在 Antigravity 中登录账号，或在 Windows Credential Manager 中存在 'gemini:antigravity'，或设置 ANTIGRAVITY_REFRESH_TOKEN。");
    }

    private static ResolvedCredentials? TryReadWindowsCredential(string targetName)
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            if (CredRead(targetName, 1 /* CRED_TYPE_GENERIC */, 0, out var pCred))
            {
                try
                {
                    var cred = Marshal.PtrToStructure<NativeCredential>(pCred);
                    if (cred.CredentialBlobSize > 0 && cred.CredentialBlob != IntPtr.Zero)
                    {
                        var bytes = new byte[cred.CredentialBlobSize];
                        Marshal.Copy(cred.CredentialBlob, bytes, 0, (int)cred.CredentialBlobSize);
                        var json = Encoding.UTF8.GetString(bytes);

                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;

                        string? refreshToken = null;
                        string? accessToken = null;
                        DateTimeOffset? expiry = null;

                        if (root.TryGetProperty("token", out var tok))
                        {
                            refreshToken = tok.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
                            accessToken = tok.TryGetProperty("access_token", out var at) ? at.GetString() : null;
                            if (tok.TryGetProperty("expiry", out var exp) && exp.GetString() is { } expStr)
                            {
                                if (DateTimeOffset.TryParse(expStr, out var dto)) expiry = dto;
                            }
                        }

                        if (string.IsNullOrEmpty(refreshToken))
                        {
                            refreshToken = root.TryGetProperty("refresh_token", out var rt2) ? rt2.GetString() : null;
                        }

                        if (!string.IsNullOrEmpty(refreshToken))
                        {
                            var idToken = root.TryGetProperty("id_token", out var idt) ? idt.GetString() : null;
                            var email = ExtractEmailFromIdToken(idToken) ?? cred.UserName ?? "antigravity-user";

                            return new ResolvedCredentials(
                                DefaultAntigravityClientId,
                                DefaultAntigravityClientSecret,
                                refreshToken,
                                email,
                                "Windows Credential Manager (gemini:antigravity)",
                                accessToken,
                                expiry);
                        }
                    }
                }
                finally
                {
                    CredFree(pCred);
                }
            }
        }
        catch
        {
            // Windows Credential Manager 读取异常降级
        }
        return null;
    }

    private static string? ExtractEmailFromIdToken(string? idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken)) return null;
        try
        {
            var parts = idToken.Split('.');
            if (parts.Length < 2) return null;
            var payload = parts[1];
            payload = payload.Replace('-', '+').Replace('_', '/');
            payload = (payload.Length % 4) switch
            {
                2 => payload + "==",
                3 => payload + "=",
                _ => payload
            };
            var bytes = Convert.FromBase64String(payload);
            using var doc = JsonDocument.Parse(bytes);
            return doc.RootElement.TryGetProperty("email", out var em) ? em.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    private static string DefaultTokenFilePath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".gemini", "oauth_creds.json");
    }

    private static string DefaultAdcFilePath()
    {
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetEnvironmentVariable("APPDATA");
            if (!string.IsNullOrEmpty(appData))
            {
                return Path.Combine(appData, "gcloud", "application_default_credentials.json");
            }
        }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config", "gcloud", "application_default_credentials.json");
    }

    // Windows P/Invoke
    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("Advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
    private static extern void CredFree(IntPtr credentialPtr);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }
}
