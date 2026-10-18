using System.Security.Cryptography;
using GeminiAgent.Models.Gemini;
using GeminiAgent.Models.OpenAi;

namespace GeminiAgent.Services;

/// <summary>Gemini generateContent 响应 -> OpenAI Chat Completions（完整 + 流式块）。</summary>
public sealed class GeminiToOpenAiMapper
{
    public ChatCompletionResponse MapResponse(GeminiGenerateContentResponse from, string model)
    {
        var candidate = from.Candidates?.FirstOrDefault();
        var message = new ChatResponseMessage();

        if (candidate?.Content is { } content)
        {
            var texts = content.Parts
                .Where(p => !string.IsNullOrEmpty(p.Text))
                .Select(p => p.Text);
            message.Content = string.Concat(texts);

            var calls = content.Parts
                .Where(p => p.FunctionCall is not null)
                .Select(p => new OpenAiToolCall
                {
                    Id = ToolCallIds.Encode(p.FunctionCall!.Name, p.ThoughtSignature),
                    Function = new OpenAiToolCallFunction
                    {
                        Name = p.FunctionCall!.Name,
                        Arguments = p.FunctionCall.Args?.ToString() ?? "{}"
                    }
                })
                .ToList();
            if (calls.Count > 0) message.ToolCalls = calls;
        }

        var finishReason = message.ToolCalls is { Count: > 0 }
            ? "tool_calls"
            : MapFinishReason(candidate?.FinishReason);

        return new ChatCompletionResponse
        {
            Id = "chatcmpl-" + RandomNumberGenerator.GetHexString(24),
            Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Model = model,
            Choices = new List<ChatChoice>
            {
                new()
                {
                    Index = 0,
                    Message = message,
                    FinishReason = finishReason
                }
            },
            Usage = MapUsage(from.UsageMetadata)
        };
    }

    public ChatUsage? MapUsage(GeminiUsageMetadata? u)
    {
        if (u is null) return null;
        var thoughts = u.ThoughtsTokenCount ?? 0;
        var completion = u.TotalTokenCount > 0
            ? Math.Max(u.CandidatesTokenCount, u.TotalTokenCount - u.PromptTokenCount)
            : u.CandidatesTokenCount;
        return new ChatUsage
        {
            PromptTokens = u.PromptTokenCount,
            CompletionTokens = completion,
            TotalTokens = u.TotalTokenCount,
            CompletionTokensDetails = thoughts > 0
                ? new ChatUsageCompletionDetails { ReasoningTokens = thoughts }
                : null
        };
    }

    public ChatCompletionChunk MapChunk(GeminiGenerateContentStreamChunk from, string model, string id, long created)
    {
        var candidate = from.Candidates?.FirstOrDefault();
        string? deltaText = null;
        List<OpenAiToolCall>? deltaCalls = null;

        if (candidate?.Content is { } content)
        {
            deltaText = string.Concat(content.Parts
                .Where(p => !string.IsNullOrEmpty(p.Text))
                .Select(p => p.Text));
            if (string.IsNullOrEmpty(deltaText)) deltaText = null;

            deltaCalls = content.Parts
                .Where(p => p.FunctionCall is not null)
                .Select(p => new OpenAiToolCall
                {
                    Id = ToolCallIds.Encode(p.FunctionCall!.Name, p.ThoughtSignature),
                    Function = new OpenAiToolCallFunction
                    {
                        Name = p.FunctionCall!.Name,
                        Arguments = p.FunctionCall.Args?.ToString() ?? "{}"
                    }
                })
                .ToList();
            if (deltaCalls.Count == 0) deltaCalls = null;
        }

        var finishReason = deltaCalls is not null
            ? "tool_calls"
            : (string.IsNullOrEmpty(candidate?.FinishReason) ? null : MapFinishReason(candidate!.FinishReason));

        return new ChatCompletionChunk
        {
            Id = id,
            Created = created,
            Model = model,
            Choices = new List<ChatChunkChoice>
            {
                new()
                {
                    Index = 0,
                    Delta = new ChatChunkDelta
                    {
                        Content = deltaText,
                        ToolCalls = deltaCalls
                    },
                    FinishReason = finishReason
                }
            }
        };
    }

    private static string? MapFinishReason(string? reason) => reason?.ToUpperInvariant() switch
    {
        null or "" => null,
        "STOP" => "stop",
        "MAX_TOKENS" => "length",
        "SAFETY" or "BLOCKLIST" or "PROHIBITED_CONTENT" or "RECITATION" => "content_filter",
        "FUNCTION_CALL" or "TOOL_CALL" => "tool_calls",
        _ => "stop"
    };
}
