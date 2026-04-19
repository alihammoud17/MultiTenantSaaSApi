using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Entites;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Integration;

public class TenantIsolationSecurityTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public TenantIsolationSecurityTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task TenantScopedAdminEndpoints_ShouldRejectTokenHeaderTenantMismatch()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var tenantA = await SecurityTestHelpers.RegisterTenantAsync(client, $"iso-admin-a-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var tenantB = await SecurityTestHelpers.RegisterTenantAsync(client, $"iso-admin-b-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenantA.Token);
        client.DefaultRequestHeaders.Add("X-Tenant-ID", tenantB.TenantId.ToString());

        var response = await client.GetAsync("/api/v1/admin/tenant/users");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("TenantMismatch");
    }

    [Fact]
    public async Task TenantAAdminCannotMutateTenantBUsers()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var tenantA = await SecurityTestHelpers.RegisterTenantAsync(client, $"iso-admin-a-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var tenantB = await SecurityTestHelpers.RegisterTenantAsync(client, $"iso-admin-b-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenantB.Token);
        var addUser = await client.PostAsJsonAsync("/api/v1/admin/tenant/users", new
        {
            email = $"tenantb-user-{Guid.NewGuid():N}@example.com",
            password = "Passw0rd!",
            role = "MEMBER"
        });
        var targetUserId = (await addUser.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenantA.Token);

        var updateRole = await client.PutAsJsonAsync($"/api/v1/admin/tenant/users/{targetUserId}/role", new { role = "ADMIN" });
        var delete = await client.DeleteAsync($"/api/v1/admin/tenant/users/{targetUserId}");

        updateRole.StatusCode.Should().Be(HttpStatusCode.NotFound);
        delete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TenantAMemberCannotTargetTenantBUserIds()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var tenantA = await SecurityTestHelpers.RegisterTenantAsync(client, $"iso-member-a-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var tenantB = await SecurityTestHelpers.RegisterTenantAsync(client, $"iso-member-b-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenantA.Token);
        var memberToken = await SecurityTestHelpers.CreateMemberAndLoginAsync(client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenantB.Token);
        var addUser = await client.PostAsJsonAsync("/api/v1/admin/tenant/users", new
        {
            email = $"tenantb-member-{Guid.NewGuid():N}@example.com",
            password = "Passw0rd!",
            role = "MEMBER"
        });
        var tenantBUserId = (await addUser.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberToken);
        var response = await client.DeleteAsync($"/api/v1/admin/tenant/users/{tenantBUserId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AnalyticsAndAuditEndpoints_ShouldRejectTokenHeaderTenantMismatch()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var tenantA = await SecurityTestHelpers.RegisterTenantAsync(client, $"iso-analytics-a-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var tenantB = await SecurityTestHelpers.RegisterTenantAsync(client, $"iso-analytics-b-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenantA.Token);
        await client.PostAsJsonAsync("/api/v1/plans/upgrade", new { planId = "plan-pro" });

        client.DefaultRequestHeaders.Remove("X-Tenant-ID");
        client.DefaultRequestHeaders.Add("X-Tenant-ID", tenantB.TenantId.ToString());

        var auditResponse = await client.GetAsync("/api/v1/tenant/audit-logs?action=TENANT_PLAN_CHANGED");
        var usageResponse = await client.GetAsync("/api/v1/tenant/analytics/usage?days=30");

        auditResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        usageResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task BillingReadEndpoints_ShouldRejectTokenHeaderTenantMismatch()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var tenantA = await SecurityTestHelpers.RegisterTenantAsync(client, $"iso-billing-a-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var tenantB = await SecurityTestHelpers.RegisterTenantAsync(client, $"iso-billing-b-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenantA.Token);
        client.DefaultRequestHeaders.Add("X-Tenant-ID", tenantB.TenantId.ToString());

        var statusResponse = await client.GetAsync("/api/v1/billing/status");
        var invoicesResponse = await client.GetAsync("/api/v1/billing/invoices");

        statusResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        invoicesResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RefreshWithCrossTenantBodyMismatch_ShouldFail()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var tenantA = await SecurityTestHelpers.RegisterTenantAsync(client, $"iso-refresh-a-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var tenantB = await SecurityTestHelpers.RegisterTenantAsync(client, $"iso-refresh-b-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        var response = await client.PostAsJsonAsync("/api/v1/auth/refresh", new
        {
            tenantId = tenantB.TenantId,
            refreshToken = tenantA.RefreshToken
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task InternalBillingCallback_ShouldRejectCrossTenantSubscriptionMapping_AndNotMutateAnyTenantState()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var tenantA = await SecurityTestHelpers.RegisterTenantAsync(client, $"iso-webhook-a-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var tenantB = await SecurityTestHelpers.RegisterTenantAsync(client, $"iso-webhook-b-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        var tenantASubscriptionId = SecurityTestHelpers.GetSubscriptionId(_factory, tenantA.TenantId);
        var tenantBSubscriptionId = SecurityTestHelpers.GetSubscriptionId(_factory, tenantB.TenantId);

        var crossTenantPayload = new
        {
            contractVersion = "2026-03-18",
            eventId = $"evt-iso-{Guid.NewGuid():N}",
            eventType = "subscription.plan_changed",
            provider = "stripe",
            providerEventId = $"stripe-iso-{Guid.NewGuid():N}",
            tenantId = tenantB.TenantId,
            subscriptionId = tenantASubscriptionId,
            targetPlanId = "plan-pro",
            occurredAtUtc = DateTime.UtcNow,
            effectiveAtUtc = (DateTime?)null,
            correlationId = $"corr-iso-{Guid.NewGuid():N}"
        };

        var response = await SecurityTestHelpers.PostSignedBillingEventAsync(client, crossTenantPayload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tenantASubscription = db.Subscriptions.Single(x => x.Id == tenantASubscriptionId);
        var tenantBSubscription = db.Subscriptions.Single(x => x.Id == tenantBSubscriptionId);
        tenantASubscription.PlanId.Should().Be("plan-free");
        tenantASubscription.Status.Should().Be(SubscriptionStatus.Active);
        tenantBSubscription.PlanId.Should().Be("plan-free");
        tenantBSubscription.Status.Should().Be(SubscriptionStatus.Active);

        db.BillingEventInboxes.Any(x => x.EventId == crossTenantPayload.eventId).Should().BeFalse();
    }
}
