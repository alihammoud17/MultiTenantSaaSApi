using Domain.Interfaces;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Presentation.Mcp;

// Writes one audit entry per MCP tool call, so MCP use shows up in the tenant's audit log like any other client.
// Registered after AddAuthorizationFilters, so it only sees calls that passed the tool's RBAC policy.
// Only the tool name and whether it returned an error are recorded: no arguments, no results.
public static class McpToolCallAuditFilter
{
    public const string AuditAction = "MCP_TOOL_CALLED";
    public const string AuditEntityType = "McpTool";

    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Create(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (request, cancellationToken) =>
        {
            CallToolResult result;
            try
            {
                result = await next(request, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A tool's McpException (e.g. an entitlement denial) surfaces here as an exception; the SDK turns
                // it into an IsError result only after this pipeline. Audit it, then let it continue unchanged.
                await LogAsync(request, isError: true);
                throw;
            }

            await LogAsync(request, isError: result.IsError ?? false);
            return result;
        };

    // Stateless transport: request.Services is the HTTP request scope, so IAuditService sees the
    // ITenantContext set by TenantMiddleware and the caller's claims.
    private static Task LogAsync(RequestContext<CallToolRequestParams> request, bool isError) =>
        request.Services!.GetRequiredService<IAuditService>().LogAsync(
            AuditAction,
            AuditEntityType,
            request.Params?.Name ?? "unknown",
            new { isError });
}
