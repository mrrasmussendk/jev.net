namespace Jev.net.Tests;

/// <summary>
/// Real calls to api.typesafe.ai. These run only when JEV_API_KEY (or TYPESAFE_API_KEY) is set,
/// so the normal <c>dotnet test</c> run stays offline and free.
///
///   $env:JEV_API_KEY = "sk-..."; dotnet test --filter TestCategory=Integration
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class IntegrationTests
{
    private const string Ticket = "Help! My payouts have been failing for 3 days.";

    private static bool HasKey => JevOptions.ApiKeyEnvironmentVariables
        .Any(n => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(n)));

    private static JevClient Client()
    {
        if (!HasKey)
            Assert.Inconclusive("Set JEV_API_KEY to run integration tests against the real API.");
        return new JevClient();
    }

    [TestMethod]
    public async Task Ask_ReturnsProbabilityBetween0And1()
    {
        using var jev = Client();
        var p = await jev.AskAsync(Ticket, "Does this convey urgency?");
        Assert.IsTrue(p is >= 0 and <= 1, $"got {p}");
        Assert.IsTrue(p > 0.5, $"expected the ticket to read as urgent, got {p}");
    }

    [TestMethod]
    public async Task Choose_PicksOneOfTheOptions()
    {
        using var jev = Client();
        var a = await jev.ChooseFullAsync(Ticket, "Which team should handle this?", new Dictionary<string, object?>
        {
            ["billing"] = "Payments, invoicing, refunds",
            ["technical"] = "Bugs, outages, integrations",
            ["sales"] = "Pricing, upgrades, new accounts",
        });

        CollectionAssert.Contains(new[] { "billing", "technical", "sales" }, a.Choice);
        Assert.AreEqual(1.0, a.Probabilities.Values.Sum(), 0.02, "probabilities should sum to 1");
        Assert.IsTrue(a.Confidence is >= 0 and <= 1);
    }

    [TestMethod]
    public async Task Score_LandsWithinLevels()
    {
        using var jev = Client();
        var s = await jev.ScoreFullAsync(Ticket, "How frustrated is the customer?", ["Calm", "Frustrated", "Very angry"]);

        Assert.IsTrue(s.Score is >= 0 and <= 2, $"got {s.Score}");
        Assert.AreEqual(3, s.Legend.Count);
        Assert.AreEqual("Calm", s.Legend["0"]);
        Assert.IsNotNull(s.MostLikelyLabel);
    }

    [TestMethod]
    public async Task MultiQuestion_ReturnsEveryAnswerAndUsage()
    {
        using var jev = Client();
        var r = await jev.Evaluate(Ticket)
            .Noul("is_urgent", "Does this convey urgency?")
            .Choice("department", "Which team should handle this?", "billing", "technical", "sales")
            .Score("frustration", "How frustrated is the customer?", "Calm", "Frustrated", "Very angry")
            .SendAsync();

        Assert.AreEqual(3, r.Answers.Count);
        Assert.IsInstanceOfType<NoulAnswer>(r["is_urgent"]);
        Assert.IsInstanceOfType<ChoiceAnswer>(r["department"]);
        Assert.IsInstanceOfType<ScoreAnswer>(r["frustration"]);
        Assert.IsTrue(r.Usage.InputTokens > 0);
        StringAssert.StartsWith(r.Model, "jev-");
    }

    [TestMethod]
    public async Task StructuredStateAndInstructions_AreAccepted()
    {
        using var jev = Client();
        var chat = new[]
        {
            new { role = "user", content = "My invoice is wrong, I was charged twice." },
            new { role = "assistant", content = "Sorry about that, I can refund the duplicate charge now." },
        };

        var p = await jev.AskAsync(chat, new
        {
            policy = "Refunds for duplicate charges are always approved.",
            question = "Did the assistant act in line with `policy`?",
        });

        Assert.IsTrue(p is >= 0 and <= 1);
    }

    [TestMethod]
    public async Task InvalidKey_ThrowsUnauthorized()
    {
        if (!HasKey) Assert.Inconclusive("Set JEV_API_KEY to run integration tests against the real API.");
        using var jev = new JevClient("definitely-not-a-valid-key");

        var ex = await Assert.ThrowsExactlyAsync<JevApiException>(() => jev.AskAsync(Ticket, "Urgent?"));
        Assert.IsTrue(ex.IsUnauthorized, $"expected 401, got {(int)ex.StatusCode}: {ex.ResponseBody}");
    }
}
