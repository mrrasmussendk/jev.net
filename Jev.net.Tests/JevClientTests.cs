using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Jev.net.Tests;

[TestClass]
public class JevClientTests
{
    private static JsonElement Body(FakeHandler h, int index = -1)
        => JsonDocument.Parse(index < 0 ? h.Last.Body : h.Requests[index].Body).RootElement;

    // ------------------------------------------------------------------
    // Request shape
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task SendsPostToEvaluationEndpointWithBearerAuthAndJson()
    {
        var h = new FakeHandler().Enqueue(200, FakeHandler.NoulBody(0.5));
        using var client = TestClients.Create(h, o => o.ApiKey = "sk-123");

        await client.AskAsync("state", "q");

        Assert.AreEqual(1, h.Calls);
        Assert.AreEqual("https://api.typesafe.ai/v1/systemone", h.Last.Url);
        Assert.AreEqual("Bearer sk-123", h.Last.Authorization);
        Assert.AreEqual("application/json", h.Last.ContentType);
    }

    [TestMethod]
    public async Task BaseUrlCanBeOverriddenWithOrWithoutTrailingSlash()
    {
        var h = new FakeHandler { Fallback = FakeHandler.AutoAnswer };
        using var a = TestClients.Create(h, o => o.BaseUrl = "http://localhost:1234");
        using var b = TestClients.Create(h, o => o.BaseUrl = "http://localhost:1234/");

        await a.AskAsync("s", "q");
        Assert.AreEqual("http://localhost:1234/v1/systemone", h.Last.Url);
        await b.AskAsync("s", "q");
        Assert.AreEqual("http://localhost:1234/v1/systemone", h.Last.Url);
    }

    [TestMethod]
    public async Task UsesConfiguredModelByDefaultAndAllowsPerRequestOverride()
    {
        var h = new FakeHandler { Fallback = FakeHandler.AutoAnswer };
        using var client = TestClients.Create(h, o => o.Model = "jev-1.13.0");

        await client.AskAsync("s", "q");
        Assert.AreEqual("jev-1.13.0", Body(h).GetProperty("model").GetString());

        await client.EvaluateAsync("s", new Dictionary<string, Question> { ["answer"] = new NoulQuestion("q") }, model: "jev-2");
        Assert.AreEqual("jev-2", Body(h).GetProperty("model").GetString());

        await client.Evaluate("s").Noul("answer", "q").Model("jev-3").SendAsync();
        Assert.AreEqual("jev-3", Body(h).GetProperty("model").GetString());
    }

    [TestMethod]
    public async Task StringStateIsSentAsString_StructuredStateAsObject()
    {
        var h = new FakeHandler { Fallback = FakeHandler.AutoAnswer };
        using var client = TestClients.Create(h);

        await client.AskAsync("plain text", "q");
        Assert.AreEqual("plain text", Body(h).GetProperty("state").GetString());

        await client.AskAsync(new { messages = new[] { new { role = "user", content = "hi" } } }, "q");
        Assert.AreEqual("hi", Body(h).GetProperty("state").GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [TestMethod]
    public async Task FullRequestObjectIsSentAsIs()
    {
        var h = new FakeHandler().Enqueue(200, FakeHandler.NoulBody(0.2, "k"));
        using var client = TestClients.Create(h);

        var response = await client.EvaluateAsync(new JevRequest
        {
            State = "s",
            Model = "jev-x",
            Questions = { ["k"] = new NoulQuestion("q") },
        });

        Assert.AreEqual("jev-x", Body(h).GetProperty("model").GetString());
        Assert.AreEqual(0.2, response.Noul("k").Noul, 1e-9);
    }

    [TestMethod]
    public async Task EvaluateAsync_RejectsEmptyQuestionsAndNulls()
    {
        using var client = TestClients.Create(new FakeHandler());

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => client.EvaluateAsync(new JevRequest { State = "s" }));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => client.EvaluateAsync(null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => client.EvaluateAsync(null!, new Dictionary<string, Question>()));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => client.EvaluateAsync("s", null!));
    }

    // ------------------------------------------------------------------
    // Errors and retries
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task Unauthorized_ThrowsWithoutRetry()
    {
        var h = new FakeHandler().Enqueue(401, """{"error":"bad key"}""");
        using var client = TestClients.Create(h);

        var ex = await Assert.ThrowsExactlyAsync<JevApiException>(() => client.AskAsync("s", "q"));

        Assert.AreEqual(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.IsTrue(ex.IsUnauthorized);
        StringAssert.Contains(ex.ResponseBody, "bad key");
        StringAssert.Contains(ex.Message, "401");
        Assert.AreEqual(1, h.Calls);
    }

    [TestMethod]
    public async Task ValidationError_ThrowsWithoutRetryAndExposesBody()
    {
        var h = new FakeHandler().Enqueue(422, """{"detail":[{"loc":["questions","x","criteria"],"msg":"field required"}]}""");
        using var client = TestClients.Create(h);

        var ex = await Assert.ThrowsExactlyAsync<JevApiException>(() => client.AskAsync("s", "q"));

        Assert.IsTrue(ex.IsValidationError);
        StringAssert.Contains(ex.ResponseBody, "field required");
        Assert.AreEqual(1, h.Calls);
    }

    [TestMethod]
    public async Task RateLimitedAndOverloaded_AreRetriedUntilSuccess()
    {
        var h = new FakeHandler().Enqueue(429).Enqueue(529).Enqueue(503).Enqueue(200, FakeHandler.NoulBody(0.7));
        using var client = TestClients.Create(h);

        var p = await client.AskAsync("s", "q");

        Assert.AreEqual(0.7, p, 1e-9);
        Assert.AreEqual(4, h.Calls);
    }

    [TestMethod]
    public async Task RetriesAreExhaustedAfterMaxRetries()
    {
        var h = new FakeHandler().Enqueue(429).Enqueue(429).Enqueue(429).Enqueue(429);
        using var client = TestClients.Create(h, o => o.MaxRetries = 2);

        var ex = await Assert.ThrowsExactlyAsync<JevApiException>(() => client.AskAsync("s", "q"));

        Assert.IsTrue(ex.IsRateLimited);
        Assert.AreEqual(3, h.Calls, "1 initial attempt + 2 retries");
    }

    [TestMethod]
    public async Task Overloaded529_IsReportedOnException()
    {
        var h = new FakeHandler().Enqueue(529);
        using var client = TestClients.Create(h, o => o.MaxRetries = 0);

        var ex = await Assert.ThrowsExactlyAsync<JevApiException>(() => client.AskAsync("s", "q"));
        Assert.IsTrue(ex.IsOverloaded);
        Assert.AreEqual(529, (int)ex.StatusCode);
    }

    [TestMethod]
    public async Task ZeroMaxRetries_DisablesRetrying()
    {
        var h = new FakeHandler().Enqueue(429).Enqueue(200, FakeHandler.NoulBody(0.7));
        using var client = TestClients.Create(h, o => o.MaxRetries = 0);

        await Assert.ThrowsExactlyAsync<JevApiException>(() => client.AskAsync("s", "q"));
        Assert.AreEqual(1, h.Calls);
    }

    [TestMethod]
    public async Task RetryAfterHeader_IsHonoured()
    {
        var h = new FakeHandler()
            .Enqueue(429, configure: r => r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMilliseconds(300)))
            .Enqueue(200, FakeHandler.NoulBody(0.7));
        using var client = TestClients.Create(h, o => { o.InitialRetryDelay = TimeSpan.FromMilliseconds(1); o.MaxRetryDelay = TimeSpan.FromSeconds(5); });

        await client.AskAsync("s", "q");

        var gap = h.Requests[1].At - h.Requests[0].At;
        Assert.IsTrue(gap >= TimeSpan.FromMilliseconds(250), $"expected >= 250ms between attempts, got {gap.TotalMilliseconds}ms");
    }

    [TestMethod]
    public async Task RetryAfterHeader_IsCappedByMaxRetryDelay()
    {
        var h = new FakeHandler()
            .Enqueue(429, configure: r => r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30)))
            .Enqueue(200, FakeHandler.NoulBody(0.7));
        using var client = TestClients.Create(h, o => o.MaxRetryDelay = TimeSpan.FromMilliseconds(20));

        await client.AskAsync("s", "q");

        var gap = h.Requests[1].At - h.Requests[0].At;
        Assert.IsTrue(gap < TimeSpan.FromSeconds(5), $"delay should be capped, got {gap.TotalMilliseconds}ms");
    }

    [TestMethod]
    public async Task ExponentialBackoff_GrowsBetweenAttempts()
    {
        var h = new FakeHandler().Enqueue(429).Enqueue(429).Enqueue(429).Enqueue(200, FakeHandler.NoulBody(0.7));
        using var client = TestClients.Create(h, o => { o.InitialRetryDelay = TimeSpan.FromMilliseconds(40); o.MaxRetryDelay = TimeSpan.FromSeconds(5); });

        await client.AskAsync("s", "q");

        // Delays: ~40ms, ~80ms, ~160ms (each with 0.75..1.25 jitter). Check that total is at least the minimum sum.
        var total = h.Requests[3].At - h.Requests[0].At;
        Assert.IsTrue(total >= TimeSpan.FromMilliseconds((40 + 80 + 160) * 0.75 * 0.9), $"total backoff too small: {total.TotalMilliseconds}ms");
    }

    [TestMethod]
    public async Task TransientNetworkFailure_IsRetried()
    {
        var h = new FakeHandler().Throw(new HttpRequestException("connection reset")).Enqueue(200, FakeHandler.NoulBody(0.7));
        using var client = TestClients.Create(h);

        Assert.AreEqual(0.7, await client.AskAsync("s", "q"), 1e-9);
        Assert.AreEqual(2, h.Calls);
    }

    [TestMethod]
    public async Task PersistentNetworkFailure_PropagatesAfterRetries()
    {
        var h = new FakeHandler()
            .Throw(new HttpRequestException("down"))
            .Throw(new HttpRequestException("down"))
            .Throw(new HttpRequestException("down"));
        using var client = TestClients.Create(h, o => o.MaxRetries = 2);

        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => client.AskAsync("s", "q"));
        Assert.AreEqual(3, h.Calls);
    }

    [TestMethod]
    public async Task HttpTimeout_IsRetried()
    {
        var h = new FakeHandler().Delay(TimeSpan.FromSeconds(10)).Enqueue(200, FakeHandler.NoulBody(0.7));
        using var client = TestClients.Create(h, o => o.Timeout = TimeSpan.FromMilliseconds(100));

        Assert.AreEqual(0.7, await client.AskAsync("s", "q"), 1e-9);
        Assert.AreEqual(2, h.Calls);
    }

    [TestMethod]
    public async Task UserCancellation_IsNotRetried()
    {
        var h = new FakeHandler().Delay(TimeSpan.FromSeconds(10)).Enqueue(200, FakeHandler.NoulBody(0.7));
        using var client = TestClients.Create(h);
        using var cts = new CancellationTokenSource(50);

        await Assert.ThrowsAsync<OperationCanceledException>(() => client.AskAsync("s", "q", cancellationToken: cts.Token));
        Assert.AreEqual(1, h.Calls);
    }

    [TestMethod]
    public async Task AlreadyCancelledToken_ThrowsImmediately()
    {
        var h = new FakeHandler { Fallback = FakeHandler.AutoAnswer };
        using var client = TestClients.Create(h);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => client.AskAsync("s", "q", cancellationToken: cts.Token));
    }

    [TestMethod]
    public async Task NullJsonBody_ThrowsApiException()
    {
        var h = new FakeHandler().Enqueue(200, "null");
        using var client = TestClients.Create(h);

        var ex = await Assert.ThrowsExactlyAsync<JevApiException>(() => client.AskAsync("s", "q"));
        StringAssert.Contains(ex.Message, "empty");
    }

    [TestMethod]
    public async Task MalformedJsonBody_ThrowsJsonException()
    {
        var h = new FakeHandler().Enqueue(200, "{not json");
        using var client = TestClients.Create(h);

        await Assert.ThrowsAsync<JsonException>(() => client.AskAsync("s", "q"));
    }

    [TestMethod]
    public void ErrorMessage_TruncatesLongBodies()
    {
        var ex = new JevApiException(HttpStatusCode.UnprocessableEntity, new string('x', 2000));
        Assert.IsTrue(ex.Message.Length < 700);
        StringAssert.EndsWith(ex.Message, "...");
        Assert.AreEqual(2000, ex.ResponseBody.Length, "the full body stays available");
    }

    // ------------------------------------------------------------------
    // HttpClient ownership
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task ExternalHttpClient_IsNotDisposedAndGetsTimeoutApplied()
    {
        var h = new FakeHandler { Fallback = FakeHandler.AutoAnswer };
        var http = new HttpClient(h);
        var client = new JevClient(new JevOptions { ApiKey = "k", Timeout = TimeSpan.FromSeconds(7) }, http);

        Assert.AreEqual(TimeSpan.FromSeconds(7), http.Timeout);
        client.Dispose();

        using var response = await http.PostAsync("http://localhost/", new StringContent("{}"));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public void DiConstructor_UsesInjectedHttpClientAndOptions()
    {
        var http = new HttpClient(new FakeHandler());
        var client = new JevClient(http, new JevOptions { ApiKey = "k", Model = "m" });
        Assert.AreEqual("m", client.Options.Model);
    }

    [TestMethod]
    public void ApiKeyOnlyConstructor_Works()
    {
        using var client = new JevClient("just-a-key");
        Assert.AreEqual("just-a-key", client.Options.ApiKey);
        Assert.AreEqual("jev-latest", client.Options.Model);
    }

    // ------------------------------------------------------------------
    // Quick helpers
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task AskAsync_SendsSingleNoulQuestionUnderAnswerKey()
    {
        var h = new FakeHandler { Fallback = FakeHandler.AutoAnswer };
        using var client = TestClients.Create(h);

        var p = await client.AskAsync("Help!", "Is this urgent?", yes: "time-sensitive", no: "relaxed");

        Assert.AreEqual(0.95, p, 1e-9);
        var q = Body(h).GetProperty("questions");
        Assert.AreEqual(1, q.EnumerateObject().Count());
        var answer = q.GetProperty("answer");
        Assert.AreEqual("noul", answer.GetProperty("type").GetString());
        Assert.AreEqual("Is this urgent?", answer.GetProperty("instructions").GetString());
        Assert.AreEqual("time-sensitive", answer.GetProperty("criteria").GetProperty("true").GetString());
        Assert.AreEqual("relaxed", answer.GetProperty("criteria").GetProperty("false").GetString());
    }

    [TestMethod]
    public async Task YesNoAsync_UsesConfigurableThreshold()
    {
        var h = new FakeHandler { Fallback = FakeHandler.AutoAnswer }; // noul = 0.95
        using var lenient = TestClients.Create(h);
        using var strict = TestClients.Create(h, o => o.YesThreshold = 0.99);

        Assert.IsTrue(await lenient.YesNoAsync("s", "q"));
        Assert.IsFalse(await strict.YesNoAsync("s", "q"));
    }

    [TestMethod]
    public async Task ChooseAsync_ParamsOptions()
    {
        var h = new FakeHandler { Fallback = FakeHandler.AutoAnswer };
        using var client = TestClients.Create(h);

        var choice = await client.ChooseAsync("s", "Which team?", "billing", "technical", "sales");

        Assert.AreEqual("billing", choice);
        var criteria = Body(h).GetProperty("questions").GetProperty("answer").GetProperty("criteria");
        Assert.AreEqual(3, criteria.EnumerateObject().Count());
        Assert.AreEqual(JsonValueKind.Null, criteria.GetProperty("technical").ValueKind);
    }

    [TestMethod]
    public async Task ChooseAsync_EnumerableWithCancellation()
    {
        var h = new FakeHandler { Fallback = FakeHandler.AutoAnswer };
        using var client = TestClients.Create(h);

        var choice = await client.ChooseAsync("s", "Which?", new List<string> { "billing", "technical" }, CancellationToken.None);
        Assert.AreEqual("billing", choice);
    }

    [TestMethod]
    public async Task ChooseAsync_DescribedOptions()
    {
        var h = new FakeHandler { Fallback = FakeHandler.AutoAnswer };
        using var client = TestClients.Create(h);

        var choice = await client.ChooseAsync("s", "Which?", new Dictionary<string, string> { ["billing"] = "money", ["technical"] = "bugs" });

        Assert.AreEqual("billing", choice);
        var criteria = Body(h).GetProperty("questions").GetProperty("answer").GetProperty("criteria");
        Assert.AreEqual("bugs", criteria.GetProperty("technical").GetString());
    }

    [TestMethod]
    public async Task ChooseFullAsync_ReturnsDistribution()
    {
        var h = new FakeHandler { Fallback = FakeHandler.AutoAnswer };
        using var client = TestClients.Create(h);

        var a = await client.ChooseFullAsync("s", "Which?", new Dictionary<string, object?> { ["billing"] = null, ["technical"] = new { x = 1 } });

        Assert.AreEqual("billing", a.Choice);
        Assert.AreEqual(0.88, a.Probabilities["billing"], 1e-9);
        Assert.AreEqual(0.81, a.Confidence, 1e-9);
        Assert.AreEqual(1, Body(h).GetProperty("questions").GetProperty("answer").GetProperty("criteria").GetProperty("technical").GetProperty("x").GetInt32());
    }

    [TestMethod]
    public async Task ScoreAsync_ParamsLevels()
    {
        var h = new FakeHandler { Fallback = FakeHandler.AutoAnswer };
        using var client = TestClients.Create(h);

        var s = await client.ScoreAsync("s", "How frustrated?", "Calm", "Frustrated", "Very angry");

        Assert.AreEqual(1.05, s, 1e-9);
        var levels = Body(h).GetProperty("questions").GetProperty("answer").GetProperty("criteria");
        Assert.AreEqual(3, levels.GetArrayLength());
        Assert.AreEqual("Very angry", levels[2].GetString());
    }

    [TestMethod]
    public async Task ScoreAsync_EnumerableWithCancellation()
    {
        var h = new FakeHandler { Fallback = FakeHandler.AutoAnswer };
        using var client = TestClients.Create(h);

        var s = await client.ScoreAsync("s", "q", new List<object> { "lo", "hi" }, CancellationToken.None);
        Assert.AreEqual(1.05, s, 1e-9);
    }

    [TestMethod]
    public async Task ScoreFullAsync_ReturnsLegendAndProbabilities()
    {
        var h = new FakeHandler { Fallback = FakeHandler.AutoAnswer };
        using var client = TestClients.Create(h);

        var a = await client.ScoreFullAsync("s", "q", ["Calm", "Frustrated", "Very angry"]);

        Assert.AreEqual(1.05, a.Score, 1e-9);
        Assert.AreEqual("Frustrated", a.MostLikelyLabel);
        Assert.AreEqual(0.92, a.Confidence, 1e-9);
    }

    [TestMethod]
    public async Task MultiQuestion_ReturnsAnswersUnderSameKeys()
    {
        var h = new FakeHandler().Enqueue(200, """
        {
          "model": "jev-1.13.0",
          "answers": {
            "is_urgent": { "type": "noul", "noul": 0.95 },
            "department": { "type": "choice", "choice": "billing", "probabilities": { "billing": 0.88, "technical": 0.12 }, "confidence": 0.81 },
            "frustration": { "type": "score", "score": 1.05, "legend": { "0": "Calm", "1": "Frustrated" }, "probabilities": { "0": 0.0, "1": 1.0 }, "confidence": 0.92 }
          },
          "usage": { "input_tokens": 296, "output_tokens": 20 }
        }
        """);
        using var client = TestClients.Create(h);

        var r = await client.Evaluate("Help! My payouts have been failing for 3 days.")
            .Noul("is_urgent", "Does this convey urgency?")
            .Choice("department", "Which team?", "billing", "technical")
            .Score("frustration", "How frustrated?", "Calm", "Frustrated")
            .SendAsync();

        Assert.AreEqual(0.95, r.Noul("is_urgent").Noul, 1e-9);
        Assert.AreEqual("billing", r.Choice("department").Choice);
        Assert.AreEqual(1.05, r.Score("frustration").Score, 1e-9);
        Assert.AreEqual(296, r.Usage.InputTokens);

        var q = Body(h).GetProperty("questions");
        CollectionAssert.AreEquivalent(new[] { "is_urgent", "department", "frustration" }, q.EnumerateObject().Select(p => p.Name).ToArray());
    }
}
