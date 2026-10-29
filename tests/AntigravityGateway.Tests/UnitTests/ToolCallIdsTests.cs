using AntigravityGateway.Services;
using Xunit;

namespace AntigravityGateway.Tests.UnitTests;

public class ToolCallIdsTests
{
    [Fact]
    public void EncodeAndDecode_WithThoughtSignature_RoundtripsSuccessfully()
    {
        var toolName = "calculator";
        var thoughtSignature = "sig_abc123_xyz789==";

        var callId = ToolCallIds.Encode(toolName, thoughtSignature);

        Assert.StartsWith("call_", callId);

        var (decodedName, decodedSignature) = ToolCallIds.Decode(callId);
        Assert.Equal(toolName, decodedName);
        Assert.Equal(thoughtSignature, decodedSignature);
    }

    [Fact]
    public void EncodeAndDecode_WithoutThoughtSignature_RoundtripsSuccessfully()
    {
        var toolName = "get_weather";

        var callId = ToolCallIds.Encode(toolName, null);

        Assert.StartsWith("call_", callId);

        var (decodedName, decodedSignature) = ToolCallIds.Decode(callId);
        Assert.Equal(toolName, decodedName);
        Assert.Null(decodedSignature);
    }

    [Fact]
    public void Decode_InvalidOrUnencodedId_ReturnsNullGracefully()
    {
        var rawId = "call_plain_unencoded_12345";

        var (decodedName, decodedSignature) = ToolCallIds.Decode(rawId);

        Assert.Null(decodedName);
        Assert.Null(decodedSignature);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not_starting_with_call")]
    public void Decode_NullOrEmptyId_ReturnsNullGracefully(string? id)
    {
        var (decodedName, decodedSignature) = ToolCallIds.Decode(id);

        Assert.Null(decodedName);
        Assert.Null(decodedSignature);
    }
}
