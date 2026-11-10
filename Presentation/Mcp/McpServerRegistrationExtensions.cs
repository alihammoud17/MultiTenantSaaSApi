using ModelContextProtocol.AspNetCore;
using Presentation.Mcp.Tools;

namespace Presentation.Mcp;

public static class McpServerRegistrationExtensions
{
    public const string RoutePattern = "/api/v1/mcp";

    // Stateless is the SDK default, but it is set explicitly: tools must resolve services from the request's
    // own scope (HttpContext.RequestServices) to see the ITenantContext that TenantMiddleware populated.
    public static IServiceCollection AddTenantMcpServer(this IServiceCollection services)
    {
        services.AddMcpServer()
            .WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless)
            .AddAuthorizationFilters()
            .WithTools<TenantReadTools>();

        return services;
    }

    // RequireAuthorization rejects requests without a valid JWT with 401 in UseAuthorization, before
    // TenantMiddleware could accept an X-Tenant-ID hint for an anonymous caller. Per-tool RBAC policies
    // are enforced by AddAuthorizationFilters.
    public static IEndpointConventionBuilder MapTenantMcpServer(this IEndpointRouteBuilder endpoints)
        => endpoints.MapMcp(RoutePattern).RequireAuthorization();
}
