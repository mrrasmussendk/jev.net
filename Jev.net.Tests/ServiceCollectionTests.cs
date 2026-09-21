using Microsoft.Extensions.DependencyInjection;

namespace Jev.net.Tests;

[TestClass]
public class ServiceCollectionTests
{
    [TestMethod]
    public void AddJev_WithApiKey_ResolvesClient()
    {
        using var provider = new ServiceCollection().AddJev("di-key").BuildServiceProvider();

        var client = provider.GetRequiredService<IJevClient>();
        Assert.IsInstanceOfType<JevClient>(client);
        Assert.AreEqual("di-key", ((JevClient)client).Options.ApiKey);
        Assert.AreEqual("di-key", provider.GetRequiredService<JevOptions>().ApiKey);
    }

    [TestMethod]
    public void AddJev_WithConfigure_AppliesOptions()
    {
        using var provider = new ServiceCollection()
            .AddJev(o => { o.ApiKey = "k"; o.Model = "jev-1.13.0"; o.MaxRetries = 2; o.YesThreshold = 0.7; })
            .BuildServiceProvider();

        var client = (JevClient)provider.GetRequiredService<IJevClient>();
        Assert.AreEqual("jev-1.13.0", client.Options.Model);
        Assert.AreEqual(2, client.Options.MaxRetries);
        Assert.AreEqual(0.7, client.Options.YesThreshold);
    }

    [TestMethod]
    public void AddJev_WithoutArguments_UsesEnvironmentVariable()
    {
        using var _ = new EnvScope(("TYPESAFE_API_KEY", "env-key"));
        using var provider = new ServiceCollection().AddJev().BuildServiceProvider();

        Assert.AreEqual("env-key", ((JevClient)provider.GetRequiredService<IJevClient>()).Options.ResolveApiKey());
    }

    [TestMethod]
    public void AddJev_WithoutAnyKey_FailsOnResolveNotOnRegistration()
    {
        using var _ = new EnvScope();
        using var provider = new ServiceCollection().AddJev().BuildServiceProvider();

        Assert.ThrowsExactly<InvalidOperationException>(() => provider.GetRequiredService<IJevClient>());
    }

    [TestMethod]
    public void AddJev_RegistersSingletonSharedBetweenInterfaceAndClass()
    {
        using var provider = new ServiceCollection().AddJev("k").BuildServiceProvider();

        var a = provider.GetRequiredService<IJevClient>();
        var b = provider.GetRequiredService<IJevClient>();
        var c = provider.GetRequiredService<JevClient>();
        Assert.AreSame(a, b);
        Assert.AreSame(a, c);

        using var scope = provider.CreateScope();
        Assert.AreSame(a, scope.ServiceProvider.GetRequiredService<IJevClient>());
    }

    [TestMethod]
    public void AddJev_DoesNotOverrideExistingRegistration()
    {
        var custom = new JevClient("custom");
        using var provider = new ServiceCollection()
            .AddSingleton<IJevClient>(custom)
            .AddJev("other")
            .BuildServiceProvider();

        Assert.AreSame(custom, provider.GetRequiredService<IJevClient>());
    }

    [TestMethod]
    public void AddJev_ReturnsSameCollectionForChaining()
    {
        var services = new ServiceCollection();
        Assert.AreSame(services, services.AddJev("k"));
    }

    [TestMethod]
    public void AddJev_ValidatesArguments()
    {
        var services = new ServiceCollection();
        Assert.ThrowsExactly<ArgumentException>(() => services.AddJev(""));
        Assert.ThrowsExactly<ArgumentException>(() => services.AddJev("   "));
        Assert.ThrowsExactly<ArgumentNullException>(() => services.AddJev((string)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => services.AddJev((Action<JevOptions>)null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => ((IServiceCollection)null!).AddJev("k"));
    }

    [TestMethod]
    public async Task InjectedClient_CanBeUsedByAService()
    {
        var h = new FakeHandler { Fallback = FakeHandler.AutoAnswer };
        using var provider = new ServiceCollection()
            .AddSingleton<IJevClient>(TestClients.Create(h))
            .AddTransient<TriageService>()
            .BuildServiceProvider();

        var team = await provider.GetRequiredService<TriageService>().RouteAsync("Help! Payouts failing.");
        Assert.AreEqual("billing", team);
    }

    private sealed class TriageService(IJevClient jev)
    {
        public Task<string> RouteAsync(string ticket) => jev.ChooseAsync(ticket, "Which team?", "billing", "technical", "sales");
    }
}
