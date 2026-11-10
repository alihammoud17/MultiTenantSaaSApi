using System.ComponentModel;
using Domain.Interfaces;
using ModelContextProtocol.Server;

namespace Presentation.Mcp.Tools;

[McpServerToolType]
public sealed class TenantReadTools(ITenantContext tenantContext)
{
    // Temporary diagnostic for step 2.2: proves the tool sees the ITenantContext set by TenantMiddleware
    // (stateless transport, same DI scope). Remove before Block 3 is done.
    [McpServerTool(Name = "whoami_tenant", ReadOnly = true)]
    [Description("Diagnostic: returns the ID of the tenant the caller's access token belongs to. Takes no input.")]
    public Guid WhoAmITenant() => tenantContext.TenantId;
}
