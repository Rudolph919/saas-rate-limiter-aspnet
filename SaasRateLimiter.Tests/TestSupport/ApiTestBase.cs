using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SaasRateLimiter.Configuration;

namespace SaasRateLimiter.Tests.TestSupport;

/// <summary>
/// Each call to <see cref="CreateClient"/> spins up a fresh WebApplicationFactory, which gives
/// every test its own DI container and therefore its own empty RateLimitCounter singleton —
/// the .NET equivalent of Laravel's per-test app boot plus explicit counter reset.
/// </summary>
public abstract class ApiTestBase : IDisposable
{
    private readonly List<WebApplicationFactory<Program>> _factories = [];

    protected HttpClient CreateClient(Action<RateLimitOptions>? configureOptions = null)
    {
        var factory = new WebApplicationFactory<Program>();

        if (configureOptions is not null)
        {
            factory = factory.WithWebHostBuilder(builder =>
                builder.ConfigureServices(services => services.Configure(configureOptions)));
        }

        _factories.Add(factory);

        return factory.CreateClient();
    }

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
