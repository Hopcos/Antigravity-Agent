using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace GeminiAgent.Tests.IntegrationTests;

public class GatewayLiveIntegrationTests
{
    private readonly HttpClient _client = new()
    {
        BaseAddress = new Uri("http://127.0.0.1:5055/v1/"),
        Timeout = TimeSpan.FromSeconds(30)
    };

    [Fact]
    public async Task HealthCheck_ReturnsHealthy_WithAccountAndModels()
    {
        var resp = await _client.GetAsync("health");
        Assert.True(resp.IsSuccessStatusCode, $"Health check failed: {resp.StatusCode}");

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>();
        Assert.NotNull(json);
        Assert.Equal("healthy", json["status"]?.GetValue<string>());

        var account = json["account"] as JsonObject;
        Assert.NotNull(account);
        Assert.True(account.ContainsKey("email"));
        Assert.True(account["hasToken"]?.GetValue<bool>());

        var models = json["allModels"] as JsonArray;
        Assert.NotNull(models);
        Assert.True(models.Count > 0);
    }

    [Fact]
    public async Task ModelsList_ReturnsOpenAiFormattedModels()
    {
        var resp = await _client.GetAsync("models");
        Assert.True(resp.IsSuccessStatusCode, $"Models list failed: {resp.StatusCode}");

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>();
        Assert.NotNull(json);
        Assert.Equal("list", json["object"]?.GetValue<string>());

        var data = json["data"] as JsonArray;
        Assert.NotNull(data);
        Assert.NotEmpty(data);

        var first = data[0] as JsonObject;
        Assert.NotNull(first);
        Assert.Equal("model", first["object"]?.GetValue<string>());
        Assert.NotNull(first["id"]?.GetValue<string>());
    }

    [Fact]
    public async Task ChatCompletions_NonStreaming_ReturnsValidResponse()
    {
        var req = new
        {
            model = "gemini-3.8-flash-high",
            messages = new[]
            {
                new { role = "user", content = "9.11 和 9.9 哪个数字更大？请仅输出更大的那个数字。" }
            },
            temperature = 0.1
        };

        var resp = await _client.PostAsJsonAsync("chat/completions", req);
        Assert.True(resp.IsSuccessStatusCode, $"Chat completions failed: {resp.StatusCode}");

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>();
        Assert.NotNull(json);

        var choices = json["choices"] as JsonArray;
        Assert.NotNull(choices);
        Assert.Single(choices);

        var content = choices[0]?["message"]?["content"]?.GetValue<string>();
        Assert.False(string.IsNullOrWhiteSpace(content));
        Assert.Contains("9.9", content);

        var usage = json["usage"] as JsonObject;
        Assert.NotNull(usage);
        Assert.True(usage["prompt_tokens"]?.GetValue<int>() > 0);
        Assert.True(usage["completion_tokens"]?.GetValue<int>() > 0);
    }

    [Fact]
    public async Task ChatCompletions_Streaming_EmitsSseEvents()
    {
        var req = new
        {
            model = "gemini-3.8-flash-high",
            messages = new[]
            {
                new { role = "user", content = "从 1 数到 3" }
            },
            stream = true
        };

        var requestMsg = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(req)
        };

        var resp = await _client.SendAsync(requestMsg, HttpCompletionOption.ResponseHeadersRead);
        Assert.True(resp.IsSuccessStatusCode);
        Assert.Equal("text/event-stream", resp.Content.Headers.ContentType?.MediaType);

        using var stream = await resp.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        var receivedData = false;
        var receivedDone = false;
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (line.StartsWith("data: [DONE]"))
            {
                receivedDone = true;
                break;
            }
            if (line.StartsWith("data: "))
            {
                receivedData = true;
            }
        }

        Assert.True(receivedData, "Expected to receive SSE data frames");
        Assert.True(receivedDone, "Expected to receive data: [DONE]");
    }

    [Fact]
    public async Task ChatCompletions_FunctionCalling_ReturnsToolCall()
    {
        var req = new
        {
            model = "gemini-3.8-flash-high",
            messages = new[]
            {
                new { role = "user", content = "帮我查询一下谷歌 (GOOG) 的当前股价" }
            },
            tools = new[]
            {
                new
                {
                    type = "function",
                    function = new
                    {
                        name = "get_stock_price",
                        description = "Get real-time stock price for a ticker",
                        parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                ticker = new { type = "string", description = "Stock ticker" }
                            },
                            required = new[] { "ticker" }
                        }
                    }
                }
            }
        };

        var resp = await _client.PostAsJsonAsync("chat/completions", req);
        Assert.True(resp.IsSuccessStatusCode);

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>();
        Assert.NotNull(json);

        var choice = json["choices"]?[0] as JsonObject;
        Assert.NotNull(choice);
        Assert.Equal("tool_calls", choice["finish_reason"]?.GetValue<string>());

        var toolCalls = choice["message"]?["tool_calls"] as JsonArray;
        Assert.NotNull(toolCalls);
        Assert.NotEmpty(toolCalls);

        var fnName = toolCalls[0]?["function"]?["name"]?.GetValue<string>();
        Assert.Equal("get_stock_price", fnName);
    }
}
