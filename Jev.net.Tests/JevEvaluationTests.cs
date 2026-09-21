namespace Jev.net.Tests;

[TestClass]
public class JevEvaluationTests
{
    private static JevClient Client() => new("k");

    [TestMethod]
    public void ToRequest_ContainsStateModelAndAllQuestionTypes()
    {
        var request = Client().Evaluate("state")
            .Noul("n", "yes?", yes: "y", no: "n")
            .Choice("c1", "which?", "a", "b")
            .Choice("c2", "which?", new Dictionary<string, string> { ["a"] = "A" })
            .Choice("c3", "which?", new Dictionary<string, object?> { ["a"] = null })
            .Score("s", "rate", "lo", "hi")
            .Add("custom", new ScoreQuestion("x", 1, 2, 3))
            .ToRequest();

        Assert.AreEqual("state", request.State);
        Assert.AreEqual("jev-latest", request.Model);
        Assert.AreEqual(6, request.Questions.Count);
        Assert.IsInstanceOfType<NoulQuestion>(request.Questions["n"]);
        Assert.IsNotNull(((NoulQuestion)request.Questions["n"]).Criteria);
        Assert.IsInstanceOfType<ChoiceQuestion>(request.Questions["c1"]);
        Assert.IsInstanceOfType<ChoiceQuestion>(request.Questions["c2"]);
        Assert.IsInstanceOfType<ChoiceQuestion>(request.Questions["c3"]);
        Assert.IsInstanceOfType<ScoreQuestion>(request.Questions["s"]);
        Assert.AreEqual(3, ((ScoreQuestion)request.Questions["custom"]).Criteria.Count);
    }

    [TestMethod]
    public void ToRequest_ModelPrecedence_BuilderOverridesDefault()
    {
        var builder = Client().Evaluate("s").Noul("n", "q");

        Assert.AreEqual("jev-latest", builder.ToRequest().Model);
        Assert.AreEqual("from-default", builder.ToRequest("from-default").Model);
        Assert.AreEqual("explicit", builder.Model("explicit").ToRequest("from-default").Model);
    }

    [TestMethod]
    public void ToRequest_ReturnsIndependentCopies()
    {
        var builder = Client().Evaluate("s").Noul("n", "q");
        var first = builder.ToRequest();
        builder.Noul("m", "q2");

        Assert.AreEqual(1, first.Questions.Count);
        Assert.AreEqual(2, builder.ToRequest().Questions.Count);
    }

    [TestMethod]
    public void Add_SameKeyReplacesPreviousQuestion()
    {
        var request = Client().Evaluate("s").Noul("k", "first").Score("k", "second", "a", "b").ToRequest();
        Assert.AreEqual(1, request.Questions.Count);
        Assert.IsInstanceOfType<ScoreQuestion>(request.Questions["k"]);
    }

    [TestMethod]
    public void Add_ValidatesArguments()
    {
        var builder = Client().Evaluate("s");
        Assert.ThrowsExactly<ArgumentException>(() => builder.Add("", new NoulQuestion("q")));
        Assert.ThrowsExactly<ArgumentException>(() => builder.Add("  ", new NoulQuestion("q")));
        Assert.ThrowsExactly<ArgumentNullException>(() => builder.Add("k", null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => Client().Evaluate(null!));
    }

    [TestMethod]
    public async Task SendAsync_SendsThroughClientWithModelOverride()
    {
        var h = new FakeHandler().Enqueue(200, FakeHandler.NoulBody(0.3, "n"));
        using var client = TestClients.Create(h);

        var response = await client.Evaluate("s").Noul("n", "q").Model("jev-9").SendAsync();

        Assert.AreEqual(0.3, response.Noul("n").Noul, 1e-9);
        StringAssert.Contains(h.Last.Body, "\"model\":\"jev-9\"");
        StringAssert.Contains(h.Last.Body, "\"n\":{\"type\":\"noul\"");
    }

    [TestMethod]
    public async Task SendAsync_WithoutQuestionsThrows()
    {
        using var client = TestClients.Create(new FakeHandler());
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => client.Evaluate("s").SendAsync());
    }
}
