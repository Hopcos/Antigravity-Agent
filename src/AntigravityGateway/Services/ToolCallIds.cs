using System.Text;
using System.Text.Json;

namespace AntigravityGateway.Services;

/// <summary>
/// 工具调用 id 编解码：Gemini 3.x（思考型模型）要求多轮工具循环中回传的
/// functionCall 必须携带生成时的 thought_signature（防重放校验）。
/// OpenAI 协议只允许在 tool_calls[].id 中携带不透明字符串，因此把
/// {函数名, thoughtSignature} 编码进 id，实现无状态往返：
///   id = "call_" + base64url( JSON{ n: 名称, s: 签名 } )
/// 客户端只要原样回传 id（assistant 消息的 tool_calls 与 tool 消息的
/// tool_call_id），网关即可还原出 Gemini 要求的字段。
/// </summary>
public static class ToolCallIds
{
    public static string Encode(string? name, string? signature)
    {
        var payload = JsonSerializer.Serialize(new { n = name ?? "", s = signature ?? "" });
        return "call_" + ToBase64Url(Encoding.UTF8.GetBytes(payload));
    }

    public static (string? Name, string? Signature) Decode(string? id)
    {
        if (string.IsNullOrEmpty(id) || !id.StartsWith("call_", StringComparison.Ordinal))
        {
            return (null, null);
        }
        try
        {
            var bytes = FromBase64Url(id[5..]);
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(bytes));
            var root = doc.RootElement;
            var name = root.TryGetProperty("n", out var n) ? n.GetString() : null;
            var sig = root.TryGetProperty("s", out var s) ? s.GetString() : null;
            return (string.IsNullOrEmpty(name) ? null : name, string.IsNullOrEmpty(sig) ? null : sig);
        }
        catch
        {
            return (null, null); // 非本网关生成的 id（如客户端自造）→ 走退化路径
        }
    }

    private static string ToBase64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        s = (s.Length % 4) switch
        {
            2 => s + "==",
            3 => s + "=",
            _ => s
        };
        return Convert.FromBase64String(s);
    }
}
