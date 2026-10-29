using AntigravityGateway.Models.Gemini;

namespace AntigravityGateway.Services;

/// <summary>
/// 模型 Provider 抽象：一个 Provider = 一种可达的后端接入方式 + 一组可服务模型。
/// </summary>
public interface IGeminiProvider
{
    string Name { get; }
    string Project { get; }
    string Location { get; }
    IReadOnlyList<string> Models { get; }

    Task<GeminiGenerateContentResponse> GenerateAsync(
        string model, GeminiGenerateContentRequest request, CancellationToken ct);

    IAsyncEnumerable<GeminiGenerateContentStreamChunk> GenerateStreamAsync(
        string model, GeminiGenerateContentRequest request, CancellationToken ct);
}
