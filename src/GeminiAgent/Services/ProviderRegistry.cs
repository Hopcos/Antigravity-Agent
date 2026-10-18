using GeminiAgent.Configuration;

namespace GeminiAgent.Services;

/// <summary>
/// Provider 注册表：按模型名解析 Provider，网关模型路由的入口。
/// </summary>
public sealed class ProviderRegistry
{
    private readonly IReadOnlyList<IGeminiProvider> _providers;
    private readonly GatewayOptions _options;

    public ProviderRegistry(GatewayOptions options, IEnumerable<IGeminiProvider> providers)
    {
        _options = options;
        _providers = providers.ToList();
    }

    public string DefaultModel => string.IsNullOrWhiteSpace(_options.DefaultModel) ? "gemini-3.8-flash-high" : _options.DefaultModel;

    public IReadOnlyList<string> AllModels => _providers
        .SelectMany(p => p.Models)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    public IGeminiProvider Resolve(string? model)
    {
        var target = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
        var provider = _providers.FirstOrDefault(p =>
            p.Models.Contains(target, StringComparer.OrdinalIgnoreCase));

        // 如果未精确匹配模型，但当前有且仅有一个 Antigravity Provider，直接由其承接处理
        if (provider is null && _providers.Count > 0)
        {
            var antigravity = _providers.FirstOrDefault(p => p.Name.Equals("Antigravity", StringComparison.OrdinalIgnoreCase));
            if (antigravity != null)
            {
                return antigravity;
            }
        }

        if (provider is null)
        {
            throw new ModelNotFoundException(target);
        }
        return provider;
    }
}
