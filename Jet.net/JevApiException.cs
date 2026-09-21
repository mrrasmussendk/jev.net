using System.Net;

namespace Jet.net;

/// <summary>Thrown when the API returns a non-success status code (after retries, for 429/529).</summary>
public class JevApiException : Exception
{
    public HttpStatusCode StatusCode { get; }

    /// <summary>The raw response body, typically JSON describing the problem.</summary>
    public string ResponseBody { get; }

    public JevApiException(HttpStatusCode statusCode, string responseBody, string? message = null)
        : base(message ?? $"JEV API returned {(int)statusCode} {statusCode}: {Truncate(responseBody)}")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    /// <summary>401: missing or invalid API key.</summary>
    public bool IsUnauthorized => StatusCode == HttpStatusCode.Unauthorized;

    /// <summary>422: the request body failed validation. See <see cref="ResponseBody"/> for the field.</summary>
    public bool IsValidationError => StatusCode == HttpStatusCode.UnprocessableEntity;

    /// <summary>429: rate limit exceeded.</summary>
    public bool IsRateLimited => StatusCode == HttpStatusCode.TooManyRequests;

    /// <summary>529: TypeSafe is temporarily overloaded.</summary>
    public bool IsOverloaded => (int)StatusCode == 529;

    private static string Truncate(string s) => s.Length <= 500 ? s : s[..500] + "...";
}
