using Domain.Entites;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Application.Services;

public sealed class OutboundWebhookDeliveryDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboundWebhookDeliveryDispatcher> _logger;

    public OutboundWebhookDeliveryDispatcher(IServiceScopeFactory scopeFactory, ILogger<OutboundWebhookDeliveryDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchBatchAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbound webhook dispatch loop failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    private async Task DispatchBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();

        var now = DateTime.UtcNow;
        var pending = await (from delivery in dbContext.OutboundWebhookDeliveries
                             join evt in dbContext.OutboundWebhookEvents on delivery.EventId equals evt.Id
                             join endpoint in dbContext.TenantWebhookEndpoints on delivery.EndpointId equals endpoint.Id
                             where endpoint.IsActive
                                   && (delivery.Status == OutboundWebhookDeliveryStatus.Pending ||
                                       delivery.Status == OutboundWebhookDeliveryStatus.RetryScheduled)
                                   && delivery.NextAttemptAtUtc <= now
                             orderby delivery.NextAttemptAtUtc
                             select new { delivery, evt, endpoint })
            .Take(20)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return;
        }

        var client = clientFactory.CreateClient("outbound-webhooks");

        foreach (var item in pending)
        {
            var timestamp = DateTimeOffset.UtcNow.ToString("O");
            var signature = OutboundWebhookSigner.ComputeSignature(item.endpoint.SigningSecret, timestamp, item.delivery.Id.ToString("D"), item.evt.PayloadJson);

            using var request = new HttpRequestMessage(HttpMethod.Post, item.endpoint.CallbackUrl)
            {
                Content = new StringContent(item.evt.PayloadJson, System.Text.Encoding.UTF8, "application/json")
            };

            request.Headers.Add("X-Tenant-Webhook-Contract-Version", item.evt.ContractVersion);
            request.Headers.Add("X-Tenant-Webhook-Timestamp", timestamp);
            request.Headers.Add("X-Tenant-Webhook-Delivery", item.delivery.Id.ToString("D"));
            request.Headers.Add("X-Tenant-Webhook-Idempotency-Key", item.delivery.Id.ToString("N"));
            request.Headers.Add("X-Tenant-Webhook-Signature", signature);

            item.delivery.AttemptCount += 1;
            item.delivery.LastAttemptAtUtc = now;
            item.delivery.UpdatedAtUtc = now;

            try
            {
                var response = await client.SendAsync(request, cancellationToken);
                item.delivery.LastResponseStatusCode = (int)response.StatusCode;

                if ((int)response.StatusCode >= 200 && (int)response.StatusCode <= 299)
                {
                    item.delivery.Status = OutboundWebhookDeliveryStatus.Succeeded;
                    item.delivery.DeliveredAtUtc = now;
                    item.delivery.LastError = null;
                    continue;
                }

                ScheduleRetry(item.delivery, $"HTTP {(int)response.StatusCode}");
            }
            catch (Exception ex)
            {
                ScheduleRetry(item.delivery, ex.Message);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void ScheduleRetry(OutboundWebhookDelivery delivery, string error)
    {
        const int maxAttempts = 6;
        if (delivery.AttemptCount >= maxAttempts)
        {
            delivery.Status = OutboundWebhookDeliveryStatus.Exhausted;
            delivery.LastError = error;
            return;
        }

        var delaySeconds = Math.Min(300, (int)Math.Pow(2, delivery.AttemptCount) * 5);
        delivery.NextAttemptAtUtc = DateTime.UtcNow.AddSeconds(delaySeconds);
        delivery.Status = OutboundWebhookDeliveryStatus.RetryScheduled;
        delivery.LastError = error;
    }
}
