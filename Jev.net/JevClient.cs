using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jev.net;

/// <summary>
/// HTTP client for the TypeSafe JEV evaluation endpoint (POST /v1/systemone).
/// Thread-safe; create one and reuse it.
/// </summary>
/// <example>
/// var jev = new JevClient("sk-...");
/// double p   = await jev.AskAsync("Help! My payouts are failing.", "Does this convey urgency?");
/// string dep = await jev.ChooseAsync(text, "Which team should handle this?", "billing", "technical", "sales");
/// double s   = await jev.ScoreAsync(text, "How frustrated is the customer?", "Calm", "Frustrated", "Very angry");
/// </example>
public sealed class JevClient : IJevClient, IDisposable
{
    private const string EvaluatePath = "v1/systemone";
    private const string SingleKey = "answer";

    /// <summary>Serializer settings used for all requests and responses.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowOutOfOrderMetadataProperties = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = null, // user-supplied state/instructions objects are serialized verbatim
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly string _apiKey;

    public JevOptions Options { get; }

    /// <summary>Create a client with just an API key and default options.</summary>
    public JevClient(string apiKey) : this(new JevOptions { ApiKey = apiKey }) { }

    /// <summary>
    /// Create a client from options. If no API key is set, the JEV_API_KEY / TYPESAFE_API_KEY
    /// environment variable is used. Optionally supply your own <see cref="HttpClient"/> (it will not be disposed).
    /// </summary>
    public JevClient(JevOptions? options = null, HttpClient? httpClient = null)
    {
        Options = (options ?? new JevOptions()).Clone();
        _apiKey = Options.ResolveApiKey();

        if (httpClient is null)
        {
            _http = new HttpClient(new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                AutomaticDecompression = DecompressionMethods.All,
            });
            _ownsHttpClient = true;
        }
        else
        {
            _http = httpClient;
        }

        _http.Timeout = Options.Timeout;
    }

    /// <summary>For DI: uses the injected <see cref="HttpClient"/> and options.</summary>
    public JevClient(HttpClient httpClient, JevOptions options) : this(options, httpClient) { }

    // ------------------------------------------------------------------
    // Core
    // ------------------------------------------------------------------

    public async Task<JevResponse> EvaluateAsync(JevRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Questions.Count == 0)
            throw new ArgumentException("At least one question is required.", nameof(request));

        var uri = new Uri(new Uri(Options.BaseUrl.TrimEnd('/') + "/"), EvaluatePath);
        var attempt = 0;

        while (true)
        {
            attempt++;
            HttpResponseMessage? response = null;
            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, uri)
                {
                    Content = JsonContent.Create(request, options: JsonOptions),
                };
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<JevResponse>(JsonOptions, cancellationToken)
                        .ConfigureAwait(false);
                    return result ?? throw new JevApiException(response.StatusCode, "", "JEV API returned an empty body.");
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var status = (int)response.StatusCode;
                var retryable = status is 429 or 529 or 502 or 503 or 504;

                if (!retryable || attempt > Options.MaxRetries)
                    throw new JevApiException(response.StatusCode, body);

                await Task.Delay(ComputeDelay(attempt, response.Headers.RetryAfter), cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (attempt <= Options.MaxRetries)
            {
                await Task.Delay(ComputeDelay(attempt, null), cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt <= Options.MaxRetries)
            {
                // HttpClient timeout (not user cancellation): retry.
                await Task.Delay(ComputeDelay(attempt, null), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                response?.Dispose();
            }
        }
    }

    public Task<JevResponse> EvaluateAsync(object state, IDictionary<string, Question> questions, string? model = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(questions);
        return EvaluateAsync(new JevRequest
        {
            State = state,
            Model = model ?? Options.Model,
            Questions = new Dictionary<string, Question>(questions),
        }, cancellationToken);
    }

    public JevEvaluation Evaluate(object state) => new(this, state);

    // ------------------------------------------------------------------
    // Quick helpers
    // ------------------------------------------------------------------

    public async Task<double> AskAsync(object state, object question, object? yes = null, object? no = null, CancellationToken cancellationToken = default)
    {
        var r = await Single(state, new NoulQuestion(question, yes, no), cancellationToken).ConfigureAwait(false);
        return r.Noul(SingleKey).Noul;
    }

    public async Task<bool> YesNoAsync(object state, object question, object? yes = null, object? no = null, CancellationToken cancellationToken = default)
        => await AskAsync(state, question, yes, no, cancellationToken).ConfigureAwait(false) >= Options.YesThreshold;

    public Task<string> ChooseAsync(object state, object question, params string[] options)
        => ChooseAsync(state, question, options, CancellationToken.None);

    public async Task<string> ChooseAsync(object state, object question, IEnumerable<string> options, CancellationToken cancellationToken = default)
    {
        var criteria = options.ToDictionary(o => o, _ => (object?)null);
        var a = await ChooseFullAsync(state, question, criteria, cancellationToken).ConfigureAwait(false);
        return a.Choice;
    }

    public async Task<string> ChooseAsync(object state, object question, IDictionary<string, string> criteria, CancellationToken cancellationToken = default)
    {
        var a = await ChooseFullAsync(state, question, criteria.ToDictionary(kv => kv.Key, kv => (object?)kv.Value), cancellationToken).ConfigureAwait(false);
        return a.Choice;
    }

    public async Task<ChoiceAnswer> ChooseFullAsync(object state, object question, IDictionary<string, object?> criteria, CancellationToken cancellationToken = default)
    {
        var r = await Single(state, new ChoiceQuestion(question, criteria), cancellationToken).ConfigureAwait(false);
        return r.Choice(SingleKey);
    }

    public Task<double> ScoreAsync(object state, object question, params object[] levels)
        => ScoreAsync(state, question, levels, CancellationToken.None);

    public async Task<double> ScoreAsync(object state, object question, IEnumerable<object> levels, CancellationToken cancellationToken = default)
    {
        var a = await ScoreFullAsync(state, question, levels, cancellationToken).ConfigureAwait(false);
        return a.Score;
    }

    public async Task<ScoreAnswer> ScoreFullAsync(object state, object question, IEnumerable<object> levels, CancellationToken cancellationToken = default)
    {
        var r = await Single(state, new ScoreQuestion(question, levels.ToArray()), cancellationToken).ConfigureAwait(false);
        return r.Score(SingleKey);
    }

    private Task<JevResponse> Single(object state, Question question, CancellationToken ct)
        => EvaluateAsync(state, new Dictionary<string, Question> { [SingleKey] = question }, null, ct);

    // ------------------------------------------------------------------
    // Retry helpers
    // ------------------------------------------------------------------

    private TimeSpan ComputeDelay(int attempt, RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter is not null)
        {
            if (retryAfter.Delta is { } delta && delta > TimeSpan.Zero)
                return Min(delta, Options.MaxRetryDelay);
            if (retryAfter.Date is { } date)
            {
                var until = date - DateTimeOffset.UtcNow;
                if (until > TimeSpan.Zero) return Min(until, Options.MaxRetryDelay);
            }
        }

        var baseMs = Options.InitialRetryDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var jitter = Random.Shared.NextDouble() * 0.5 + 0.75; // 0.75x .. 1.25x
        return Min(TimeSpan.FromMilliseconds(baseMs * jitter), Options.MaxRetryDelay);
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    public void Dispose()
    {
        if (_ownsHttpClient) _http.Dispose();
    }
}
