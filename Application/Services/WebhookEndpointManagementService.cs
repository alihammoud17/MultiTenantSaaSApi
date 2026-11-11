using Domain.Entities;
using Domain.Interfaces;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

public sealed class WebhookEndpointManagementService : IWebhookEndpointManagementService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IWebhookEndpointSecretIssuer _secretIssuer;
    private readonly IAuditService _auditService;

    public WebhookEndpointManagementService(ApplicationDbContext dbContext, IWebhookEndpointSecretIssuer secretIssuer, IAuditService auditService)
    {
        _dbContext = dbContext;
        _secretIssuer = secretIssuer;
        _auditService = auditService;
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
        await AuditAsync("WEBHOOK_ENDPOINT_CREATED", endpoint, new { endpoint.Name, endpoint.CallbackUrl, endpoint.SubscribedEventTypes });
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

        var before = new { endpoint.Name, endpoint.CallbackUrl, endpoint.SubscribedEventTypes };
        endpoint.Name = name;
        endpoint.CallbackUrl = callbackUrl;
        endpoint.SubscribedEventTypes = string.IsNullOrWhiteSpace(subscribedEventTypes) ? "*" : subscribedEventTypes;
        endpoint.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await AuditAsync("WEBHOOK_ENDPOINT_UPDATED", endpoint, new
        {
            Before = before,
            After = new { endpoint.Name, endpoint.CallbackUrl, endpoint.SubscribedEventTypes }
        });
        return MapView(endpoint);
    }

    public Task<bool> IsNameTakenAsync(Guid tenantId, string name, Guid? exceptEndpointId = null, CancellationToken cancellationToken = default)
        => _dbContext.TenantWebhookEndpoints.AnyAsync(
            x => x.TenantId == tenantId && x.Name == name && (exceptEndpointId == null || x.Id != exceptEndpointId),
            cancellationToken);

    public async Task<bool> DeleteEndpointAsync(Guid tenantId, Guid endpointId, CancellationToken cancellationToken = default)
    {
        var endpoint = await _dbContext.TenantWebhookEndpoints.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == endpointId, cancellationToken);
        if (endpoint is null) return false;
        _dbContext.TenantWebhookEndpoints.Remove(endpoint);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await AuditAsync("WEBHOOK_ENDPOINT_DELETED", endpoint, new { endpoint.Name, endpoint.CallbackUrl });
        return true;
    }

    public async Task<WebhookEndpointManagementView?> SetEndpointActiveStateAsync(Guid tenantId, Guid endpointId, bool isActive, CancellationToken cancellationToken = default)
    {
        var endpoint = await _dbContext.TenantWebhookEndpoints.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == endpointId, cancellationToken);
        if (endpoint is null) return null;
        endpoint.IsActive = isActive;
        endpoint.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await AuditAsync(isActive ? "WEBHOOK_ENDPOINT_ACTIVATED" : "WEBHOOK_ENDPOINT_DEACTIVATED", endpoint, new { endpoint.Name });
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
        await AuditAsync("WEBHOOK_ENDPOINT_SECRET_ROTATED", endpoint, new { endpoint.Name, endpoint.NextSigningSecretIssuedAtUtc });
        return new WebhookEndpointManagementResult(MapView(endpoint), endpoint.NextSigningSecret, true);
    }

    // Called only after a successful save. Callers build `changes` field by field: never pass the entity or a
    // WebhookEndpointManagementResult, because both can carry a plaintext signing secret.
    private Task AuditAsync(string action, TenantWebhookEndpoint endpoint, object changes)
        => _auditService.LogAsync(action, nameof(TenantWebhookEndpoint), endpoint.Id.ToString(), changes);

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
