using System.Text.Json;
using GeminiAgent.Services;
using Xunit;

namespace GeminiAgent.Tests.UnitTests;

public class SchemaNormalizerTests
{
    [Fact]
    public void Normalize_RemovesUnsupportedFields_AndPreservesProperties()
    {
        var rawJson = """
        {
            "$schema": "http://json-schema.org/draft-07/schema#",
            "$comment": "Internal comment to be stripped",
            "$id": "http://example.com/schema",
            "title": "CalculatorParameters",
            "type": "object",
            "properties": {
                "expr": {
                    "type": "string",
                    "description": "Math expression",
                    "$comment": "field comment to be stripped"
                }
            },
            "required": ["expr"]
        }
        """;

        using var doc = JsonDocument.Parse(rawJson);
        var normalized = SchemaNormalizer.Normalize(doc.RootElement);

        Assert.NotNull(normalized);
        var root = normalized.Value;
        Assert.Equal("object", root.GetProperty("type").GetString());
        Assert.False(root.TryGetProperty("$schema", out _));
        Assert.False(root.TryGetProperty("$comment", out _));
        Assert.False(root.TryGetProperty("$id", out _));
        Assert.True(root.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("expr", out var exprProp));
        Assert.Equal("string", exprProp.GetProperty("type").GetString());
        Assert.Equal("Math expression", exprProp.GetProperty("description").GetString());
        Assert.False(exprProp.TryGetProperty("$comment", out _));
        Assert.True(root.TryGetProperty("required", out var req));
        Assert.Equal("expr", req[0].GetString());
    }

    [Fact]
    public void Normalize_HandlesNullOrEmptyRootGracefully()
    {
        using var doc = JsonDocument.Parse("{}");
        var normalized = SchemaNormalizer.Normalize(doc.RootElement);

        Assert.NotNull(normalized);
        Assert.Equal(JsonValueKind.Object, normalized.Value.ValueKind);
    }
}
