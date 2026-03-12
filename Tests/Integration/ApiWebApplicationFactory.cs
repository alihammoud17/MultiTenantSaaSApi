using Domain.Interfaces;
using Domain.Outputs;
using Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tests.Integration;

public sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"tests-{Guid.NewGuid()}";

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
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=unused;Username=unused;Password=unused",
                ["Redis:ConnectionString"] = "localhost:6379"
            };

            configBuilder.AddInMemoryCollection(settings);
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<ApplicationDbContext>();
            services.RemoveAll<IRateLimitService>();

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));

            services.AddScoped<IRateLimitService, AlwaysAllowRateLimitService>();

            using var scope = services.BuildServiceProvider().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.EnsureDeleted();
            db.Database.EnsureCreated();
        });
    }

    private sealed class AlwaysAllowRateLimitService : IRateLimitService
    {
        public Task<RateLimitResult> CheckRateLimitAsync(Guid tenantId)
        {
            var nextMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddMonths(1);
            return Task.FromResult(new RateLimitResult(true, int.MaxValue, int.MaxValue, nextMonth));
        }
    }
}
