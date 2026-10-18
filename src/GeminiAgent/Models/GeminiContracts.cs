using System.Text.Json;
using System.Text.Json.Serialization;

namespace GeminiAgent.Models.Gemini;

public sealed class GeminiGenerateContentRequest
{
    [JsonPropertyName("contents")] public List<GeminiContent> Contents { get; set; } = new();

    [JsonPropertyName("systemInstruction")] public GeminiContent? SystemInstruction { get; set; }

    [JsonPropertyName("generationConfig")] public GeminiGenerationConfig? GenerationConfig { get; set; }

    [JsonPropertyName("tools")] public List<GeminiTool>? Tools { get; set; }
}

public sealed class GeminiContent
{
    [JsonPropertyName("role")] public string? Role { get; set; }

    [JsonPropertyName("parts")] public List<GeminiPart> Parts { get; set; } = new();
}

public sealed class GeminiPart
{
    [JsonPropertyName("text")] public string? Text { get; set; }

    [JsonPropertyName("functionCall")] public GeminiFunctionCall? FunctionCall { get; set; }

    [JsonPropertyName("functionResponse")] public GeminiFunctionResponse? FunctionResponse { get; set; }

    /// <summary>思考型模型的防重放签名；functionCall 回传时必须原样携带。</summary>
    [JsonPropertyName("thoughtSignature")] public string? ThoughtSignature { get; set; }
}

public sealed class GeminiFunctionCall
{
    [JsonPropertyName("name")] public string? Name { get; set; }

    [JsonPropertyName("args")] public JsonElement? Args { get; set; }

    [JsonPropertyName("id")] public string? Id { get; set; }
}

public sealed class GeminiFunctionResponse
{
    [JsonPropertyName("name")] public string? Name { get; set; }

    [JsonPropertyName("response")] public object? Response { get; set; }
}

public sealed class GeminiGenerationConfig
{
    [JsonPropertyName("maxOutputTokens")] public int? MaxOutputTokens { get; set; }

    [JsonPropertyName("temperature")] public double? Temperature { get; set; }

    [JsonPropertyName("topP")] public double? TopP { get; set; }

    [JsonPropertyName("stopSequences")] public List<string>? StopSequences { get; set; }
}

public sealed class GeminiTool
{
    [JsonPropertyName("functionDeclarations")] public List<GeminiFunctionDeclaration> FunctionDeclarations { get; set; } = new();
}

public sealed class GeminiFunctionDeclaration
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";

    [JsonPropertyName("description")] public string? Description { get; set; }

    [JsonPropertyName("parameters")] public JsonElement? Parameters { get; set; }
}

public sealed class GeminiGenerateContentResponse
{
    [JsonPropertyName("candidates")] public List<GeminiCandidate>? Candidates { get; set; }

    [JsonPropertyName("usageMetadata")] public GeminiUsageMetadata? UsageMetadata { get; set; }

    [JsonPropertyName("modelVersion")] public string? ModelVersion { get; set; }
}

public sealed class GeminiCandidate
{
    [JsonPropertyName("content")] public GeminiContent? Content { get; set; }

    [JsonPropertyName("finishReason")] public string? FinishReason { get; set; }
}

public sealed class GeminiUsageMetadata
{
    [JsonPropertyName("promptTokenCount")] public int PromptTokenCount { get; set; }

    [JsonPropertyName("candidatesTokenCount")] public int CandidatesTokenCount { get; set; }

    [JsonPropertyName("totalTokenCount")] public int TotalTokenCount { get; set; }

    [JsonPropertyName("thoughtsTokenCount")] public int? ThoughtsTokenCount { get; set; }

    [JsonPropertyName("trafficType")] public string? TrafficType { get; set; }

    [JsonPropertyName("promptTokensDetails")] public List<GeminiTokenDetail>? PromptTokensDetails { get; set; }

    [JsonPropertyName("candidatesTokensDetails")] public List<GeminiTokenDetail>? CandidatesTokensDetails { get; set; }
}

public sealed class GeminiTokenDetail
{
    [JsonPropertyName("modality")] public string? Modality { get; set; }

    [JsonPropertyName("tokenCount")] public int TokenCount { get; set; }
}

public sealed class GeminiGenerateContentStreamChunk
{
    [JsonPropertyName("candidates")] public List<GeminiCandidate>? Candidates { get; set; }

    [JsonPropertyName("usageMetadata")] public GeminiUsageMetadata? UsageMetadata { get; set; }

    [JsonPropertyName("modelVersion")] public string? ModelVersion { get; set; }
}
