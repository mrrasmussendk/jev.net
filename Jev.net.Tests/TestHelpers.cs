using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Jev.net.Tests;

/// <summary>A scriptable <see cref="HttpMessageHandler"/> that records every request.</summary>
internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _script = new();

    public sealed record Recorded(string Url, string? Authorization, string? ContentType, string Body, DateTime At);

    public List<Recorded> Requests { get; } = new();
    public int Calls => Requests.Count;
    public Recorded Last => Requests[^1];

    /// <summary>Used when the script queue is empty.</summary>
    public Func<HttpRequestMessage, string, HttpResponseMessage>? Fallback { get; set; }

    public FakeHandler Enqueue(HttpStatusCode status, string body = "{}", Action<HttpResponseMessage>? configure = null)
    {
        _script.Enqueue(_ =>
        {
            var r = Json(status, body);
            configure?.Invoke(r);
            return r;
        });
        return this;
    }

    public FakeHandler Enqueue(int status, string body = "{}", Action<HttpResponseMessage>? configure = null)
        => Enqueue((HttpStatusCode)status, body, configure);

    public FakeHandler Throw(Exception ex)
    {
        _script.Enqueue(_ => throw ex);
        return this;
    }

    /// <summary>Delay (honouring the cancellation token, so HttpClient timeouts fire) then respond.</summary>
    public FakeHandler Delay(TimeSpan delay, HttpStatusCode status = HttpStatusCode.OK, string body = "{}")
    {
        _script.Enqueue(_ => throw new DelayMarker(delay, status, body));
        return this;
    }

    private sealed class DelayMarker(TimeSpan delay, HttpStatusCode status, string body) : Exception
    {
        public TimeSpan Delay { get; } = delay;
        public HttpStatusCode Status { get; } = status;
        public string Body { get; } = body;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new Recorded(
            request.RequestUri!.ToString(),
            request.Headers.Authorization?.ToString(),
            request.Content?.Headers.ContentType?.MediaType,
            body,
            DateTime.UtcNow));

        if (_script.TryDequeue(out var next))
        {
            try
            {
                return next(request);
            }
            catch (DelayMarker d)
            {
                await Task.Delay(d.Delay, cancellationToken);
                return Json(d.Status, d.Body);
            }
        }

        if (Fallback is not null)
            return Fallback(request, body);

        throw new InvalidOperationException("FakeHandler: no scripted response left and no Fallback set.");
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    // ---- canned JEV response bodies ----

    public static string NoulBody(double noul, string key = "answer") =>
        Wrap(key, "{\"type\":\"noul\",\"noul\":" + F(noul) + "}");

    public static string ChoiceBody(string choice, IDictionary<string, double> probabilities, double confidence = 0.8, string key = "answer")
    {
        var probs = string.Join(",", probabilities.Select(kv => "\"" + kv.Key + "\":" + F(kv.Value)));
        return Wrap(key, "{\"type\":\"choice\",\"choice\":\"" + choice + "\",\"probabilities\":{" + probs + "},\"confidence\":" + F(confidence) + "}");
    }

    public static string ScoreBody(double score, IList<string> legend, IList<double> probabilities, double confidence = 0.9, string key = "answer")
    {
        var leg = string.Join(",", legend.Select((l, i) => "\"" + i + "\":\"" + l + "\""));
        var probs = string.Join(",", probabilities.Select((p, i) => "\"" + i + "\":" + F(p)));
        return Wrap(key, "{\"type\":\"score\",\"score\":" + F(score) + ",\"legend\":{" + leg + "},\"probabilities\":{" + probs + "},\"confidence\":" + F(confidence) + "}");
    }

    private static string Wrap(string key, string answerJson) =>
        "{\"model\":\"jev-1.13.0\",\"answers\":{\"" + key + "\":" + answerJson + "},\"usage\":{\"input_tokens\":10,\"output_tokens\":2}}";

    /// <summary>Answers any single-question request with a sensible canned body based on the question type.</summary>
    public static HttpResponseMessage AutoAnswer(HttpRequestMessage _, string body)
    {
        if (body.Contains("\"type\":\"choice\""))
            return Json(HttpStatusCode.OK, ChoiceBody("billing", new Dictionary<string, double> { ["billing"] = 0.88, ["technical"] = 0.12 }, 0.81));
        if (body.Contains("\"type\":\"score\""))
            return Json(HttpStatusCode.OK, ScoreBody(1.05, ["Calm", "Frustrated", "Very angry"], [0.0, 0.95, 0.05], 0.92));
        return Json(HttpStatusCode.OK, NoulBody(0.95));
    }

    private static string F(double d) => d.ToString("0.0###", CultureInfo.InvariantCulture);
}

internal static class TestClients
{
    public static JevClient Create(FakeHandler handler, Action<JevOptions>? configure = null)
    {
        var options = new JevOptions
        {
            ApiKey = "test-key",
            InitialRetryDelay = TimeSpan.FromMilliseconds(1),
            MaxRetryDelay = TimeSpan.FromMilliseconds(50),
        };
        configure?.Invoke(options);
        return new JevClient(options, new HttpClient(handler));
    }
}

/// <summary>Sets environment variables for the duration of a test and restores them afterwards.</summary>
internal sealed class EnvScope : IDisposable
{
    private readonly Dictionary<string, string?> _previous = new();

    public EnvScope(params (string Name, string? Value)[] vars)
    {
        // Always neutralise both key variables so ambient machine config cannot leak into tests.
        foreach (var name in JevOptions.ApiKeyEnvironmentVariables)
        {
            _previous[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, null);
        }
        foreach (var (name, value) in vars)
        {
            _previous.TryAdd(name, Environment.GetEnvironmentVariable(name));
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    public void Dispose()
    {
        foreach (var (name, value) in _previous)
            Environment.SetEnvironmentVariable(name, value);
    }
}
