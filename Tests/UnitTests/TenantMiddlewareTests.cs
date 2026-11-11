using System.Security.Claims;
using System.Text.Json;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Presentation.Middleware;

namespace Tests.UnitTests;

/// <summary>
/// Drives <see cref="TenantMiddleware"/> directly, covering resolution branches the HTTP suites do not reach
/// (unknown JWT tenants on auth routes, malformed hints and claims, skip lists, and the resolution cache).
/// </summary>
public sealed class TenantMiddlewareTests : IAsyncLifetime
{
    private const string BaseDomain = "example.test";

    private SqliteConnection _connection = null!;
    private ApplicationDbContext _db = null!;

    public async ValueTask InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task AuthRoute_WithJwtForUnknownTenant_ShouldReturn404_WithoutCallingNext()
    {
        var run = await InvokeAsync("/api/v1/auth/sessions", user: Jwt(Guid.NewGuid()));

        run.NextCalled.Should().BeFalse();
        run.StatusCode.Should().Be(404);
        run.Error.Should().Be("TenantNotFound");
    }

    [Fact]
    public async Task AuthRoute_WithJwtForSuspendedTenant_ShouldReturn403()
    {
        var tenant = await SeedTenantAsync(TenantStatus.Suspended);

        var run = await InvokeAsync("/api/v1/auth/mfa/step-up", user: Jwt(tenant.Id));

        run.NextCalled.Should().BeFalse();
        run.StatusCode.Should().Be(403);
        run.Error.Should().Be("TenantSuspended");
    }

    [Fact]
    public async Task AuthRoute_Anonymous_ShouldPassThrough_WithoutResolvingATenant()
    {
        var run = await InvokeAsync("/api/v2/auth/login", headers: new() { ["X-Tenant-ID"] = Guid.NewGuid().ToString() });

        run.NextCalled.Should().BeTrue();
        run.TenantContext.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task AuthRoute_WithJwtForActiveTenant_ShouldPassThrough()
    {
        var tenant = await SeedTenantAsync(TenantStatus.Active);

        var run = await InvokeAsync("/api/v1/auth/sessions", user: Jwt(tenant.Id));

        run.NextCalled.Should().BeTrue();
    }

    [Theory]
    [InlineData("/api/internal/billing/subscription-events", "POST")]
    [InlineData("/api/internal/tenants/00000000-0000-0000-0000-000000000001/suspend", "POST")]
    [InlineData("/health", "GET")]
    [InlineData("/metrics", "GET")]
    [InlineData("/swagger/index.html", "GET")]
    [InlineData("/api/v1/plans", "GET")]
    [InlineData("/API/V1/PLANS", "GET")]
    public async Task PublicRoutes_ShouldSkipTenantResolution(string path, string method)
    {
        var run = await InvokeAsync(path, method);

        run.NextCalled.Should().BeTrue();
        run.TenantContext.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task PlansMutation_ShouldRequireATenant()
    {
        var run = await InvokeAsync("/api/v1/plans/upgrade", "POST");

        run.NextCalled.Should().BeFalse();
        run.StatusCode.Should().Be(400);
        run.Error.Should().Be("TenantNotFound");
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    public async Task MalformedTenantHeader_WithoutOtherHints_ShouldReturn400(string header)
    {
        var run = await InvokeAsync("/api/v1/billing/subscription", headers: new() { ["X-Tenant-ID"] = header });

        run.NextCalled.Should().BeFalse();
        run.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task HeaderForUnknownTenant_ShouldReturn404()
    {
        var run = await InvokeAsync("/api/v1/billing/subscription", headers: new() { ["X-Tenant-ID"] = Guid.NewGuid().ToString() });

        run.NextCalled.Should().BeFalse();
        run.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task HeaderForSuspendedTenant_ShouldReturn403()
    {
        var tenant = await SeedTenantAsync(TenantStatus.Suspended);

        var run = await InvokeAsync("/api/v1/billing/subscription", headers: new() { ["X-Tenant-ID"] = tenant.Id.ToString() });

        run.NextCalled.Should().BeFalse();
        run.StatusCode.Should().Be(403);
        run.Error.Should().Be("TenantSuspended");
    }

    [Fact]
    public async Task HeaderForPendingVerificationTenant_ShouldReturn403()
    {
        var tenant = await SeedTenantAsync(TenantStatus.PendingVerification);

        var run = await InvokeAsync("/api/v1/billing/subscription", headers: new() { ["X-Tenant-ID"] = tenant.Id.ToString() });

        run.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task UnknownSubdomain_ShouldFallBackToTheHeaderHint()
    {
        var tenant = await SeedTenantAsync(TenantStatus.Active);

        var run = await InvokeAsync("/api/v1/billing/subscription", host: $"missing.{BaseDomain}", headers: new() { ["X-Tenant-ID"] = tenant.Id.ToString() });

        run.NextCalled.Should().BeTrue();
        run.TenantContext.TenantId.Should().Be(tenant.Id);
        run.ResolutionCache.ResolvedTenant!.Id.Should().Be(tenant.Id);
    }

    [Fact]
    public async Task Subdomain_ShouldWinOverAConflictingHeader_AndBeCachedForTheRequest()
    {
        var subdomainTenant = await SeedTenantAsync(TenantStatus.Active);
        var headerTenant = await SeedTenantAsync(TenantStatus.Active);

        var run = await InvokeAsync(
            "/api/v1/billing/subscription",
            host: $"{subdomainTenant.Subdomain}.{BaseDomain}",
            headers: new() { ["X-Tenant-ID"] = headerTenant.Id.ToString() });

        run.NextCalled.Should().BeTrue();
        run.TenantContext.TenantId.Should().Be(subdomainTenant.Id);
        run.ResolutionCache.ResolvedTenant!.Id.Should().Be(subdomainTenant.Id);
    }

    [Fact]
    public async Task SuspendedSubdomainTenant_ShouldReturn403()
    {
        var tenant = await SeedTenantAsync(TenantStatus.Suspended);

        var run = await InvokeAsync("/api/v1/billing/subscription", host: $"{tenant.Subdomain}.{BaseDomain}");

        run.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task JwtTenant_ConflictingWithSubdomainTenant_ShouldReturn403Mismatch()
    {
        var subdomainTenant = await SeedTenantAsync(TenantStatus.Active);
        var jwtTenant = await SeedTenantAsync(TenantStatus.Active);

        var run = await InvokeAsync("/api/v1/billing/subscription", host: $"{subdomainTenant.Subdomain}.{BaseDomain}", user: Jwt(jwtTenant.Id));

        run.NextCalled.Should().BeFalse();
        run.StatusCode.Should().Be(403);
        run.Error.Should().Be("TenantMismatch");
        run.TenantContext.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task JwtTenant_MatchingTheHeader_ShouldResolve()
    {
        var tenant = await SeedTenantAsync(TenantStatus.Active);

        var run = await InvokeAsync("/api/v1/billing/subscription", user: Jwt(tenant.Id), headers: new() { ["X-Tenant-ID"] = tenant.Id.ToString() });

        run.NextCalled.Should().BeTrue();
        run.TenantContext.TenantId.Should().Be(tenant.Id);
    }

    [Fact]
    public async Task JwtWithMalformedTenantClaim_ShouldFallBackToTheHeaderHint()
    {
        var tenant = await SeedTenantAsync(TenantStatus.Active);
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_id", "not-a-guid")], "Bearer"));

        var run = await InvokeAsync("/api/v1/billing/subscription", user: user, headers: new() { ["X-Tenant-ID"] = tenant.Id.ToString() });

        run.NextCalled.Should().BeTrue();
        run.TenantContext.TenantId.Should().Be(tenant.Id);
    }

    [Fact]
    public async Task UnauthenticatedIdentityWithTenantClaim_ShouldNotBeTrusted()
    {
        var claimedTenant = await SeedTenantAsync(TenantStatus.Active);
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_id", claimedTenant.Id.ToString())]));

        var run = await InvokeAsync("/api/v1/billing/subscription", user: anonymous);

        run.NextCalled.Should().BeFalse();
        run.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task JwtForDeletedTenant_ShouldReturn404()
    {
        var run = await InvokeAsync("/api/v1/billing/subscription", user: Jwt(Guid.NewGuid()));

        run.NextCalled.Should().BeFalse();
        run.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task WithoutBaseDomain_SubdomainHostsShouldNotResolve()
    {
        var tenant = await SeedTenantAsync(TenantStatus.Active);

        var run = await InvokeAsync("/api/v1/billing/subscription", host: $"{tenant.Subdomain}.{BaseDomain}", baseDomain: null);

        run.NextCalled.Should().BeFalse();
        run.StatusCode.Should().Be(400);
    }

    [Fact]
    public void TenantContext_ShouldRejectSwitchingToAnotherTenantWithinARequest()
    {
        var context = new TenantContext();
        var tenantId = Guid.NewGuid();

        context.SetTenantId(tenantId);
        context.SetTenantId(tenantId);

        var switchTenant = () => context.SetTenantId(Guid.NewGuid());
        switchTenant.Should().Throw<InvalidOperationException>();
        context.TenantId.Should().Be(tenantId);
    }

    private static ClaimsPrincipal Jwt(Guid tenantId) =>
        new(new ClaimsIdentity([new Claim("tenant_id", tenantId.ToString())], "Bearer"));

    private async Task<Tenant> SeedTenantAsync(TenantStatus status)
    {
        var now = DateTime.UtcNow;
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Middleware Tenant", Subdomain = $"t{Guid.NewGuid():N}"[..20], Status = status, CreatedAt = now, UpdatedAt = now };
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return tenant;
    }

    private async Task<MiddlewareRun> InvokeAsync(
        string path,
        string method = "GET",
        string host = "api.localhost",
        ClaimsPrincipal? user = null,
        Dictionary<string, string>? headers = null,
        string? baseDomain = BaseDomain)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = method;
        context.Request.Host = new HostString(host);
        context.Response.Body = new MemoryStream();
        if (user is not null)
            context.User = user;
        foreach (var (name, value) in headers ?? [])
            context.Request.Headers[name] = value;

        var nextCalled = false;
        var middleware = new TenantMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            NullLogger<TenantMiddleware>.Instance,
            new StaticOptionsMonitor(new TenantResolutionOptions { BaseDomain = baseDomain }));
        var tenantContext = new TenantContext();
        var cache = new RequestTenantResolutionCache();

        await middleware.InvokeAsync(context, tenantContext, cache, _db);

        context.Response.Body.Position = 0;
        string? error = null;
        if (context.Response.Body.Length > 0)
            error = JsonDocument.Parse(context.Response.Body).RootElement.GetProperty("error").GetString();

        return new MiddlewareRun(nextCalled, context.Response.StatusCode, error, tenantContext, cache);
    }

    private sealed record MiddlewareRun(bool NextCalled, int StatusCode, string? Error, TenantContext TenantContext, RequestTenantResolutionCache ResolutionCache);

    private sealed class StaticOptionsMonitor(TenantResolutionOptions value) : IOptionsMonitor<TenantResolutionOptions>
    {
        public TenantResolutionOptions CurrentValue => value;
        public TenantResolutionOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<TenantResolutionOptions, string?> listener) => null;
    }
}
