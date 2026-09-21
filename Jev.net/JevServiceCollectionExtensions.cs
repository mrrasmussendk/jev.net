using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Jev.net;

/// <summary>Registers <see cref="IJevClient"/> in the DI container.</summary>
/// <example>
/// builder.Services.AddJev("sk-...");
/// // or
/// builder.Services.AddJev(o => { o.ApiKey = builder.Configuration["Jev:ApiKey"]; o.MaxRetries = 3; });
/// // or (reads JEV_API_KEY / TYPESAFE_API_KEY from the environment)
/// builder.Services.AddJev();
///
/// public class MyService(IJevClient jev) { ... }
/// </example>
public static class JevServiceCollectionExtensions
{
    /// <summary>Register the JEV client using the JEV_API_KEY / TYPESAFE_API_KEY environment variable.</summary>
    public static IServiceCollection AddJev(this IServiceCollection services)
        => services.AddJev(_ => { });

    /// <summary>Register the JEV client with an API key.</summary>
    public static IServiceCollection AddJev(this IServiceCollection services, string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        return services.AddJev(o => o.ApiKey = apiKey);
    }

    /// <summary>Register the JEV client with custom options.</summary>
    public static IServiceCollection AddJev(this IServiceCollection services, Action<JevOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new JevOptions();
        configure(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<JevClient>(sp => new JevClient(sp.GetRequiredService<JevOptions>()));
        services.TryAddSingleton<IJevClient>(sp => sp.GetRequiredService<JevClient>());
        return services;
    }
}
