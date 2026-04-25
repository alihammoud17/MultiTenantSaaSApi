using System.Net;
using System.Reflection;
using Application.Services;
using Domain.DTOs;
using Domain.Entites;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.UnitTests.OutboundWebhooks;

internal sealed class OutboundWebhookDeliveryHarness : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;
    private readonly QueueBackedWebhookHandler _webhookHandler;

    private OutboundWebhookDeliveryHarness(SqliteConnection connection, ServiceProvider serviceProvider, QueueBackedWebhookHandler webhookHandler)
    {
        _connection = connection;
        _serviceProvider = serviceProvider;
        _webhookHandler = webhookHandler;
    }

    public IReadOnlyList<CapturedWebhookRequest> CapturedRequests => _webhookHandler.CapturedRequests;

    public static async Task<OutboundWebhookDeliveryHarness> CreateAsync(params WebhookDispatchOutcome[] outcomes)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var webhookHandler = new QueueBackedWebhookHandler(outcomes);
        var services = new ServiceCollection();

        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton<IHttpClientFactory>(new FixedHttpClientFactory(webhookHandler));

        var serviceProvider = services.BuildServiceProvider();

        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
        }

        return new OutboundWebhookDeliveryHarness(connection, serviceProvider, webhookHandler);
    }

    public async Task<SeededTenantWebhookEndpoint> SeedTenantEndpointAsync(string callbackUrl = "https://callbacks.example.test/webhooks")
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var now = DateTime.UtcNow;
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Harness Tenant",
            Subdomain = $"tenant-{Guid.NewGuid():N}",
            Status = TenantStatus.Active
        };

        var endpoint = new TenantWebhookEndpoint
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Name = "primary",
            CallbackUrl = callbackUrl,
            SigningSecret = "test-signing-secret",
            SubscribedEventTypes = "*",
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        dbContext.Tenants.Add(tenant);
        dbContext.TenantWebhookEndpoints.Add(endpoint);
        await dbContext.SaveChangesAsync();

        return new SeededTenantWebhookEndpoint(tenant.Id, endpoint.Id);
    }

    public async Task PublishAsync(OutboundWebhookPublishRequest request)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var publisher = new OutboundWebhookPublisher(dbContext);
        await publisher.PublishAsync(request);
    }

    public async Task DispatchDueDeliveriesOnceAsync(CancellationToken cancellationToken = default)
    {
        var dispatcher = new OutboundWebhookDeliveryDispatcher(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OutboundWebhookDeliveryDispatcher>.Instance);

        var method = typeof(OutboundWebhookDeliveryDispatcher)
            .GetMethod("DispatchBatchAsync", BindingFlags.Instance | BindingFlags.NonPublic);

        method.Should().NotBeNull("dispatcher batch invocation is required for the deterministic harness");

        var task = (Task?)method!.Invoke(dispatcher, [cancellationToken]);
        task.Should().NotBeNull();
        await task!;
    }

    public async Task<OutboundWebhookDelivery> GetSingleDeliveryAsync(Guid tenantId)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await (from delivery in dbContext.OutboundWebhookDeliveries
                      join evt in dbContext.OutboundWebhookEvents on delivery.EventId equals evt.Id
                      where evt.TenantId == tenantId
                      select delivery)
            .SingleAsync();
    }

    public async Task ForceDeliveryDueAsync(Guid deliveryId, DateTime dueAtUtc)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var delivery = await dbContext.OutboundWebhookDeliveries.SingleAsync(x => x.Id == deliveryId);
        delivery.NextAttemptAtUtc = dueAtUtc;
        delivery.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    internal sealed record SeededTenantWebhookEndpoint(Guid TenantId, Guid EndpointId);

    internal sealed record WebhookDispatchOutcome(HttpStatusCode StatusCode, string? Body = null)
    {
        public static WebhookDispatchOutcome Success(HttpStatusCode statusCode = HttpStatusCode.NoContent)
            => new(statusCode);

        public static WebhookDispatchOutcome Failure(HttpStatusCode statusCode)
            => new(statusCode, "failure");
    }

    internal sealed record CapturedWebhookRequest(
        Uri RequestUri,
        string Body,
        IReadOnlyDictionary<string, string> Headers,
        DateTime CapturedAtUtc);

    private sealed class QueueBackedWebhookHandler : HttpMessageHandler
    {
        private readonly Queue<WebhookDispatchOutcome> _outcomes;
        private readonly List<CapturedWebhookRequest> _capturedRequests = [];

        public QueueBackedWebhookHandler(IEnumerable<WebhookDispatchOutcome> outcomes)
        {
            _outcomes = new Queue<WebhookDispatchOutcome>(outcomes);
        }

        public IReadOnlyList<CapturedWebhookRequest> CapturedRequests => _capturedRequests;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var headers = request.Headers.ToDictionary(x => x.Key, x => string.Join(",", x.Value));

            _capturedRequests.Add(new CapturedWebhookRequest(
                request.RequestUri ?? new Uri("https://callbacks.example.test/unknown"),
                body,
                headers,
                DateTime.UtcNow));

            var outcome = _outcomes.Count > 0
                ? _outcomes.Dequeue()
                : WebhookDispatchOutcome.Success();

            return new HttpResponseMessage(outcome.StatusCode)
            {
                Content = outcome.Body is null ? null : new StringContent(outcome.Body)
            };
        }
    }

    private sealed class FixedHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public FixedHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler, disposeHandler: false)
            {
                Timeout = TimeSpan.FromSeconds(5)
            };
        }
    }
}
