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
