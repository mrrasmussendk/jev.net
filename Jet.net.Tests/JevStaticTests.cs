namespace Jet.net.Tests;

[TestClass]
public class JevStaticTests
{
    private EnvScope _env = null!;

    [TestInitialize]
    public void Init()
    {
        _env = new EnvScope();
        Jev.Reset();
    }

    [TestCleanup]
    public void Cleanup()
    {
        Jev.Reset();
        _env.Dispose();
    }

    [TestMethod]
    public void ApiKey_SetsKeyAndBuildsClient()
    {
        Jev.ApiKey = "static-key";

        Assert.AreEqual("static-key", Jev.ApiKey);
        Assert.AreEqual("static-key", Jev.Options.ApiKey);
        Assert.IsInstanceOfType<JevClient>(Jev.Client);
        Assert.AreEqual("static-key", ((JevClient)Jev.Client).Options.ApiKey);
    }

    [TestMethod]
    public void Configure_WithString()
    {
        Jev.Configure("abc");
        Assert.AreEqual("abc", Jev.ApiKey);
    }

    [TestMethod]
    public void Configure_WithAction_AppliesAllOptions()
    {
        Jev.Configure(o => { o.ApiKey = "k"; o.Model = "jev-1.13.0"; o.MaxRetries = 1; });

        var client = (JevClient)Jev.Client;
        Assert.AreEqual("jev-1.13.0", client.Options.Model);
        Assert.AreEqual(1, client.Options.MaxRetries);
    }

    [TestMethod]
    public void Configure_IsCumulative()
    {
        Jev.Configure(o => o.ApiKey = "k");
        Jev.Configure(o => o.Model = "m");
        Assert.AreEqual("k", Jev.Options.ApiKey);
        Assert.AreEqual("m", Jev.Options.Model);
    }

    [TestMethod]
    public void Client_IsCachedUntilReconfigured()
    {
        Jev.ApiKey = "a";
        var first = Jev.Client;
        Assert.AreSame(first, Jev.Client);

        Jev.ApiKey = "b";
        Assert.AreNotSame(first, Jev.Client);
    }

    [TestMethod]
    public void Client_UsesEnvironmentVariableWhenNoKeySet()
    {
        using var _ = new EnvScope(("JEV_API_KEY", "env-key"));
        Assert.AreEqual("env-key", ((JevClient)Jev.Client).Options.ResolveApiKey());
    }

    [TestMethod]
    public void Client_ThrowsWithoutAnyKey()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = Jev.Client);
    }

    [TestMethod]
    public void Options_ReturnsCopy()
    {
        Jev.ApiKey = "k";
        var copy = Jev.Options;
        copy.ApiKey = "changed";
        Assert.AreEqual("k", Jev.ApiKey);
    }

    [TestMethod]
    public void Configure_ValidatesArgument()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => Jev.Configure((Action<JevOptions>)null!));
    }

    [TestMethod]
    public async Task StaticHelpers_ForwardToDefaultClient()
    {
        var h = new FakeHandler { Fallback = FakeHandler.AutoAnswer };
        Jev.Configure(o => { o.ApiKey = "k"; o.InitialRetryDelay = TimeSpan.FromMilliseconds(1); }, new HttpClient(h));

        Assert.AreEqual(0.95, await Jev.AskAsync("s", "urgent?"), 1e-9);
        Assert.IsTrue(await Jev.YesNoAsync("s", "urgent?"));
        Assert.AreEqual("billing", await Jev.ChooseAsync("s", "team?", "billing", "technical"));
        Assert.AreEqual("billing", await Jev.ChooseAsync("s", "team?", new Dictionary<string, string> { ["billing"] = "b" }));
        Assert.AreEqual("billing", (await Jev.ChooseFullAsync("s", "team?", new Dictionary<string, object?> { ["billing"] = null })).Choice);
        Assert.AreEqual(1.05, await Jev.ScoreAsync("s", "how?", "lo", "mid", "hi"), 1e-9);
        Assert.AreEqual("Frustrated", (await Jev.ScoreFullAsync("s", "how?", ["lo", "mid", "hi"])).MostLikelyLabel);

        var multi = await Jev.Evaluate("s").Noul("answer", "q").SendAsync();
        Assert.AreEqual(0.95, multi.Noul("answer").Noul, 1e-9);

        var raw = await Jev.EvaluateAsync(new JevRequest { State = "s", Questions = { ["answer"] = new NoulQuestion("q") } });
        Assert.AreEqual("jev-1.13.0", raw.Model);

        Assert.AreEqual("Bearer k", h.Last.Authorization);
        Assert.AreEqual(9, h.Calls);
    }
}
