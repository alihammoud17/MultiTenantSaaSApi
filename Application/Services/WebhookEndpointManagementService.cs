using Domain.Entities;
using Domain.Interfaces;
using Infrastructure.Data;

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

    public async Task<Guid> CreateEndpointAsync(Guid tenantId, string name, string callbackUrl, string subscribedEventTypes, CancellationToken cancellationToken = default)
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
        return endpoint.Id;
    }
}
