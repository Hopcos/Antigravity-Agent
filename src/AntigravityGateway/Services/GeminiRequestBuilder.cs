using System.Text.Json;
using AntigravityGateway.Models.Gemini;
using AntigravityGateway.Models.OpenAi;

namespace AntigravityGateway.Services;

/// <summary>OpenAI Chat Completions 请求 -> Gemini generateContent 请求。</summary>
public static class GeminiRequestBuilder
{
    public static GeminiGenerateContentRequest Build(ChatCompletionRequest req)
    {
        ArgumentNullException.ThrowIfNull(req);

        var gemini = new GeminiGenerateContentRequest();

        // system 消息 -> systemInstruction
        var systemParts = req.Messages
            .Where(m => m.Role.Equals("system", StringComparison.OrdinalIgnoreCase))
            .Select(m => new GeminiPart { Text = m.ContentString ?? "" })
            .ToList();
        if (systemParts.Count > 0)
        {
            gemini.SystemInstruction = new GeminiContent { Parts = systemParts };
        }

        foreach (var msg in req.Messages)
        {
            if (msg.Role.Equals("system", StringComparison.OrdinalIgnoreCase)) continue;

            var content = new GeminiContent();
            var contentStr = msg.ContentString;

            switch (msg.Role.ToLowerInvariant())
            {
                case "user":
                    content.Role = "user";
                    break;
                case "assistant":
                    content.Role = "model";
                    break;
                case "tool":
                    // tool 消息 → Gemini functionResponse（从工具调用 id 解码函数名）
                    var (fnName, _) = ToolCallIds.Decode(msg.ToolCallId);
                    content.Role = "user";
                    if (!string.IsNullOrEmpty(fnName))
                    {
                        content.Parts.Add(new GeminiPart
                        {
                            FunctionResponse = new GeminiFunctionResponse
                            {
                                Name = fnName,
                                Response = new { result = contentStr ?? "" }
                            }
                        });
                    }
                    else
                    {
                        content.Parts.Add(new GeminiPart
                        {
                            Text = $"[function result for {msg.ToolCallId ?? "?"}]: {contentStr ?? ""}"
                        });
                    }
                    gemini.Contents.Add(content);
                    continue;
                default:
                    content.Role = "user";
                    break;
            }

            if (!string.IsNullOrEmpty(contentStr))
            {
                content.Parts.Add(new GeminiPart { Text = contentStr });
            }

            // assistant 的 tool_calls -> functionCall parts（还原 thoughtSignature 满足防重放校验）
            if (msg.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) && msg.ToolCalls is not null)
            {
                foreach (var call in msg.ToolCalls)
                {
                    var (decodedName, signature) = ToolCallIds.Decode(call.Id);
                    var actualName = !string.IsNullOrEmpty(call.Function.Name) ? call.Function.Name : decodedName;
                    content.Parts.Add(new GeminiPart
                    {
                        FunctionCall = new GeminiFunctionCall
                        {
                            Name = actualName,
                            Args = TryParseJson(call.Function.Arguments),
                            Id = call.Id
                        },
                        ThoughtSignature = signature
                    });
                }
            }

            gemini.Contents.Add(content);
        }

        // 合并连续同角色 content
        var merged = new List<GeminiContent>(gemini.Contents.Count);
        foreach (var c in gemini.Contents)
        {
            var last = merged.Count > 0 ? merged[^1] : null;
            if (last is not null
                && string.Equals(last.Role, c.Role, StringComparison.Ordinal)
                && ShouldMerge(last, c))
            {
                last.Parts.AddRange(c.Parts);
            }
            else
            {
                merged.Add(c);
            }
        }
        gemini.Contents = merged;

        if (gemini.Contents.Count == 0 && gemini.SystemInstruction is null)
        {
            throw new ArgumentException("messages 不能为空");
        }

        var cfg = new GeminiGenerationConfig();
        var hasCfg = false;

        if (req.MaxTokens is > 0)
        {
            cfg.MaxOutputTokens = req.MaxTokens;
            hasCfg = true;
        }
        else if (req.MaxCompletionTokens is > 0)
        {
            cfg.MaxOutputTokens = req.MaxCompletionTokens;
            hasCfg = true;
        }
        if (req.Temperature is not null)
        {
            cfg.Temperature = req.Temperature;
            hasCfg = true;
        }
        if (req.TopP is not null)
        {
            cfg.TopP = req.TopP;
            hasCfg = true;
        }
        if (req.Stop is not null)
        {
            var stops = NormalizeStop(req.Stop);
            if (stops.Count > 0)
            {
                cfg.StopSequences = stops;
                hasCfg = true;
            }
        }
        if (hasCfg) gemini.GenerationConfig = cfg;

        if (req.Tools is { Count: > 0 })
        {
            gemini.Tools = req.Tools
                .Where(t => t.Type.Equals("function", StringComparison.OrdinalIgnoreCase))
                .Select(t => new GeminiTool
                {
                    FunctionDeclarations = new List<GeminiFunctionDeclaration>
                    {
                        new()
                        {
                            Name = t.Function.Name,
                            Description = t.Function.Description,
                            Parameters = SchemaNormalizer.Normalize(t.Function.Parameters)
                        }
                    }
                })
                .ToList();
        }

        // 避免以 model 回合结尾
        while (gemini.Contents.Count > 1 && gemini.Contents[^1].Role == "model")
        {
            gemini.Contents.RemoveAt(gemini.Contents.Count - 1);
        }
        if (gemini.Contents.Count == 1 && gemini.Contents[0].Role == "model")
        {
            throw new ArgumentException("messages 以 model/assistant 回合结尾且无任何 user/tool 回合，无法构造有效请求");
        }

        // 检查上一回合如果是带 thoughtSignature 的 functionCall，且当前回合是 functionResponse，则天然安全。
        // 若缺少 thoughtSignature，为防止 400 拦截，优雅降级尾部的 functionResponse 为用户文本结果。
        if (gemini.Contents.Count > 0)
        {
            var last = gemini.Contents[^1];
            if (string.Equals(last.Role, "user", StringComparison.Ordinal))
            {
                var frParts = last.Parts.Where(p => p.FunctionResponse is not null).ToList();
                // 检查前一个 model 节点中是否所有 functionCall 都有 thoughtSignature
                var prevModel = gemini.Contents.Count > 1 ? gemini.Contents[^2] : null;
                var hasMissingSignature = prevModel != null && prevModel.Parts.Any(p => p.FunctionCall != null && string.IsNullOrEmpty(p.ThoughtSignature));

                if (hasMissingSignature && frParts.Count > 0)
                {
                    // 缺少签名时，将 functionResponse 转换为纯文本 user 提示，防止被上游驳回
                    var text = string.Join("\n", frParts.Select(p => RenderFunctionResponse(p.FunctionResponse!)));
                    last.Parts.RemoveAll(p => p.FunctionResponse is not null);
                    last.Parts.Add(new GeminiPart { Text = text });
                }
            }
        }

        return gemini;
    }

    private static string RenderFunctionResponse(GeminiFunctionResponse fr)
    {
        var payload = fr.Response;
        var payloadText = payload switch
        {
            null => "(empty)",
            string s => s,
            _ when payload is JsonElement je => je.ValueKind == JsonValueKind.String ? je.GetString() ?? "(empty)" : je.ToString(),
            _ => payload.ToString() ?? "(empty)"
        };
        return $"[function result for {fr.Name}]: {payloadText}";
    }

    private static bool ShouldMerge(GeminiContent a, GeminiContent b)
    {
        static bool HasFr(GeminiContent c) => c.Parts.Any(p => p.FunctionResponse is not null);
        static bool HasText(GeminiContent c) => c.Parts.Any(p => p.Text is not null);

        var aFr = HasFr(a);
        var bFr = HasFr(b);
        if (!aFr && !bFr) return true;
        if (aFr && bFr) return !HasText(a) && !HasText(b);
        return false;
    }

    private static List<string> NormalizeStop(object stop)
    {
        var result = new List<string>();
        if (stop is JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.String)
            {
                var s = el.GetString();
                if (!string.IsNullOrEmpty(s)) result.Add(s);
            }
            else if (el.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in el.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var s = item.GetString();
                        if (!string.IsNullOrEmpty(s)) result.Add(s);
                    }
                }
            }
        }
        else if (stop is string str && !string.IsNullOrEmpty(str))
        {
            result.Add(str);
        }
        return result;
    }

    private static JsonElement? TryParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(new { raw = json });
        }
    }
}
