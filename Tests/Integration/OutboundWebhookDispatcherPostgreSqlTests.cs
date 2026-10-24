using System.Net;
using System.Reflection;
using Application.Services;
using Domain.DTOs;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tests.Containers;

namespace Tests.Integration;

/// <summary>
/// Runs the publisher, endpoint management and the delivery dispatcher against PostgreSQL with the real migrations,
/// where column limits and the dispatcher's batch save behave as in production.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class OutboundWebhookDispatcherPostgreSqlTests : IAsyncLifetime
{
    private readonly PostgreSqlContainerFixture _fixture;
    private readonly RecordingHandler _handler = new();
    private ServiceProvider _services = null!;

    public OutboundWebhookDispatcherPostgreSqlTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        var connectionString = await _fixture.CreateDatabaseAsync();
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));
        services.AddSingleton<IHttpClientFactory>(new HandlerClientFactory(_handler));
        _services = services.BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        if (_services is not null) await _services.DisposeAsync();
    }

    [Fact]
    public async Task Delivery_ShouldBeSignedWithTheActiveSecret_WhileARotationIsPending()
    {
        var tenantId = await SeedTenantAsync();
        var (endpointId, activeSecret) = await CreateEndpointAsync(tenantId, "https://hooks.example.test/a");
        string pendingSecret;
        await using (var scope = _services.CreateAsyncScope())
        {
            var manager = new WebhookEndpointManagementService(Db(scope), new WebhookEndpointSecretIssuer());
            pendingSecret = (await manager.RotateSigningSecretAsync(tenantId, endpointId))!.SigningSecret;
        }

        await PublishAsync(tenantId, "src-rotation");
        await DispatchOnceAsync();

        var request = _handler.Requests.Should().ContainSingle().Subject;
        var timestamp = request.Headers["X-Tenant-Webhook-Timestamp"];
        var deliveryId = request.Headers["X-Tenant-Webhook-Delivery"];
        request.Headers["X-Tenant-Webhook-Signature"].Should()
            .Be(OutboundWebhookSigner.ComputeSignature(activeSecret, timestamp, deliveryId, request.Body))
            .And.NotBe(OutboundWebhookSigner.ComputeSignature(pendingSecret, timestamp, deliveryId, request.Body));
        request.Headers["X-Tenant-Webhook-Idempotency-Key"].Should().Be(Guid.Parse(deliveryId).ToString("N"));

        var delivery = await SingleDeliveryAsync(endpointId);
        delivery.Status.Should().Be(OutboundWebhookDeliveryStatus.Succeeded);
        delivery.AttemptCount.Should().Be(1);
        delivery.LastResponseStatusCode.Should().Be(204);
    }

    [Fact]
    public async Task SecondRotation_ShouldReplaceThePendingSecret_AndLeaveTheActiveSecretUnchanged()
    {
        var tenantId = await SeedTenantAsync();
        var (endpointId, activeSecret) = await CreateEndpointAsync(tenantId, "https://hooks.example.test/rotate");

        await using var scope = _services.CreateAsyncScope();
        var manager = new WebhookEndpointManagementService(Db(scope), new WebhookEndpointSecretIssuer());
        var first = await manager.RotateSigningSecretAsync(tenantId, endpointId);
        var second = await manager.RotateSigningSecretAsync(tenantId, endpointId);

        second!.SigningSecret.Should().NotBe(first!.SigningSecret).And.NotBe(activeSecret);
        var stored = await Db(scope).TenantWebhookEndpoints.AsNoTracking().SingleAsync(x => x.Id == endpointId);
        stored.SigningSecret.Should().Be(activeSecret);
        stored.NextSigningSecret.Should().Be(second.SigningSecret);
        stored.NextSigningSecretIssuedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Rotation_ShouldNotReachAnotherTenantsEndpoint()
    {
        var owner = await SeedTenantAsync();
        var attacker = await SeedTenantAsync();
        var (endpointId, activeSecret) = await CreateEndpointAsync(owner, "https://hooks.example.test/owner");

        await using var scope = _services.CreateAsyncScope();
        var manager = new WebhookEndpointManagementService(Db(scope), new WebhookEndpointSecretIssuer());

        (await manager.RotateSigningSecretAsync(attacker, endpointId)).Should().BeNull();
        (await manager.GetEndpointAsync(attacker, endpointId)).Should().BeNull();
        (await manager.SetEndpointActiveStateAsync(attacker, endpointId, false)).Should().BeNull();
        (await manager.DeleteEndpointAsync(attacker, endpointId)).Should().BeFalse();

        var stored = await Db(scope).TenantWebhookEndpoints.AsNoTracking().SingleAsync(x => x.Id == endpointId);
        stored.SigningSecret.Should().Be(activeSecret);
        stored.NextSigningSecret.Should().BeNull();
        stored.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task TransportErrorWithLongMessage_ShouldStillRecordTheRetry()
    {
        var tenantId = await SeedTenantAsync();
        var (endpointId, _) = await CreateEndpointAsync(tenantId, "https://hooks.example.test/long-error");
        _handler.Respond = _ => throw new HttpRequestException("upstream refused: " + new string('x', 5000));

        await PublishAsync(tenantId, "src-long-error");
        await DispatchOnceAsync();

        var delivery = await SingleDeliveryAsync(endpointId);
        delivery.Status.Should().Be(OutboundWebhookDeliveryStatus.RetryScheduled);
        delivery.AttemptCount.Should().Be(1);
        delivery.LastError.Should().StartWith("upstream refused: ");
        delivery.LastError!.Length.Should().BeLessThanOrEqualTo(1000);
    }

    [Fact]
    public async Task EndpointWithMalformedUrl_ShouldNotBlockOrDuplicateOtherTenantsDeliveries()
    {
        var brokenTenant = await SeedTenantAsync();
        var healthyTenant = await SeedTenantAsync();
        // Stored directly: the URL predates request validation, or was written by another path.
        var brokenEndpointId = await SeedEndpointAsync(brokenTenant, "http://[not-a-host");
        var (healthyEndpointId, _) = await CreateEndpointAsync(healthyTenant, "https://hooks.example.test/healthy");

        await PublishAsync(brokenTenant, "src-broken");
        await PublishAsync(healthyTenant, "src-healthy");

        await DispatchOnceAsync();
        await DispatchOnceAsync();

        _handler.Requests.Should().ContainSingle(r => r.Uri.AbsolutePath == "/healthy");
        (await SingleDeliveryAsync(healthyEndpointId)).Status.Should().Be(OutboundWebhookDeliveryStatus.Succeeded);

        var broken = await SingleDeliveryAsync(brokenEndpointId);
        broken.Status.Should().Be(OutboundWebhookDeliveryStatus.RetryScheduled);
        broken.AttemptCount.Should().Be(1);
        broken.LastError.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task NonSuccessResponses_ShouldBackOff_AndExhaustAfterSixAttempts()
    {
        var tenantId = await SeedTenantAsync();
        var (endpointId, _) = await CreateEndpointAsync(tenantId, "https://hooks.example.test/failing");
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        await PublishAsync(tenantId, "src-exhaust");

        var observedDelays = new List<double>();
        for (var attempt = 1; attempt <= 6; attempt++)
        {
            await DispatchOnceAsync();
            var delivery = await SingleDeliveryAsync(endpointId);
            delivery.AttemptCount.Should().Be(attempt);
            delivery.LastResponseStatusCode.Should().Be(503);
            delivery.LastError.Should().Be("HTTP 503");

            if (attempt < 6)
            {
                delivery.Status.Should().Be(OutboundWebhookDeliveryStatus.RetryScheduled);
                observedDelays.Add((delivery.NextAttemptAtUtc - delivery.LastAttemptAtUtc!.Value).TotalSeconds);
                await MakeDueAsync(endpointId);
            }
            else
            {
                delivery.Status.Should().Be(OutboundWebhookDeliveryStatus.Exhausted);
            }
        }

        observedDelays.Should().HaveCount(5);
        observedDelays.Zip(new[] { 10d, 20, 40, 80, 160 }).Should().AllSatisfy(pair => pair.First.Should().BeApproximately(pair.Second, 2));

        await DispatchOnceAsync();
        _handler.Requests.Should().HaveCount(6, "an exhausted delivery is never sent again");
    }

    [Fact]
    public async Task InactiveEndpoint_ShouldNotBeDispatched()
    {
        var tenantId = await SeedTenantAsync();
        var (endpointId, _) = await CreateEndpointAsync(tenantId, "https://hooks.example.test/inactive");
        await PublishAsync(tenantId, "src-inactive");

        await using (var scope = _services.CreateAsyncScope())
        {
            var manager = new WebhookEndpointManagementService(Db(scope), new WebhookEndpointSecretIssuer());
            await manager.SetEndpointActiveStateAsync(tenantId, endpointId, false);
        }

        await DispatchOnceAsync();

        _handler.Requests.Should().BeEmpty();
        (await SingleDeliveryAsync(endpointId)).Status.Should().Be(OutboundWebhookDeliveryStatus.Pending);
    }

    [Fact]
    public async Task Publish_ShouldOnlyTargetSubscribedActiveEndpointsOfTheSameTenant()
    {
        var tenantId = await SeedTenantAsync();
        var otherTenant = await SeedTenantAsync();
        var (subscribed, _) = await CreateEndpointAsync(tenantId, "https://hooks.example.test/subscribed", "tenant.subscription.updated, tenant.other");
        var (wildcard, _) = await CreateEndpointAsync(tenantId, "https://hooks.example.test/wildcard", "*");
        var (unsubscribed, _) = await CreateEndpointAsync(tenantId, "https://hooks.example.test/unsubscribed", "tenant.other");
        var (otherTenantEndpoint, _) = await CreateEndpointAsync(otherTenant, "https://hooks.example.test/other-tenant");

        await PublishAsync(tenantId, "src-fanout");
        await PublishAsync(tenantId, "src-fanout");

        await using var scope = _services.CreateAsyncScope();
        var endpointIds = await Db(scope).OutboundWebhookDeliveries.Select(x => x.EndpointId).ToListAsync();
        endpointIds.Should().BeEquivalentTo([subscribed, wildcard]);
        endpointIds.Should().NotContain([unsubscribed, otherTenantEndpoint]);
        (await Db(scope).OutboundWebhookEvents.CountAsync(x => x.SourceEventKey == "src-fanout")).Should().Be(1);
    }

    private static ApplicationDbContext Db(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    private async Task<Guid> SeedTenantAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        var now = DateTime.UtcNow;
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Webhook Tenant", Subdomain = $"wh-{Guid.NewGuid():N}", Status = TenantStatus.Active, CreatedAt = now, UpdatedAt = now };
        Db(scope).Tenants.Add(tenant);
        await Db(scope).SaveChangesAsync();
        return tenant.Id;
    }

    private async Task<(Guid EndpointId, string SigningSecret)> CreateEndpointAsync(Guid tenantId, string url, string eventTypes = "*")
    {
        await using var scope = _services.CreateAsyncScope();
        var manager = new WebhookEndpointManagementService(Db(scope), new WebhookEndpointSecretIssuer());
        var created = await manager.CreateEndpointAsync(tenantId, $"endpoint-{Guid.NewGuid():N}", url, eventTypes);
        return (created.Endpoint.Id, created.SigningSecret);
    }

    private async Task<Guid> SeedEndpointAsync(Guid tenantId, string url)
    {
        await using var scope = _services.CreateAsyncScope();
        var now = DateTime.UtcNow;
        var endpoint = new TenantWebhookEndpoint
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "seeded",
            CallbackUrl = url,
            SigningSecret = "seeded-secret",
            SigningSecretIssuedAtUtc = now,
            SubscribedEventTypes = "*",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        Db(scope).TenantWebhookEndpoints.Add(endpoint);
        await Db(scope).SaveChangesAsync();
        return endpoint.Id;
    }

    private async Task PublishAsync(Guid tenantId, string sourceEventKey)
    {
        await using var scope = _services.CreateAsyncScope();
        await new OutboundWebhookPublisher(Db(scope)).PublishAsync(new OutboundWebhookPublishRequest(
            tenantId,
            "tenant.subscription.updated",
            new { status = "Active" },
            $"corr-{sourceEventKey}",
            DateTime.UtcNow,
            sourceEventKey));
    }

    private async Task DispatchOnceAsync()
    {
        var dispatcher = new OutboundWebhookDeliveryDispatcher(
            _services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OutboundWebhookDeliveryDispatcher>.Instance);
        var method = typeof(OutboundWebhookDeliveryDispatcher).GetMethod("DispatchBatchAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)method.Invoke(dispatcher, [CancellationToken.None])!;
    }

    private async Task<OutboundWebhookDelivery> SingleDeliveryAsync(Guid endpointId)
    {
        await using var scope = _services.CreateAsyncScope();
        return await Db(scope).OutboundWebhookDeliveries.AsNoTracking().SingleAsync(x => x.EndpointId == endpointId);
    }

    private async Task MakeDueAsync(Guid endpointId)
    {
        await using var scope = _services.CreateAsyncScope();
        await Db(scope).OutboundWebhookDeliveries
            .Where(x => x.EndpointId == endpointId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextAttemptAtUtc, DateTime.UtcNow.AddSeconds(-1)));
    }

    private sealed record CapturedRequest(Uri Uri, string Body, IReadOnlyDictionary<string, string> Headers);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NoContent);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(request.RequestUri!, body, request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value))));
            return Respond(request);
        }
    }

    private sealed class HandlerClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
