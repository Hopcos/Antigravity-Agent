using GeminiAgent.Models.OpenAi;
using GeminiAgent.Services;
using Xunit;

namespace GeminiAgent.Tests.UnitTests;

public class GeminiRequestBuilderTests
{
    [Fact]
    public void Build_TranslatesSystemAndUserMessages()
    {
        var req = new ChatCompletionRequest
        {
            Model = "gemini-3.8-flash-high",
            Messages = new List<ChatMessage>
            {
                new() { Role = "system", Content = "You are a test assistant." },
                new() { Role = "user", Content = "Hello!" }
            }
        };

        var gemini = GeminiRequestBuilder.Build(req);

        Assert.NotNull(gemini.SystemInstruction);
        Assert.Single(gemini.SystemInstruction.Parts);
        Assert.Equal("You are a test assistant.", gemini.SystemInstruction.Parts[0].Text);

        Assert.Single(gemini.Contents);
        Assert.Equal("user", gemini.Contents[0].Role);
        Assert.Equal("Hello!", gemini.Contents[0].Parts[0].Text);
    }

    [Fact]
    public void Build_MultiTurnToolInteraction_RestoresThoughtSignatureAndFunctionResponse()
    {
        var toolName = "calculator";
        var thoughtSig = "test_signature_xyz_123";
        var toolCallId = ToolCallIds.Encode(toolName, thoughtSig);

        var req = new ChatCompletionRequest
        {
            Model = "gemini-3.8-flash-high",
            Messages = new List<ChatMessage>
            {
                new() { Role = "user", Content = "What is 2+2?" },
                new()
                {
                    Role = "assistant",
                    ToolCalls = new List<OpenAiToolCall>
                    {
                        new()
                        {
                            Id = toolCallId,
                            Function = new OpenAiToolCallFunction
                            {
                                Name = toolName,
                                Arguments = "{\"expr\":\"2+2\"}"
                            }
                        }
                    }
                },
                new()
                {
                    Role = "tool",
                    ToolCallId = toolCallId,
                    Content = "4"
                }
            }
        };

        var gemini = GeminiRequestBuilder.Build(req);

        Assert.Equal(3, gemini.Contents.Count);

        // Turn 1: user
        Assert.Equal("user", gemini.Contents[0].Role);

        // Turn 2: assistant / model with thought signature
        Assert.Equal("model", gemini.Contents[1].Role);
        var fnCallPart = gemini.Contents[1].Parts[0];
        Assert.NotNull(fnCallPart.FunctionCall);
        Assert.Equal(toolName, fnCallPart.FunctionCall.Name);
        Assert.Equal(thoughtSig, fnCallPart.ThoughtSignature);

        // Turn 3: tool / user function response with decoded tool name
        Assert.Equal("user", gemini.Contents[2].Role);
        var fnRespPart = gemini.Contents[2].Parts[0];
        Assert.NotNull(fnRespPart.FunctionResponse);
        Assert.Equal(toolName, fnRespPart.FunctionResponse.Name);
    }
}
