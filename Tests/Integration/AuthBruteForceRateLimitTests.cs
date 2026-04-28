using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tests.Integration;

public class AuthBruteForceRateLimitTests
{
    private const int PermitLimit = 10;

    [Fact]
    public async Task RepeatedLoginAttempts_ShouldReturn429_AfterPermitLimit()
    {
        using var factory = new AuthRateLimitWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        for (var attempt = 1; attempt <= PermitLimit; attempt++)
        {
            var allowedResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new
            {
                email = $"unknown-{attempt}-{Guid.NewGuid():N}@example.com",
                password = "wrong-pass"
            });

            allowedResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var rateLimitedResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = $"unknown-final-{Guid.NewGuid():N}@example.com",
            password = "wrong-pass"
        });

        rateLimitedResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task RepeatedRegistrationAttempts_ShouldReturn429_AfterPermitLimit()
    {
        using var factory = new AuthRateLimitWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        for (var attempt = 1; attempt <= PermitLimit; attempt++)
        {
            var allowedResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new
            {
                companyName = $"Rate Limit Co {attempt}",
                subdomain = $"rate-limit-{attempt}-{Guid.NewGuid():N}",
                adminEmail = $"rl-register-{attempt}-{Guid.NewGuid():N}@example.com",
                adminPassword = "Passw0rd!"
            });

            allowedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var rateLimitedResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            companyName = "Rate Limit Co Final",
            subdomain = $"rate-limit-final-{Guid.NewGuid():N}",
            adminEmail = $"rl-register-final-{Guid.NewGuid():N}@example.com",
            adminPassword = "Passw0rd!"
        });

        rateLimitedResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task RefreshEndpoint_ShouldReturn429_WhenLoginRateLimitBudgetIsExhausted()
    {
        using var factory = new AuthRateLimitWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        for (var attempt = 1; attempt <= PermitLimit; attempt++)
        {
            var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new
            {
                email = $"refresh-budget-{attempt}-{Guid.NewGuid():N}@example.com",
                password = "wrong-pass"
            });

            loginResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var refreshResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new
        {
            tenantId = Guid.NewGuid(),
            refreshToken = "non-existent-refresh-token"
        });

        refreshResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}

public sealed class AuthRateLimitWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString = $"Data Source=file:auth-rate-limit-tests-{Guid.NewGuid():N}?mode=memory&cache=shared";
    private readonly SqliteConnection _keepAliveConnection;

    public AuthRateLimitWebApplicationFactory()
    {
        _keepAliveConnection = new SqliteConnection(_connectionString);
        _keepAliveConnection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "super_secret_key_for_tests_only_1234567890",
                ["Jwt:Issuer"] = "MultiTenantSaasApi",
                ["Jwt:Audience"] = "MultiTenantSaasApi",
                ["Jwt:ExpirationMinutes"] = "60",
                ["BillingIntegration:SharedSecret"] = "billing-integration-test-secret",
                ["BillingIntegration:AllowedClockSkewMinutes"] = "5",
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=unused;Username=unused;Password=unused",
                ["Redis:ConnectionString"] = "localhost:6379"
            };

            configBuilder.AddInMemoryCollection(settings);
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ApplicationDbContext>();
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            services.RemoveAll<IConfigureOptions<DbContextOptions<ApplicationDbContext>>>();
            services.RemoveAll<DbConnection>();

            services.AddScoped<DbConnection>(_ =>
            {
                var connection = new SqliteConnection(_connectionString);
                connection.Open();
                return connection;
            });
            services.AddDbContext<ApplicationDbContext>((sp, options) =>
                options.UseSqlite(sp.GetRequiredService<DbConnection>()));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.Database.EnsureCreated();

        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _keepAliveConnection.Dispose();
        }
    }
}
