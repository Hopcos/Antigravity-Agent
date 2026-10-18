namespace GeminiAgent.Configuration;

/// <summary>顶层网关配置（与 appsettings.json 的 "Gateway" 节 + 环境变量合并）。</summary>
public sealed class GatewayOptions
{
    /// <summary>可选：调用方访问网关所需的 Bearer Key。为空则不鉴权。</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>请求未指定 model 时使用的默认模型。</summary>
    public string DefaultModel { get; set; } = "gemini-3.8-flash-high";

    public AuthOptions Auth { get; set; } = new();

    /// <summary>命名 Provider 集合。键 = Provider 名（如 "Antigravity", "CodeAssist", "Vertex"）。</summary>
    public Dictionary<string, ProviderOptions> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AuthOptions
{
    /// <summary>凭据来源模式：Auto (优先 Windows Credential Manager -> oauth_creds.json -> ADC), CredentialManager, File, Env。</summary>
    public string Source { get; set; } = "Auto";

    /// <summary>Windows 凭据管理器目标名（默认 gemini:antigravity）。</summary>
    public string CredentialTarget { get; set; } = "gemini:antigravity";

    /// <summary>Antigravity / Gemini OAuth 凭据文件路径（结构包含 refresh_token / access_token）。</summary>
    public string? TokenFile { get; set; }

    /// <summary>Google ADC JSON 文件路径。</summary>
    public string? AdcFile { get; set; }

    /// <summary>显式指定 OAuth 客户端信息与 refresh token（优先级最高）。</summary>
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? RefreshToken { get; set; }
}

public sealed class ProviderOptions
{
    /// <summary>Provider 实现类型名："Antigravity"（默认推荐）、"CodeAssist" 或 "Vertex"。</summary>
    public string Type { get; set; } = "Antigravity";

    /// <summary>项目 ID。Antigravity 默认为 "aicode-consumers"。</summary>
    public string Project { get; set; } = "aicode-consumers";

    /// <summary>区域（默认 global）。</summary>
    public string Location { get; set; } = "global";

    /// <summary>该 Provider 可服务的模型列表（OpenAI 侧可见/可路由的模型名）。</summary>
    public List<string> Models { get; set; } = new();

    /// <summary>可选：API 基础地址覆盖。</summary>
    public string? BaseUrl { get; set; }

    /// <summary>上游实际使用的模型名（若为空则使用请求模型或默认）。</summary>
    public string? UpstreamModel { get; set; }

    /// <summary>请求 User-Agent。</summary>
    public string? UserAgent { get; set; }

    /// <summary>请求 x-goog-api-client 请求头。</summary>
    public string? ApiClientHeader { get; set; }

    /// <summary>最大并发请求数（默认 2）。</summary>
    public int MaxConcurrency { get; set; } = 2;

    /// <summary>相邻两次后端请求的最小间隔（秒，默认 0 = 不强制）。</summary>
    public int MinInterval { get; set; } = 0;

    /// <summary>429 时的最大重试次数（默认 4）。</summary>
    public int MaxAttempts { get; set; } = 4;
}
