using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Integration;

public class WebhookEndpointManagementTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public WebhookEndpointManagementTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task TenantAdmin_CanManageOwnEndpoints_EndToEndLifecycle()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await SecurityTestHelpers.RegisterTenantAsync(client, $"wh-mgmt-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);

        var createResponse = await client.PostAsJsonAsync("/api/v1/tenant/webhook-endpoints", new
        {
            name = "primary",
            callbackUrl = "https://example.com/webhooks/primary",
            subscribedEventTypes = "tenant.subscription.updated"
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var createBody = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var endpointId = createBody.GetProperty("endpoint").GetProperty("id").GetGuid();
        var createdSecret = createBody.GetProperty("secret").GetProperty("signingSecret").GetString();
        createBody.GetProperty("secret").GetProperty("hasPendingSecretRotation").GetBoolean().Should().BeFalse();
        createdSecret.Should().NotBeNullOrWhiteSpace();

        var listResponse = await client.GetAsync("/api/v1/tenant/webhook-endpoints");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listed = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        listed.GetArrayLength().Should().Be(1);

        var updateResponse = await client.PutAsJsonAsync($"/api/v1/tenant/webhook-endpoints/{endpointId}", new
        {
            name = "primary-updated",
            callbackUrl = "https://example.com/webhooks/updated",
            subscribedEventTypes = "*"
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var disableResponse = await client.PatchAsJsonAsync($"/api/v1/tenant/webhook-endpoints/{endpointId}/status", new { isActive = false });
        disableResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await disableResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isActive").GetBoolean().Should().BeFalse();

        var enableResponse = await client.PatchAsJsonAsync($"/api/v1/tenant/webhook-endpoints/{endpointId}/status", new { isActive = true });
        enableResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await enableResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isActive").GetBoolean().Should().BeTrue();

        var rotateResponse = await client.PostAsync($"/api/v1/tenant/webhook-endpoints/{endpointId}/rotate-secret", null);
        rotateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var rotateBody = await rotateResponse.Content.ReadFromJsonAsync<JsonElement>();
        rotateBody.GetProperty("secret").GetProperty("hasPendingSecretRotation").GetBoolean().Should().BeTrue();
        var rotatedSecret = rotateBody.GetProperty("secret").GetProperty("signingSecret").GetString();
        rotatedSecret.Should().NotBeNullOrWhiteSpace();
        rotatedSecret.Should().NotBe(createdSecret);

        var deleteResponse = await client.DeleteAsync($"/api/v1/tenant/webhook-endpoints/{endpointId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterDeleteList = await client.GetAsync("/api/v1/tenant/webhook-endpoints");
        var afterDeleteBody = await afterDeleteList.Content.ReadFromJsonAsync<JsonElement>();
        afterDeleteBody.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task WebhookEndpointManagement_ShouldRequireAuthentication()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);

        var create = await client.PostAsJsonAsync("/api/v1/tenant/webhook-endpoints", new
        {
            name = "unauthorized",
            callbackUrl = "https://example.com/webhooks/unauthorized",
            subscribedEventTypes = "*"
        });
        var list = await client.GetAsync("/api/v1/tenant/webhook-endpoints");

        create.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        list.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    [InlineData("ftp://example.com/webhooks")]
    [InlineData("file:///etc/passwd")]
    [InlineData("http://[not-a-host")]
    public async Task CreateAndUpdate_ShouldRejectCallbackUrlsTheDispatcherCannotPostTo(string callbackUrl)
    {
        using var client = await CreateAdminClientAsync();

        var create = await client.PostAsJsonAsync("/api/v1/tenant/webhook-endpoints", new { name = "invalid-url", callbackUrl });
        create.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var endpointId = await CreateEndpointAsync(client, "valid");
        var update = await client.PutAsJsonAsync($"/api/v1/tenant/webhook-endpoints/{endpointId}", new { name = "valid", callbackUrl });
        update.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var listed = await (await client.GetAsync("/api/v1/tenant/webhook-endpoints")).Content.ReadFromJsonAsync<JsonElement>();
        listed.EnumerateArray().Select(e => e.GetProperty("callbackUrl").GetString()).Should().Equal("https://example.com/webhooks/valid");
    }

    [Fact]
    public async Task Create_ShouldRejectValuesLongerThanTheirColumns()
    {
        using var client = await CreateAdminClientAsync();

        var longName = await client.PostAsJsonAsync("/api/v1/tenant/webhook-endpoints", new
        {
            name = new string('n', 121),
            callbackUrl = "https://example.com/webhooks"
        });
        var longUrl = await client.PostAsJsonAsync("/api/v1/tenant/webhook-endpoints", new
        {
            name = "long-url",
            callbackUrl = "https://example.com/" + new string('p', 500)
        });
        var longEventTypes = await client.PostAsJsonAsync("/api/v1/tenant/webhook-endpoints", new
        {
            name = "long-events",
            callbackUrl = "https://example.com/webhooks",
            subscribedEventTypes = new string('e', 501)
        });

        longName.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        longUrl.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        longEventTypes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_WithHttpCallbackAndNoEventTypes_ShouldSubscribeToAllEvents()
    {
        using var client = await CreateAdminClientAsync();

        var create = await client.PostAsJsonAsync("/api/v1/tenant/webhook-endpoints", new { name = "  padded  ", callbackUrl = "  http://hooks.internal.test:8080/in  " });

        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var endpoint = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("endpoint");
        endpoint.GetProperty("name").GetString().Should().Be("padded");
        endpoint.GetProperty("callbackUrl").GetString().Should().Be("http://hooks.internal.test:8080/in");
        endpoint.GetProperty("subscribedEventTypes").GetString().Should().Be("*");
    }

    [Fact]
    public async Task RotateSecret_ForUnknownEndpoint_ShouldReturnNotFound()
    {
        using var client = await CreateAdminClientAsync();

        var rotate = await client.PostAsync($"/api/v1/tenant/webhook-endpoints/{Guid.NewGuid()}/rotate-secret", null);
        var update = await client.PutAsJsonAsync($"/api/v1/tenant/webhook-endpoints/{Guid.NewGuid()}", new { name = "x", callbackUrl = "https://example.com/x" });

        rotate.StatusCode.Should().Be(HttpStatusCode.NotFound);
        update.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_WithDuplicateName_ShouldReturnConflict()
    {
        using var client = await CreateAdminClientAsync();
        await CreateEndpointAsync(client, "duplicate");

        var second = await client.PostAsJsonAsync("/api/v1/tenant/webhook-endpoints", new { name = "duplicate", callbackUrl = "https://example.com/webhooks/other" });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Update_ShouldKeepItsOwnName_ButNotTakeAnotherEndpointsName()
    {
        using var client = await CreateAdminClientAsync();
        var first = await CreateEndpointAsync(client, "first");
        await CreateEndpointAsync(client, "second");

        var keepName = await client.PutAsJsonAsync($"/api/v1/tenant/webhook-endpoints/{first}", new { name = "first", callbackUrl = "https://example.com/webhooks/moved" });
        var takeName = await client.PutAsJsonAsync($"/api/v1/tenant/webhook-endpoints/{first}", new { name = "second", callbackUrl = "https://example.com/webhooks/moved" });

        keepName.StatusCode.Should().Be(HttpStatusCode.OK);
        takeName.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task SameName_InAnotherTenant_ShouldNotConflict()
    {
        using var tenantA = await CreateAdminClientAsync();
        using var tenantB = await CreateAdminClientAsync();
        await CreateEndpointAsync(tenantA, "shared-name");

        var response = await tenantB.PostAsJsonAsync("/api/v1/tenant/webhook-endpoints", new { name = "shared-name", callbackUrl = "https://example.com/webhooks/b" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task EndpointChanges_ShouldBeAudited_WithoutSigningSecrets()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await SecurityTestHelpers.RegisterTenantAsync(client, $"wh-audit-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);

        var create = await client.PostAsJsonAsync("/api/v1/tenant/webhook-endpoints", new
        {
            name = "audited",
            callbackUrl = "https://example.com/webhooks/audited",
            subscribedEventTypes = "tenant.subscription.updated"
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var createBody = await create.Content.ReadFromJsonAsync<JsonElement>();
        var endpointId = createBody.GetProperty("endpoint").GetProperty("id").GetGuid();
        var createdSecret = createBody.GetProperty("secret").GetProperty("signingSecret").GetString()!;

        (await client.PutAsJsonAsync($"/api/v1/tenant/webhook-endpoints/{endpointId}", new
        {
            name = "audited-renamed",
            callbackUrl = "https://example.com/webhooks/renamed",
            subscribedEventTypes = "*"
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PatchAsJsonAsync($"/api/v1/tenant/webhook-endpoints/{endpointId}/status", new { isActive = false })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PatchAsJsonAsync($"/api/v1/tenant/webhook-endpoints/{endpointId}/status", new { isActive = true })).StatusCode.Should().Be(HttpStatusCode.OK);
        var rotate = await client.PostAsync($"/api/v1/tenant/webhook-endpoints/{endpointId}/rotate-secret", null);
        rotate.StatusCode.Should().Be(HttpStatusCode.OK);
        var rotatedSecret = (await rotate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("secret").GetProperty("signingSecret").GetString()!;
        (await client.DeleteAsync($"/api/v1/tenant/webhook-endpoints/{endpointId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var entries = ReadWebhookAuditEntries(admin.TenantId).Where(a => a.EntityId == endpointId.ToString()).ToList();

        entries.Select(a => a.Action).Should().BeEquivalentTo(
        [
            "WEBHOOK_ENDPOINT_CREATED",
            "WEBHOOK_ENDPOINT_UPDATED",
            "WEBHOOK_ENDPOINT_DEACTIVATED",
            "WEBHOOK_ENDPOINT_ACTIVATED",
            "WEBHOOK_ENDPOINT_SECRET_ROTATED",
            "WEBHOOK_ENDPOINT_DELETED"
        ]);
        entries.Should().OnlyContain(a => a.EntityType == nameof(TenantWebhookEndpoint));
        foreach (var entry in entries)
        {
            entry.Changes.Should().NotBeNullOrWhiteSpace();
            entry.Changes.Should().NotContain(createdSecret).And.NotContain(rotatedSecret);
            using var changes = JsonDocument.Parse(entry.Changes!);
            PropertyNames(changes.RootElement).Should().NotContain(name => name.Contains("Secret", StringComparison.Ordinal) && !name.EndsWith("IssuedAtUtc", StringComparison.Ordinal));
        }

        using var update = JsonDocument.Parse(entries.Single(a => a.Action == "WEBHOOK_ENDPOINT_UPDATED").Changes!);
        update.RootElement.GetProperty("Before").GetProperty("Name").GetString().Should().Be("audited");
        update.RootElement.GetProperty("Before").GetProperty("SubscribedEventTypes").GetString().Should().Be("tenant.subscription.updated");
        update.RootElement.GetProperty("After").GetProperty("Name").GetString().Should().Be("audited-renamed");
        update.RootElement.GetProperty("After").GetProperty("CallbackUrl").GetString().Should().Be("https://example.com/webhooks/renamed");

        using var rotated = JsonDocument.Parse(entries.Single(a => a.Action == "WEBHOOK_ENDPOINT_SECRET_ROTATED").Changes!);
        rotated.RootElement.GetProperty("NextSigningSecretIssuedAtUtc").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public async Task FailedEndpointChanges_ShouldNotBeAudited()
    {
        using var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await SecurityTestHelpers.RegisterTenantAsync(client, $"wh-audit-fail-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);
        var existing = await CreateEndpointAsync(client, "taken");
        var unknown = Guid.NewGuid();

        (await client.PutAsJsonAsync($"/api/v1/tenant/webhook-endpoints/{unknown}", new { name = "x", callbackUrl = "https://example.com/x" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PatchAsJsonAsync($"/api/v1/tenant/webhook-endpoints/{unknown}/status", new { isActive = false })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsync($"/api/v1/tenant/webhook-endpoints/{unknown}/rotate-secret", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.DeleteAsync($"/api/v1/tenant/webhook-endpoints/{unknown}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync("/api/v1/tenant/webhook-endpoints", new { name = "taken", callbackUrl = "https://example.com/webhooks/other" })).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var entries = ReadWebhookAuditEntries(admin.TenantId);

        entries.Should().ContainSingle()
            .Which.Should().Match<AuditLog>(a => a.Action == "WEBHOOK_ENDPOINT_CREATED" && a.EntityId == existing.ToString());
    }

    [Fact]
    public async Task WebhookAuditEntries_ShouldOnlyBeVisibleToTheirOwnTenant()
    {
        using var tenantA = SecurityTestHelpers.CreateHttpsClient(_factory);
        var adminA = await SecurityTestHelpers.RegisterTenantAsync(tenantA, $"wh-audit-a-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        tenantA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminA.Token);
        using var tenantB = SecurityTestHelpers.CreateHttpsClient(_factory);
        var adminB = await SecurityTestHelpers.RegisterTenantAsync(tenantB, $"wh-audit-b-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        tenantB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminB.Token);

        var endpointA = await CreateEndpointAsync(tenantA, "tenant-a");
        var endpointB = await CreateEndpointAsync(tenantB, "tenant-b");
        (await tenantB.PostAsync($"/api/v1/tenant/webhook-endpoints/{endpointB}/rotate-secret", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await tenantA.GetAsync("/api/v1/tenant/audit-logs?action=WEBHOOK_ENDPOINT_CREATED");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var logsA = await response.Content.ReadFromJsonAsync<JsonElement>();
        logsA.EnumerateArray().Select(l => l.GetProperty("entityId").GetString()).Should().Equal(endpointA.ToString());

        var rotations = await (await tenantA.GetAsync("/api/v1/tenant/audit-logs?action=WEBHOOK_ENDPOINT_SECRET_ROTATED")).Content.ReadFromJsonAsync<JsonElement>();
        rotations.GetArrayLength().Should().Be(0);
    }

    private List<AuditLog> ReadWebhookAuditEntries(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return db.AuditLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.Action.StartsWith("WEBHOOK_ENDPOINT_"))
            .ToList();
    }

    private static IEnumerable<string> PropertyNames(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) yield break;
        foreach (var property in element.EnumerateObject())
        {
            yield return property.Name;
            foreach (var nested in PropertyNames(property.Value)) yield return nested;
        }
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = SecurityTestHelpers.CreateHttpsClient(_factory);
        var admin = await SecurityTestHelpers.RegisterTenantAsync(client, $"wh-validate-{Guid.NewGuid():N}@example.com", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.Token);
        return client;
    }

    private static async Task<Guid> CreateEndpointAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/tenant/webhook-endpoints", new { name, callbackUrl = $"https://example.com/webhooks/{name}" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("endpoint").GetProperty("id").GetGuid();
    }
}
