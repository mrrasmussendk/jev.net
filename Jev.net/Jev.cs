namespace Jev.net;

/// <summary>
/// Zero-setup static entry point. Set <see cref="ApiKey"/> once (or the JEV_API_KEY environment
/// variable) and ask questions from anywhere. For DI, prefer <see cref="JevServiceCollectionExtensions.AddJev(Microsoft.Extensions.DependencyInjection.IServiceCollection, string)"/>.
/// </summary>
/// <example>
/// Jev.ApiKey = "sk-...";
/// bool urgent = await Jev.YesNoAsync("Help! My payouts are failing.", "Is this urgent?");
/// </example>
public static class Jev
{
    private static readonly object Lock = new();
    private static JevOptions _options = new();
    private static HttpClient? _httpClient;
    private static JevClient? _client;

    /// <summary>Set your API key. Replaces any previously created default client.</summary>
    public static string? ApiKey
    {
        get => _options.ApiKey;
        set => Configure(o => o.ApiKey = value);
    }

    /// <summary>The options the default client is (or will be) built from.</summary>
    public static JevOptions Options => _options.Clone();

    /// <summary>
    /// Configure the default client (API key, model, retries, ...). Optionally supply the
    /// <see cref="HttpClient"/> it should use (handy for tests); it will not be disposed.
    /// </summary>
    public static void Configure(Action<JevOptions> configure, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(configure);
        lock (Lock)
        {
            var next = _options.Clone();
            configure(next);
            _options = next;
            _httpClient = httpClient;
            _client?.Dispose();
            _client = null;
        }
    }

    /// <summary>Reset to defaults: no API key (environment variables still apply), default options, no client.</summary>
    public static void Reset()
    {
        lock (Lock)
        {
            _options = new JevOptions();
            _httpClient = null;
            _client?.Dispose();
            _client = null;
        }
    }

    /// <summary>Configure the default client with an API key.</summary>
    public static void Configure(string apiKey) => Configure(o => o.ApiKey = apiKey);

    /// <summary>The shared default client, created on first use.</summary>
    public static IJevClient Client
    {
        get
        {
            if (_client is { } c) return c;
            lock (Lock)
            {
                return _client ??= new JevClient(_options, _httpClient);
            }
        }
    }

    public static JevEvaluation Evaluate(object state) => Client.Evaluate(state);

    public static Task<JevResponse> EvaluateAsync(JevRequest request, CancellationToken cancellationToken = default)
        => Client.EvaluateAsync(request, cancellationToken);

    public static Task<double> AskAsync(object state, object question, object? yes = null, object? no = null, CancellationToken cancellationToken = default)
        => Client.AskAsync(state, question, yes, no, cancellationToken);

    public static Task<bool> YesNoAsync(object state, object question, object? yes = null, object? no = null, CancellationToken cancellationToken = default)
        => Client.YesNoAsync(state, question, yes, no, cancellationToken);

    public static Task<string> ChooseAsync(object state, object question, params string[] options)
        => Client.ChooseAsync(state, question, options);

    public static Task<string> ChooseAsync(object state, object question, IDictionary<string, string> criteria, CancellationToken cancellationToken = default)
        => Client.ChooseAsync(state, question, criteria, cancellationToken);

    public static Task<ChoiceAnswer> ChooseFullAsync(object state, object question, IDictionary<string, object?> criteria, CancellationToken cancellationToken = default)
        => Client.ChooseFullAsync(state, question, criteria, cancellationToken);

    public static Task<double> ScoreAsync(object state, object question, params object[] levels)
        => Client.ScoreAsync(state, question, levels);

    public static Task<ScoreAnswer> ScoreFullAsync(object state, object question, IEnumerable<object> levels, CancellationToken cancellationToken = default)
        => Client.ScoreFullAsync(state, question, levels, cancellationToken);
}
