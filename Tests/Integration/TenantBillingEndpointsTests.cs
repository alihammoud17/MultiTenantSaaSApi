using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Entites;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Integration;

public class TenantBillingEndpointsTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public TenantBillingEndpointsTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task BillingStatus_ShouldReturnTenantScopedSubscription()
    {
        using var client = _factory.CreateClient();
        var authA = await SecurityTestHelpers.RegisterTenantAsync(client, $"billing-a-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var authB = await SecurityTestHelpers.RegisterTenantAsync(client, $"billing-b-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var subB = db.Subscriptions.Single(x => x.TenantId == authB.TenantId);
        subB.PlanId = "plan-pro";
        subB.Status = SubscriptionStatus.GracePeriod;
        subB.GracePeriodEndsAtUtc = DateTime.UtcNow.AddDays(5);
        db.SaveChanges();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authA.Token);
        var response = await client.GetAsync("/api/billing/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("planId").GetString().Should().Be("plan-free");
        body.GetProperty("status").GetString().Should().Be(SubscriptionStatus.Active.ToString());
        body.GetProperty("availableActions").EnumerateArray().Select(x => x.GetString()).Should().BeEquivalentTo(["cancel", "change_plan"]);
    }

    [Fact]
    public async Task BillingInvoices_ShouldReturnOnlyCurrentTenantInvoices()
    {
        using var client = _factory.CreateClient();
        var authA = await SecurityTestHelpers.RegisterTenantAsync(client, $"billing-invoice-a-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        var authB = await SecurityTestHelpers.RegisterTenantAsync(client, $"billing-invoice-b-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var subA = db.Subscriptions.Single(x => x.TenantId == authA.TenantId);
            var subB = db.Subscriptions.Single(x => x.TenantId == authB.TenantId);

            db.BillingEventInboxes.Add(new BillingEventInbox
            {
                Id = Guid.NewGuid(),
                EventId = $"evt-a-{Guid.NewGuid():N}",
                ContractVersion = "2026-03-18",
                EventType = "invoice.payment_failed",
                Provider = "test",
                ProviderEventId = "inv-a",
                CorrelationId = $"corr-a-{Guid.NewGuid():N}",
                TenantId = authA.TenantId,
                SubscriptionId = subA.Id,
                OccurredAtUtc = DateTime.UtcNow.AddDays(-2),
                ReceivedAtUtc = DateTime.UtcNow.AddDays(-2),
                ProcessedAtUtc = DateTime.UtcNow.AddDays(-2)
            });

            db.BillingEventInboxes.Add(new BillingEventInbox
            {
                Id = Guid.NewGuid(),
                EventId = $"evt-b-{Guid.NewGuid():N}",
                ContractVersion = "2026-03-18",
                EventType = "invoice.payment_failed",
                Provider = "test",
                ProviderEventId = "inv-b",
                CorrelationId = $"corr-b-{Guid.NewGuid():N}",
                TenantId = authB.TenantId,
                SubscriptionId = subB.Id,
                OccurredAtUtc = DateTime.UtcNow.AddDays(-1),
                ReceivedAtUtc = DateTime.UtcNow.AddDays(-1),
                ProcessedAtUtc = DateTime.UtcNow.AddDays(-1)
            });

            db.SaveChanges();
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authA.Token);
        var response = await client.GetAsync("/api/billing/invoices");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetArrayLength().Should().Be(1);
        body.EnumerateArray().Single().GetProperty("externalInvoiceId").GetString().Should().Be("inv-a");
    }

    [Fact]
    public async Task BillingSubscriptionActions_ShouldRequireBillingManagePermission()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var auth = await SecurityTestHelpers.RegisterTenantAsync(client, $"billing-action-{Guid.NewGuid():N}@example.com", "Passw0rd!");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        var memberToken = await SecurityTestHelpers.CreateMemberAndLoginAsync(client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberToken);
        var response = await client.PostAsJsonAsync("/api/billing/subscription/cancel", new { reason = "No longer needed" });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task BillingSubscriptionActions_ShouldCancelAndReactivate_ForTenantAdmin()
    {
        using var client = _factory.CreateClient();
        var auth = await SecurityTestHelpers.RegisterTenantAsync(client, $"billing-admin-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var cancel = await client.PostAsJsonAsync("/api/billing/subscription/cancel", new { reason = "User requested cancelation" });
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);

        var reactivate = await client.PostAsJsonAsync("/api/billing/subscription/reactivate", new { reason = "Customer returned" });
        reactivate.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var subscription = db.Subscriptions.Single(x => x.TenantId == auth.TenantId);
        subscription.Status.Should().Be(SubscriptionStatus.Active);
        subscription.CanceledAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task BillingSubscriptionActions_ShouldReturnCleanErrors_WhenActionIsInvalidForCurrentState()
    {
        using var client = _factory.CreateClient();
        var auth = await SecurityTestHelpers.RegisterTenantAsync(client, $"billing-state-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var firstCancel = await client.PostAsJsonAsync("/api/billing/subscription/cancel", new { reason = "Initial cancellation" });
        firstCancel.StatusCode.Should().Be(HttpStatusCode.OK);

        var secondCancel = await client.PostAsJsonAsync("/api/billing/subscription/cancel", new { reason = "Duplicate cancellation" });
        secondCancel.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var secondCancelBody = await secondCancel.Content.ReadFromJsonAsync<JsonElement>();
        secondCancelBody.GetProperty("error").GetString().Should().Be("Subscription is already canceled");
    }
}
