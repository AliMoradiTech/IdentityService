using idnetityServiceWedApi.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.MsSql;

namespace IdentityService.Tests;

public sealed class IdentityServiceFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public IdentityServiceFactory() => ClientOptions.BaseAddress = new Uri("https://localhost");

    public async Task InitializeAsync() => await _sql.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await _sql.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:AuthDb"] = _sql.GetConnectionString(),
                ["RateLimiting:Enabled"] = "false",
                ["OpenTelemetry:OtlpEndpoint"] = "http://127.0.0.1:14317",
                ["Kafka:BootstrapServers"] = "127.0.0.1:1"
            }));
        builder.ConfigureServices(services =>
        {
            ServiceDescriptor? dispatcher = services.SingleOrDefault(descriptor =>
                descriptor.ServiceType == typeof(IHostedService) &&
                descriptor.ImplementationType == typeof(OutboxDispatcher));
            if (dispatcher is not null)
            {
                services.Remove(dispatcher);
            }
        });
    }
}
