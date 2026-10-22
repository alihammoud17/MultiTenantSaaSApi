using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Integration;

public class TenantCorsOriginTests : IClassFixture<ApiWebApplicationFactory>
{
    private const string OriginsRoute = "/api/v1/admin/tenant/cors-origins";
    private const string ApiHost = "https://localhost";

    private static readonly string[] CorsResponseHeaders =
    [
        "Access-Control-Allow-Origin",
        "Access-Control-Allow-Credentials",
        "Access-Control-Allow-Methods",
        "Access-Control-Allow-Headers"
    ];

    private readonly ApiWebApplicationFactory _factory;

    public TenantCorsOriginTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RegisteredTenantOrigin_ShouldBeAllowed_WithoutCredentials_OnSimpleAndPreflightRequests()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await RegisterAdminAsync(client);
        var origin = UniqueOrigin();
        await AddOriginAsync(client, admin.Token, origin);

        using var simple = await client.SendAsync(CreateSimpleRequest(ApiHost, origin), TestContext.Current.CancellationToken);
        using var preflight = await client.SendAsync(CreatePreflightRequest(ApiHost, origin), TestContext.Current.CancellationToken);

        foreach (var response in new[] { simple, preflight })
        {
            response.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(origin);
            response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
            response.Headers.Vary.Should().Contain("Origin");
        }

        preflight.StatusCode.Should().Be(HttpStatusCode.NoContent);
        preflight.Headers.GetValues("Access-Control-Allow-Methods").Should().Equal("DELETE");
        preflight.Headers.GetValues("Access-Control-Allow-Headers").Single().Should().ContainEquivalentOf("authorization");
    }

    [Fact]
    public async Task UnknownOrigin_ShouldReceiveNoCorsHeaders()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await RegisterAdminAsync(client);
        await AddOriginAsync(client, admin.Token, UniqueOrigin());
        var unknown = UniqueOrigin();

        using var simple = await client.SendAsync(CreateSimpleRequest(ApiHost, unknown), TestContext.Current.CancellationToken);
        using var preflight = await client.SendAsync(CreatePreflightRequest(ApiHost, unknown), TestContext.Current.CancellationToken);

        simple.StatusCode.Should().Be(HttpStatusCode.OK);
        preflight.StatusCode.Should().Be(HttpStatusCode.NoContent);
        ShouldHaveNoCorsHeaders(simple);
        ShouldHaveNoCorsHeaders(preflight);
    }

    [Theory]
    [InlineData("HTTPS://{0}")]
    [InlineData("https://{0}/")]
    [InlineData("https://{0}:443")]
    [InlineData("http://{0}")]
    [InlineData("https://{0}.evil.test")]
    public async Task OriginThatOnlyResemblesARegisteredOrigin_ShouldReceiveNoCorsHeaders(string requestOriginFormat)
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await RegisterAdminAsync(client);
        var origin = UniqueOrigin();
        await AddOriginAsync(client, admin.Token, origin);
        var requestOrigin = string.Format(requestOriginFormat, new Uri(origin).Host);

        using var preflight = await client.SendAsync(CreatePreflightRequest(ApiHost, requestOrigin), TestContext.Current.CancellationToken);

        ShouldHaveNoCorsHeaders(preflight);
    }

    [Fact]
    public async Task RemovedOrigin_ShouldStopBeingAllowed_BecauseRemovalInvalidatesTheCache()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await RegisterAdminAsync(client);
        var origin = UniqueOrigin();
        var originId = await AddOriginAsync(client, admin.Token, origin);

        // Populate the cached origin lists while the origin is registered.
        using (var allowed = await client.SendAsync(CreatePreflightRequest(ApiHost, origin), TestContext.Current.CancellationToken))
        {
            allowed.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(origin);
        }

        using var delete = await SendAuthorizedAsync(client, admin.Token, HttpMethod.Delete, $"{OriginsRoute}/{originId}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var simple = await client.SendAsync(CreateSimpleRequest(ApiHost, origin), TestContext.Current.CancellationToken);
        using var preflight = await client.SendAsync(CreatePreflightRequest(ApiHost, origin), TestContext.Current.CancellationToken);
        ShouldHaveNoCorsHeaders(simple);
        ShouldHaveNoCorsHeaders(preflight);
    }

    [Fact]
    public async Task OriginLists_ShouldBeServedFromCache_UntilInvalidated()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await RegisterAdminAsync(client);
        var origin = UniqueOrigin();

        using (var before = await client.SendAsync(CreatePreflightRequest(ApiHost, origin), TestContext.Current.CancellationToken))
        {
            ShouldHaveNoCorsHeaders(before);
        }

        // A row written behind the service's back is invisible until something invalidates the cache.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.TenantAllowedOrigins.Add(new TenantAllowedOrigin { Id = Guid.NewGuid(), TenantId = admin.TenantId, Origin = origin, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using (var stale = await client.SendAsync(CreatePreflightRequest(ApiHost, origin), TestContext.Current.CancellationToken))
        {
            ShouldHaveNoCorsHeaders(stale);
        }

        await AddOriginAsync(client, admin.Token, UniqueOrigin());

        using var fresh = await client.SendAsync(CreatePreflightRequest(ApiHost, origin), TestContext.Current.CancellationToken);
        fresh.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(origin);
    }

    [Fact]
    public async Task PreflightOnATenantSubdomain_ShouldOnlyAllowThatTenantsOrigins()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var tenantA = await RegisterAdminAsync(client);
        var tenantB = await RegisterAdminAsync(client);
        var originA = UniqueOrigin();
        var originB = UniqueOrigin();
        await AddOriginAsync(client, tenantA.Token, originA);
        await AddOriginAsync(client, tenantB.Token, originB);
        var tenantAHost = $"https://{tenantA.Subdomain}.{ApiWebApplicationFactory.TenantBaseDomain}";

        using var ownOrigin = await client.SendAsync(CreatePreflightRequest(tenantAHost, originA), TestContext.Current.CancellationToken);
        using var otherTenantsOrigin = await client.SendAsync(CreatePreflightRequest(tenantAHost, originB), TestContext.Current.CancellationToken);
        using var sharedHost = await client.SendAsync(CreatePreflightRequest(ApiHost, originB), TestContext.Current.CancellationToken);

        ownOrigin.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(originA);
        ShouldHaveNoCorsHeaders(otherTenantsOrigin);
        sharedHost.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(originB);
    }

    [Fact]
    public async Task PreflightOnAnUnknownTenantSubdomain_ShouldNotFallBackToOtherTenantsOrigins()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await RegisterAdminAsync(client);
        var origin = UniqueOrigin();
        await AddOriginAsync(client, admin.Token, origin);

        using var preflight = await client.SendAsync(
            CreatePreflightRequest($"https://unknown-{Guid.NewGuid():N}.{ApiWebApplicationFactory.TenantBaseDomain}", origin),
            TestContext.Current.CancellationToken);

        ShouldHaveNoCorsHeaders(preflight);
    }

    [Fact]
    public async Task SuspendedTenantsOrigins_ShouldStopBeingAllowed_AndReturnOnReactivation()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await RegisterAdminAsync(client);
        var origin = UniqueOrigin();
        await AddOriginAsync(client, admin.Token, origin);

        using (var active = await client.SendAsync(CreatePreflightRequest(ApiHost, origin), TestContext.Current.CancellationToken))
        {
            active.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(origin);
        }

        (await SecurityTestHelpers.SuspendTenantAsync(client, admin.TenantId)).StatusCode.Should().Be(HttpStatusCode.OK);
        using (var suspended = await client.SendAsync(CreatePreflightRequest(ApiHost, origin), TestContext.Current.CancellationToken))
        {
            ShouldHaveNoCorsHeaders(suspended);
        }

        (await SecurityTestHelpers.ReactivateTenantAsync(client, admin.TenantId)).StatusCode.Should().Be(HttpStatusCode.OK);
        using var reactivated = await client.SendAsync(CreatePreflightRequest(ApiHost, origin), TestContext.Current.CancellationToken);
        reactivated.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(origin);
    }

    [Fact]
    public async Task TenantAdmin_CanListAddAndRemoveOrigins()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await RegisterAdminAsync(client);
        var origin = UniqueOrigin();

        using var add = await SendAuthorizedAsync(client, admin.Token, HttpMethod.Post, OriginsRoute, new { origin = origin.ToUpperInvariant() });
        add.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await add.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        created.GetProperty("origin").GetString().Should().Be(origin);
        var originId = created.GetProperty("id").GetGuid();
        add.Headers.Location!.ToString().Should().EndWith($"{OriginsRoute}/{originId}");

        using var duplicate = await SendAuthorizedAsync(client, admin.Token, HttpMethod.Post, OriginsRoute, new { origin });
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var list = await SendAuthorizedAsync(client, admin.Token, HttpMethod.Get, OriginsRoute);
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var listed = await list.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        listed.EnumerateArray().Select(o => o.GetProperty("origin").GetString()).Should().Equal(origin);

        using var remove = await SendAuthorizedAsync(client, admin.Token, HttpMethod.Delete, $"{OriginsRoute}/{originId}");
        remove.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var removeAgain = await SendAuthorizedAsync(client, admin.Token, HttpMethod.Delete, $"{OriginsRoute}/{originId}");
        removeAgain.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var auditScope = _factory.Services.CreateScope();
        var db = auditScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var actions = db.AuditLogs
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == admin.TenantId && a.EntityId == originId.ToString())
            .Select(a => a.Action)
            .ToList();
        actions.Should().BeEquivalentTo(["CORS_ORIGIN_ADDED", "CORS_ORIGIN_REMOVED"]);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("https://*.example.com")]
    [InlineData("null")]
    [InlineData("https://app.example.com/")]
    [InlineData("https://app.example.com/path")]
    [InlineData("http://app.example.com")]
    [InlineData("http://localhost:5173")]
    [InlineData("https://localhost")]
    [InlineData("")]
    public async Task InvalidOrigin_ShouldBeRejected(string origin)
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await RegisterAdminAsync(client);

        using var add = await SendAuthorizedAsync(client, admin.Token, HttpMethod.Post, OriginsRoute, new { origin });

        add.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await add.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task OriginLimit_ShouldBeEnforcedPerTenant()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await RegisterAdminAsync(client);
        for (var i = 0; i < Application.Services.TenantCorsOriginService.MaxOriginsPerTenant; i++)
        {
            await AddOriginAsync(client, admin.Token, UniqueOrigin());
        }

        using var overLimit = await SendAuthorizedAsync(client, admin.Token, HttpMethod.Post, OriginsRoute, new { origin = UniqueOrigin() });

        overLimit.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Development_ShouldAcceptHttpLocalhost()
    {
        using var factory = _factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = factory.CreateClient(new() { BaseAddress = new Uri(ApiHost) });
        var admin = await RegisterAdminAsync(client);

        using var add = await SendAuthorizedAsync(client, admin.Token, HttpMethod.Post, OriginsRoute, new { origin = "http://localhost:5173" });

        add.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task OriginManagement_ShouldRequireAuthentication()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);

        using var list = await client.GetAsync(OriginsRoute, TestContext.Current.CancellationToken);
        using var add = await client.PostAsJsonAsync(OriginsRoute, new { origin = UniqueOrigin() }, TestContext.Current.CancellationToken);
        using var remove = await client.DeleteAsync($"{OriginsRoute}/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        list.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        add.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        remove.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task OriginManagement_ShouldRequireTenantPermissions()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await RegisterAdminAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);
        var memberToken = await SecurityTestHelpers.CreateMemberAndLoginAsync(client);
        client.DefaultRequestHeaders.Authorization = null;

        using var list = await SendAuthorizedAsync(client, memberToken, HttpMethod.Get, OriginsRoute);
        using var add = await SendAuthorizedAsync(client, memberToken, HttpMethod.Post, OriginsRoute, new { origin = UniqueOrigin() });

        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        add.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OriginManagement_ShouldBeForbidden_WhenTenantOverrideDisablesTheFeature()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await RegisterAdminAsync(client);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.TenantEntitlementOverrides.Add(new TenantEntitlementOverride
            {
                Id = Guid.NewGuid(),
                TenantId = admin.TenantId,
                EntitlementKey = "feature.security.cors_origins.manage",
                Value = "false",
                Reason = "Security review disablement",
                Source = TenantEntitlementOverrideSource.ManualCorrection,
                EffectiveFromUtc = DateTime.UtcNow.AddMinutes(-1),
                CreatedUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var list = await SendAuthorizedAsync(client, admin.Token, HttpMethod.Get, OriginsRoute);
        using var add = await SendAuthorizedAsync(client, admin.Token, HttpMethod.Post, OriginsRoute, new { origin = UniqueOrigin() });

        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        add.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await add.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("error").GetString().Should().Contain("feature.security.cors_origins.manage");
    }

    [Fact]
    public async Task Tenant_ShouldNotSeeOrRemoveAnotherTenantsOrigins()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var tenantA = await RegisterAdminAsync(client);
        var tenantB = await RegisterAdminAsync(client);
        var originA = UniqueOrigin();
        var originAId = await AddOriginAsync(client, tenantA.Token, originA);

        using var listB = await SendAuthorizedAsync(client, tenantB.Token, HttpMethod.Get, OriginsRoute);
        using var removeB = await SendAuthorizedAsync(client, tenantB.Token, HttpMethod.Delete, $"{OriginsRoute}/{originAId}");
        using var crossTenantHint = await SendAuthorizedAsync(client, tenantB.Token, HttpMethod.Get, OriginsRoute, tenantHint: tenantA.TenantId);

        (await listB.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetArrayLength().Should().Be(0);
        removeB.StatusCode.Should().Be(HttpStatusCode.NotFound);
        crossTenantHint.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var listA = await SendAuthorizedAsync(client, tenantA.Token, HttpMethod.Get, OriginsRoute);
        (await listA.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .EnumerateArray().Select(o => o.GetProperty("origin").GetString()).Should().Equal(originA);
    }

    private static Task<(string Token, string RefreshToken, Guid TenantId, string Subdomain)> RegisterAdminAsync(HttpClient client)
        => SecurityTestHelpers.RegisterVerifiedTenantAsync(
            client,
            $"cors-{Guid.NewGuid():N}@example.com",
            "Passw0rd!",
            $"cors-{Guid.NewGuid():N}"[..30]);

    private static string UniqueOrigin() => $"https://app-{Guid.NewGuid():N}.customer.test";

    private static async Task<Guid> AddOriginAsync(HttpClient client, string token, string origin)
    {
        using var response = await SendAuthorizedAsync(client, token, HttpMethod.Post, OriginsRoute, new { origin });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> SendAuthorizedAsync(
        HttpClient client,
        string token,
        HttpMethod method,
        string path,
        object? body = null,
        Guid? tenantHint = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (tenantHint is not null)
            request.Headers.Add("X-Tenant-ID", tenantHint.Value.ToString());
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }

    private static HttpRequestMessage CreateSimpleRequest(string host, string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{host}/health");
        request.Headers.Add("Origin", origin);
        return request;
    }

    private static HttpRequestMessage CreatePreflightRequest(string host, string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, $"{host}{OriginsRoute}/{Guid.NewGuid()}");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "DELETE");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        return request;
    }

    private static void ShouldHaveNoCorsHeaders(HttpResponseMessage response)
    {
        foreach (var header in CorsResponseHeaders)
        {
            response.Headers.Contains(header).Should().BeFalse($"{header} must not be sent to a disallowed origin");
        }
    }
}
