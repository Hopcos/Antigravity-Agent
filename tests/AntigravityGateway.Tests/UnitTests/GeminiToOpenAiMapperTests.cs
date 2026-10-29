using System.Text.Json;
using AntigravityGateway.Models.Gemini;
using AntigravityGateway.Services;
using Xunit;

namespace AntigravityGateway.Tests.UnitTests;

public class GeminiToOpenAiMapperTests
{
    private readonly GeminiToOpenAiMapper _mapper = new();

    [Fact]
    public void MapResponse_TranslatesTextAndReasoningTokens()
    {
        var geminiResp = new GeminiGenerateContentResponse
        {
            Candidates = new List<GeminiCandidate>
            {
                new()
                {
                    Content = new GeminiContent
                    {
                        Role = "model",
                        Parts = new List<GeminiPart>
                        {
                            new() { Text = "Hello from unit test!" }
                        }
                    },
                    FinishReason = "STOP"
                }
            },
            UsageMetadata = new GeminiUsageMetadata
            {
                PromptTokenCount = 10,
                CandidatesTokenCount = 20,
                TotalTokenCount = 70,
                ThoughtsTokenCount = 40
            }
        };

        var openAi = _mapper.MapResponse(geminiResp, "gemini-3.8-flash-high");

        Assert.NotNull(openAi);
        Assert.Equal("gemini-3.8-flash-high", openAi.Model);
        Assert.Single(openAi.Choices);
        Assert.Equal("Hello from unit test!", openAi.Choices[0].Message.Content);
        Assert.Equal("stop", openAi.Choices[0].FinishReason);

        Assert.NotNull(openAi.Usage);
        Assert.Equal(10, openAi.Usage.PromptTokens);
        Assert.Equal(60, openAi.Usage.CompletionTokens); // Math.Max(20, 70 - 10)
        Assert.Equal(70, openAi.Usage.TotalTokens);
        Assert.NotNull(openAi.Usage.CompletionTokensDetails);
        Assert.Equal(40, openAi.Usage.CompletionTokensDetails.ReasoningTokens);
    }

    [Fact]
    public void MapResponse_WithFunctionCall_SetsToolCallsAndFinishReason()
    {
        using var argsDoc = JsonDocument.Parse("{\"x\": 10}");
        var geminiResp = new GeminiGenerateContentResponse
        {
            Candidates = new List<GeminiCandidate>
            {
                new()
                {
                    Content = new GeminiContent
                    {
                        Role = "model",
                        Parts = new List<GeminiPart>
                        {
                            new()
                            {
                                FunctionCall = new GeminiFunctionCall
                                {
                                    Name = "calc",
                                    Args = argsDoc.RootElement.Clone()
                                },
                                ThoughtSignature = "sig_thought_99"
                            }
                        }
                    },
                    FinishReason = "STOP"
                }
            }
        };

        var openAi = _mapper.MapResponse(geminiResp, "gemini-3.8-flash-high");

        Assert.Equal("tool_calls", openAi.Choices[0].FinishReason);
        Assert.NotNull(openAi.Choices[0].Message.ToolCalls);
        Assert.Single(openAi.Choices[0].Message.ToolCalls!);

        var toolCall = openAi.Choices[0].Message.ToolCalls![0];
        Assert.Equal("calc", toolCall.Function.Name);

        var (name, sig) = ToolCallIds.Decode(toolCall.Id);
        Assert.Equal("calc", name);
        Assert.Equal("sig_thought_99", sig);
    }
}
