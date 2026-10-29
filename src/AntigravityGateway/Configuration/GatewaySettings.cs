namespace AntigravityGateway.Configuration;

/// <summary>加载并合并配置：appsettings.json 的 "Gateway" 节 &lt;- 环境变量（环境变量优先）。</summary>
public static class GatewaySettings
{
    public static GatewayOptions Load(IConfiguration config)
    {
        var opts = new GatewayOptions();
        config.GetSection("Gateway").Bind(opts);

        // 顶层环境变量
        opts.ApiKey = Env("GEMINI_API_KEY") ?? Env("GATEWAY_API_KEY") ?? opts.ApiKey;
        opts.DefaultModel = Env("GEMINI_DEFAULT_MODEL") ?? opts.DefaultModel;

        opts.Auth ??= new AuthOptions();
        opts.Auth.Source = Env("ANTIGRAVITY_AUTH_SOURCE") ?? opts.Auth.Source;
        opts.Auth.CredentialTarget = Env("ANTIGRAVITY_CREDENTIAL_TARGET") ?? opts.Auth.CredentialTarget;
        opts.Auth.TokenFile = Env("ANTIGRAVITY_TOKEN_FILE") ?? opts.Auth.TokenFile;
        opts.Auth.AdcFile = Env("GEMINI_ADCFILE") ?? opts.Auth.AdcFile;
        opts.Auth.ClientId = Env("ANTIGRAVITY_CLIENT_ID") ?? Env("GEMINI_CLIENT_ID") ?? opts.Auth.ClientId;
        opts.Auth.ClientSecret = Env("ANTIGRAVITY_CLIENT_SECRET") ?? Env("GEMINI_CLIENT_SECRET") ?? opts.Auth.ClientSecret;
        opts.Auth.RefreshToken = Env("ANTIGRAVITY_REFRESH_TOKEN") ?? Env("GEMINI_REFRESH_TOKEN") ?? opts.Auth.RefreshToken;

        // —— 默认配置 Antigravity Provider ——
        var antigravity = opts.Providers.GetValueOrDefault("Antigravity");
        if (antigravity is null)
        {
            antigravity = new ProviderOptions
            {
                Type = "Antigravity",
                Project = "aicode-consumers",
                Location = "global",
                BaseUrl = "https://daily-cloudcode-pa.googleapis.com/v1internal",
                UserAgent = "antigravity/2.24.0 (win32; x64)",
                MaxConcurrency = 2,
                Models = new List<string>
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
                }
            };
            opts.Providers["Antigravity"] = antigravity;
        }

        // 环境变量覆盖
        antigravity.Project = Env("ANTIGRAVITY_PROJECT") ?? antigravity.Project;
        antigravity.BaseUrl = Env("ANTIGRAVITY_BASE_URL") ?? antigravity.BaseUrl;
        antigravity.UserAgent = Env("ANTIGRAVITY_USER_AGENT") ?? antigravity.UserAgent;
        if (int.TryParse(Env("ANTIGRAVITY_MAX_CONCURRENCY"), out var mc) && mc > 0) antigravity.MaxConcurrency = mc;
        if (int.TryParse(Env("ANTIGRAVITY_MIN_INTERVAL"), out var mi) && mi >= 0) antigravity.MinInterval = mi;

        var modelsEnv = Env("ANTIGRAVITY_MODELS");
        if (!string.IsNullOrWhiteSpace(modelsEnv))
        {
            antigravity.Models = modelsEnv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }

        return opts;
    }

    private static string? Env(string name) => Environment.GetEnvironmentVariable(name);
}
