using System.ComponentModel;
using Domain.Authorization;
using Domain.Interfaces;
using Domain.Outputs;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Presentation.Mcp.Tools;

[McpServerToolType]
public sealed class TenantReadTools(
    ITenantContext tenantContext,
    IEntitlementEnforcer entitlementEnforcer,
    IUsageAnalyticsService usageAnalytics,
    IAuditService auditService,
    ITenantCorsOriginService corsOriginService,
    IWebhookEndpointManagementService webhookEndpointManagementService)
{
    // Mirrors AnalyticsController.GetUsageSummary: same policy, same entitlement, same service call.
    [McpServerTool(Name = "get_usage_summary", ReadOnly = true)]
    [Description(
        "Summarizes activity for the caller's tenant only over the last N days, built from its audit log: " +
        "the UTC window, total events, distinct users, event counts per UTC day, and the 10 most frequent actions " +
        "with their counts. Use list_audit_logs for the individual entries.")]
    [Authorize(Policy = RbacPolicyNames.AuditLogsRead)]
    public async Task<TenantUsageAnalyticsSummary> GetUsageSummary(
        [Description("Number of days back from now to cover, between 1 and 90 (values outside are clamped). Default 30.")] int days = 30,
        [Description("Only count entries with exactly this action name (case-sensitive), e.g. USER_LOGGED_IN. Omit for all actions.")] string? action = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureFeatureEnabledAsync(EntitlementKeys.AnalyticsAuditLogsRead, cancellationToken);

        // The service scopes the query by ITenantContext itself.
        return await usageAnalytics.GetTenantUsageSummaryAsync(days, action, cancellationToken);
    }

    // Mirrors AuditLogsController.GetTenantAuditLogs: same policy, same entitlement, same service call.
    [McpServerTool(Name = "list_audit_logs", ReadOnly = true)]
    [Description(
        "Lists audit log entries for the caller's tenant only, newest first, one page at a time. " +
        "Each entry has the action, the entity type and ID, the acting user ID, the JSON changes (if any), " +
        "the UTC timestamp and the client IP address.")]
    [Authorize(Policy = RbacPolicyNames.AuditLogsRead)]
    public async Task<IReadOnlyCollection<TenantAuditLogItem>> ListAuditLogs(
        [Description("1-based page number. Values below 1 are treated as 1. Default 1.")] int page = 1,
        [Description("Entries per page, between 1 and 200 (values outside are clamped). Default 50.")] int pageSize = 50,
        [Description("Only entries with exactly this action name (case-sensitive), e.g. USER_LOGGED_IN. Omit for all actions.")] string? action = null,
        [Description("Only entries at or after this UTC time (ISO 8601). Omit for no lower bound.")] DateTime? fromUtc = null,
        [Description("Only entries at or before this UTC time (ISO 8601). Omit for no upper bound.")] DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureFeatureEnabledAsync(EntitlementKeys.AnalyticsAuditLogsRead, cancellationToken);

        // IAuditService takes no CancellationToken (nor does the controller action); not changed in this slice.
        return await auditService.GetTenantAuditLogsAsync(page, pageSize, action, fromUtc, toUtc);
    }

    // Mirrors TenantCorsOriginsController.List: same policy, same entitlement, same service call.
    [McpServerTool(Name = "list_cors_origins", ReadOnly = true)]
    [Description(
        "Lists the browser origins the caller's tenant has registered as allowed for cross-origin (CORS) requests " +
        "to this API, sorted alphabetically. Each entry has its ID, the exact origin (scheme, host and optional port, " +
        "e.g. https://app.example.com) and its UTC creation time. Covers the caller's tenant only; origins configured " +
        "for the whole platform are not included.")]
    [Authorize(Policy = RbacPolicyNames.TenantsRead)]
    public async Task<IReadOnlyCollection<TenantCorsOriginView>> ListCorsOrigins(CancellationToken cancellationToken = default)
    {
        await EnsureFeatureEnabledAsync(EntitlementKeys.SecurityCorsOriginsManage, cancellationToken);

        return await corsOriginService.ListAsync(tenantContext.TenantId, cancellationToken);
    }

    // Mirrors WebhookEndpointsController.List: same policy, and no entitlement check because the controller has none.
    [McpServerTool(Name = "list_webhook_endpoints", ReadOnly = true)]
    [Description(
        "Lists the outbound webhook endpoints registered by the caller's tenant only, sorted by name. Each entry has its ID, name, " +
        "callback URL, subscribed event types (comma-separated; * means all events), whether it is active, " +
        "when its signing secret was issued, when a pending rotation secret was issued (null if no rotation is pending), " +
        "and its UTC creation and update times. Signing secrets are never included.")]
    [Authorize(Policy = RbacPolicyNames.BillingManage)]
    public async Task<IReadOnlyCollection<WebhookEndpointManagementView>> ListWebhookEndpoints(CancellationToken cancellationToken = default)
    {
        return await webhookEndpointManagementService.ListEndpointsAsync(tenantContext.TenantId, cancellationToken);
    }

    // Same entitlement check as the controllers' EnforceFeatureAsync. A denial becomes a tool error result
    // (IsError = true) carrying the reason, so the model can explain it, instead of an HTTP 403.
    private async Task EnsureFeatureEnabledAsync(string entitlementKey, CancellationToken cancellationToken)
    {
        var enforcement = await entitlementEnforcer.EnsureFeatureEnabledAsync(tenantContext.TenantId, entitlementKey, cancellationToken);
        if (!enforcement.Allowed)
        {
            throw new McpException(enforcement.DenialReason ?? $"Feature '{entitlementKey}' is not enabled for this tenant.");
        }
    }
}
