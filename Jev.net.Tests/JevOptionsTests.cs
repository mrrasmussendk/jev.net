namespace Jev.net.Tests;

[TestClass]
public class JevOptionsTests
{
    [TestMethod]
    public void Defaults()
    {
        var o = new JevOptions();
        Assert.IsNull(o.ApiKey);
        Assert.AreEqual("jev-latest", o.Model);
        Assert.AreEqual("https://api.typesafe.ai/", o.BaseUrl);
        Assert.AreEqual(TimeSpan.FromSeconds(60), o.Timeout);
        Assert.AreEqual(5, o.MaxRetries);
        Assert.AreEqual(TimeSpan.FromMilliseconds(500), o.InitialRetryDelay);
        Assert.AreEqual(TimeSpan.FromSeconds(30), o.MaxRetryDelay);
        Assert.AreEqual(0.5, o.YesThreshold);
    }

    [TestMethod]
    public void ResolveApiKey_PrefersExplicitKey()
    {
        using var _ = new EnvScope(("JEV_API_KEY", "env"), ("TYPESAFE_API_KEY", "env2"));
        Assert.AreEqual("explicit", new JevOptions { ApiKey = "explicit" }.ResolveApiKey());
    }

    [TestMethod]
    public void ResolveApiKey_FallsBackToJevEnvVar()
    {
        using var _ = new EnvScope(("JEV_API_KEY", "from-jev"), ("TYPESAFE_API_KEY", "from-typesafe"));
        Assert.AreEqual("from-jev", new JevOptions().ResolveApiKey());
    }

    [TestMethod]
    public void ResolveApiKey_FallsBackToTypeSafeEnvVar()
    {
        using var _ = new EnvScope(("TYPESAFE_API_KEY", "from-typesafe"));
        Assert.AreEqual("from-typesafe", new JevOptions().ResolveApiKey());
    }

    [TestMethod]
    public void ResolveApiKey_TrimsWhitespace()
    {
        using var _ = new EnvScope();
        Assert.AreEqual("k", new JevOptions { ApiKey = "  k \n" }.ResolveApiKey());
    }

    [TestMethod]
    public void ResolveApiKey_IgnoresBlankExplicitKey()
    {
        using var _ = new EnvScope(("JEV_API_KEY", "env"));
        Assert.AreEqual("env", new JevOptions { ApiKey = "   " }.ResolveApiKey());
    }

    [TestMethod]
    public void ResolveApiKey_ThrowsWithGuidanceWhenNothingConfigured()
    {
        using var _ = new EnvScope();
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => new JevOptions().ResolveApiKey());
        StringAssert.Contains(ex.Message, "JEV_API_KEY");
        StringAssert.Contains(ex.Message, "TYPESAFE_API_KEY");
        StringAssert.Contains(ex.Message, "ApiKey");
    }

    [TestMethod]
    public void ClientConstructor_ThrowsWithoutKey()
    {
        using var _ = new EnvScope();
        Assert.ThrowsExactly<InvalidOperationException>(() => new JevClient());
    }

    [TestMethod]
    public void Client_CopiesOptionsSoLaterMutationsDoNotLeak()
    {
        var o = new JevOptions { ApiKey = "k", Model = "a" };
        using var client = new JevClient(o, new HttpClient(new FakeHandler()));
        o.Model = "b";
        Assert.AreEqual("a", client.Options.Model);
    }
}
