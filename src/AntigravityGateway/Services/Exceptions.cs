using System.Net;

namespace AntigravityGateway.Services;

public sealed class ModelNotFoundException(string model) : Exception($"Model '{model}' not found.")
{
    public string Model { get; } = model;
}

public sealed class TokenRefreshException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class UpstreamException(HttpStatusCode statusCode, string responseBody)
    : Exception($"Upstream request failed with status {(int)statusCode}: {Truncate(responseBody, 500)}")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string ResponseBody { get; } = responseBody;

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "(empty)";
        s = s.Trim();
        return s.Length <= max ? s : s[..max] + "...";
    }
}
