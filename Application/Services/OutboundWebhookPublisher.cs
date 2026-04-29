using System.Text.Json;
using Domain.DTOs;
using Domain.Entities;
using Domain.Interfaces;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

public sealed class OutboundWebhookPublisher : IOutboundWebhookPublisher
{
    private const string ContractVersion = "2026-04-13";
    private readonly ApplicationDbContext _dbContext;

    public OutboundWebhookPublisher(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task PublishAsync(OutboundWebhookPublishRequest request, CancellationToken cancellationToken = default)
    {
        if (request.TenantId == Guid.Empty || string.IsNullOrWhiteSpace(request.EventType) || string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            throw new InvalidOperationException("Outbound webhook event is missing required fields.");
        }

        if (!string.IsNullOrWhiteSpace(request.SourceEventKey))
        {
            var existing = await _dbContext.OutboundWebhookEvents
                .AsNoTracking()
                .AnyAsync(x => x.SourceEventKey == request.SourceEventKey, cancellationToken);
            if (existing)
            {
                return;
            }
        }

        var endpoints = await _dbContext.TenantWebhookEndpoints
            .Where(x => x.TenantId == request.TenantId && x.IsActive)
            .ToListAsync(cancellationToken);

        if (endpoints.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var eventId = $"owevt_{Guid.NewGuid():N}";
        var envelope = new OutboundWebhookEnvelope(
            ContractVersion,
            eventId,
            request.TenantId,
            request.EventType,
            request.CorrelationId,
            request.OccurredAtUtc ?? now,
            request.Data);

        var outboundEvent = new OutboundWebhookEvent
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            ContractVersion = ContractVersion,
            TenantId = request.TenantId,
            EventType = request.EventType,
            CorrelationId = request.CorrelationId,
            PayloadJson = JsonSerializer.Serialize(envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            SourceEventKey = request.SourceEventKey,
            OccurredAtUtc = envelope.OccurredAtUtc,
            CreatedAtUtc = now
        };

        _dbContext.OutboundWebhookEvents.Add(outboundEvent);

        foreach (var endpoint in endpoints)
        {
            if (!IsSubscribed(endpoint.SubscribedEventTypes, request.EventType))
            {
                continue;
            }

            _dbContext.OutboundWebhookDeliveries.Add(new OutboundWebhookDelivery
            {
                Id = Guid.NewGuid(),
                EventId = outboundEvent.Id,
                EndpointId = endpoint.Id,
                AttemptCount = 0,
                NextAttemptAtUtc = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                Status = OutboundWebhookDeliveryStatus.Pending
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool IsSubscribed(string subscribedEventTypes, string eventType)
    {
        if (string.IsNullOrWhiteSpace(subscribedEventTypes) || subscribedEventTypes == "*")
        {
            return true;
        }

        var normalized = subscribedEventTypes.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return normalized.Any(x => string.Equals(x, eventType, StringComparison.OrdinalIgnoreCase));
    }
}
