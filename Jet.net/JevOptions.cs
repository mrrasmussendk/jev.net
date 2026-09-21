namespace Jet.net;

/// <summary>Configuration for <see cref="JevClient"/>.</summary>
public sealed class JevOptions
{
    public const string DefaultModel = "jev-latest";
    public const string DefaultBaseUrl = "https://api.typesafe.ai/";

    /// <summary>Environment variables consulted, in order, when <see cref="ApiKey"/> is not set.</summary>
    public static readonly string[] ApiKeyEnvironmentVariables = ["JEV_API_KEY", "TYPESAFE_API_KEY"];

    /// <summary>Your TypeSafe API key. Falls back to the JEV_API_KEY or TYPESAFE_API_KEY environment variable.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Model used when a request does not specify one. Defaults to "jev-latest".</summary>
    public string Model { get; set; } = DefaultModel;

    /// <summary>API base URL. Defaults to https://api.typesafe.ai/.</summary>
    public string BaseUrl { get; set; } = DefaultBaseUrl;

    /// <summary>Per-request timeout. Defaults to 60 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>How many times to retry a 429/529 (or transient network failure) before giving up. Defaults to 5.</summary>
    public int MaxRetries { get; set; } = 5;

    /// <summary>Initial backoff delay; doubles per attempt with jitter. Defaults to 500 ms.</summary>
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Upper bound for a single backoff delay. Defaults to 30 seconds.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Threshold at or above which a noul probability is treated as "yes" by the boolean helpers. Defaults to 0.5.</summary>
    public double YesThreshold { get; set; } = 0.5;

    /// <summary>Resolves the API key from <see cref="ApiKey"/> or the environment; throws if none is found.</summary>
    public string ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(ApiKey))
            return ApiKey.Trim();

        foreach (var name in ApiKeyEnvironmentVariables)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        throw new InvalidOperationException(
            $"No JEV API key configured. Set {nameof(JevOptions)}.{nameof(ApiKey)}, pass it to the client, " +
            $"or set the {string.Join(" or ", ApiKeyEnvironmentVariables)} environment variable.");
    }

    internal JevOptions Clone() => (JevOptions)MemberwiseClone();
}
