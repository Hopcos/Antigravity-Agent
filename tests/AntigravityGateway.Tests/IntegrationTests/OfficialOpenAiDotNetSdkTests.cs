using System.ClientModel;
using OpenAI;
using OpenAI.Chat;
using Xunit;

namespace AntigravityGateway.Tests.IntegrationTests;

public class OfficialOpenAiDotNetSdkTests
{
    private readonly ChatClient _chatClient;

    public OfficialOpenAiDotNetSdkTests()
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri("http://127.0.0.1:5055/v1")
        };
        var client = new OpenAIClient(new ApiKeyCredential("sk-antigravity"), options);
        _chatClient = client.GetChatClient("gemini-3.8-flash-high");
    }

    [Fact]
    public async Task OfficialSdk_CompleteChat_ReturnsResponse()
    {
        ChatCompletion completion = await _chatClient.CompleteChatAsync("What is the capital of France? Reply in one word.");

        Assert.NotNull(completion);
        Assert.NotEmpty(completion.Content);
        var text = string.Concat(completion.Content.Select(c => c.Text));
        Assert.Contains("Paris", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ChatFinishReason.Stop, completion.FinishReason);
    }

    [Fact]
    public async Task OfficialSdk_CompleteChatStreaming_ReceivesUpdates()
    {
        var streamedChunks = 0;
        var accumulated = new System.Text.StringBuilder();

        AsyncCollectionResult<StreamingChatCompletionUpdate> updates =
            _chatClient.CompleteChatStreamingAsync("Count 1, 2, 3 in order");

        await foreach (StreamingChatCompletionUpdate update in updates)
        {
            streamedChunks++;
            foreach (ChatMessageContentPart part in update.ContentUpdate)
            {
                accumulated.Append(part.Text);
            }
        }

        Assert.True(streamedChunks > 0);
        Assert.NotEmpty(accumulated.ToString());
    }

    [Fact]
    public async Task OfficialSdk_MultiTurnToolCalling_ExecutesSuccessfully()
    {
        ChatTool calculateTool = ChatTool.CreateFunctionTool(
            functionName: "calculate",
            functionDescription: "Calculates mathematical expressions",
            functionParameters: BinaryData.FromString("""
            {
                "type": "object",
                "properties": {
                    "expr": { "type": "string", "description": "Expression like '123 * 456'" }
                },
                "required": ["expr"]
            }
            """)
        );

        var options = new ChatCompletionOptions
        {
            Tools = { calculateTool }
        };

        var messages = new List<ChatMessage>
        {
            new UserChatMessage("What is 123 * 456? Use the calculate tool.")
        };

        // 1. First turn: Model triggers tool call
        ChatCompletion firstResponse = await _chatClient.CompleteChatAsync(messages, options);

        Assert.Equal(ChatFinishReason.ToolCalls, firstResponse.FinishReason);
        Assert.NotEmpty(firstResponse.ToolCalls);

        ChatToolCall toolCall = firstResponse.ToolCalls[0];
        Assert.Equal("calculate", toolCall.FunctionName);
        Assert.StartsWith("call_", toolCall.Id);

        // 2. Second turn: Provide tool result
        messages.Add(new AssistantChatMessage(firstResponse));
        messages.Add(new ToolChatMessage(toolCall.Id, "56088"));

        ChatCompletion secondResponse = await _chatClient.CompleteChatAsync(messages, options);

        Assert.NotNull(secondResponse);
        var finalText = string.Concat(secondResponse.Content.Select(c => c.Text));
        Assert.Contains("56", finalText);
        Assert.Contains("088", finalText);
    }
}
