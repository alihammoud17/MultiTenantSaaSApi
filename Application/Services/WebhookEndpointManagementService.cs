using Domain.Entities;
using Domain.Interfaces;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

public sealed class WebhookEndpointManagementService : IWebhookEndpointManagementService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IWebhookEndpointSecretIssuer _secretIssuer;

    public WebhookEndpointManagementService(ApplicationDbContext dbContext, IWebhookEndpointSecretIssuer secretIssuer)
    {
        _dbContext = dbContext;
        _secretIssuer = secretIssuer;
    }

    public async Task<WebhookEndpointManagementResult> CreateEndpointAsync(Guid tenantId, string name, string callbackUrl, string subscribedEventTypes, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var secret = _secretIssuer.IssueSecret();
        var endpoint = new TenantWebhookEndpoint
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            CallbackUrl = callbackUrl,
            SigningSecret = secret,
            SigningSecretIssuedAtUtc = now,
            NextSigningSecret = null,
            NextSigningSecretIssuedAtUtc = null,
            SubscribedEventTypes = string.IsNullOrWhiteSpace(subscribedEventTypes) ? "*" : subscribedEventTypes,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _dbContext.TenantWebhookEndpoints.Add(endpoint);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new WebhookEndpointManagementResult(MapView(endpoint), secret, false);
    }

    public async Task<IReadOnlyCollection<WebhookEndpointManagementView>> ListEndpointsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.TenantWebhookEndpoints
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.Name)
            .Select(x => MapView(x))
            .ToListAsync(cancellationToken);
    }

    public async Task<WebhookEndpointManagementView?> GetEndpointAsync(Guid tenantId, Guid endpointId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.TenantWebhookEndpoints
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Id == endpointId)
            .Select(x => MapView(x))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<WebhookEndpointManagementView?> UpdateEndpointAsync(Guid tenantId, Guid endpointId, string name, string callbackUrl, string subscribedEventTypes, CancellationToken cancellationToken = default)
    {
        var endpoint = await _dbContext.TenantWebhookEndpoints.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == endpointId, cancellationToken);
        if (endpoint is null) return null;

        endpoint.Name = name;
        endpoint.CallbackUrl = callbackUrl;
        endpoint.SubscribedEventTypes = string.IsNullOrWhiteSpace(subscribedEventTypes) ? "*" : subscribedEventTypes;
        endpoint.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapView(endpoint);
    }

    public async Task<bool> DeleteEndpointAsync(Guid tenantId, Guid endpointId, CancellationToken cancellationToken = default)
    {
        var endpoint = await _dbContext.TenantWebhookEndpoints.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == endpointId, cancellationToken);
        if (endpoint is null) return false;
        _dbContext.TenantWebhookEndpoints.Remove(endpoint);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<WebhookEndpointManagementView?> SetEndpointActiveStateAsync(Guid tenantId, Guid endpointId, bool isActive, CancellationToken cancellationToken = default)
    {
        var endpoint = await _dbContext.TenantWebhookEndpoints.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == endpointId, cancellationToken);
        if (endpoint is null) return null;
        endpoint.IsActive = isActive;
        endpoint.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapView(endpoint);
    }

    public async Task<WebhookEndpointManagementResult?> RotateSigningSecretAsync(Guid tenantId, Guid endpointId, CancellationToken cancellationToken = default)
    {
        var endpoint = await _dbContext.TenantWebhookEndpoints.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == endpointId, cancellationToken);
        if (endpoint is null) return null;

        endpoint.NextSigningSecret = _secretIssuer.IssueSecret();
        endpoint.NextSigningSecretIssuedAtUtc = DateTime.UtcNow;
        endpoint.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new WebhookEndpointManagementResult(MapView(endpoint), endpoint.NextSigningSecret, true);
    }

    private static WebhookEndpointManagementView MapView(TenantWebhookEndpoint endpoint)
        => new(
            endpoint.Id,
            endpoint.Name,
            endpoint.CallbackUrl,
            endpoint.SubscribedEventTypes,
            endpoint.IsActive,
            endpoint.SigningSecretIssuedAtUtc,
            endpoint.NextSigningSecretIssuedAtUtc,
            endpoint.CreatedAtUtc,
            endpoint.UpdatedAtUtc);
}
