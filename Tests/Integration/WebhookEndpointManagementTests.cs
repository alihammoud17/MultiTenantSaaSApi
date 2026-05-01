using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

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
}
