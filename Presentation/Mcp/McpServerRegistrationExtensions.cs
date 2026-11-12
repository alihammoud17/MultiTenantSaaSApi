using ModelContextProtocol.AspNetCore;
using Presentation.Mcp.Tools;

namespace Presentation.Mcp;

public static class McpServerRegistrationExtensions
{
    public const string RoutePattern = "/api/v1/mcp";

    // JwtBearer scheme that accepts only tokens whose audience is Jwt:McpAudience (issued by POST /api/v1/auth/mcp-token).
    public const string AuthenticationScheme = "McpBearer";

    // Stateless is the SDK default, but it is set explicitly: tools must resolve services from the request's
    // own scope (HttpContext.RequestServices) to see the ITenantContext that TenantMiddleware populated.
    public static IServiceCollection AddTenantMcpServer(this IServiceCollection services)
    {
        services.AddMcpServer()
            .WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless)
            .AddAuthorizationFilters()
            // After the authorization filters: only calls that passed the tool's RBAC policy are audited.
            .WithRequestFilters(filters => filters.AddCallToolFilter(McpToolCallAuditFilter.Create))
            .WithTools<TenantReadTools>();

        return services;
    }

    // The policy authenticates with the MCP scheme only, so API tokens (the default scheme's audience) get 401 here,
    // and UseAuthorization sets HttpContext.User from the MCP token before TenantMiddleware resolves the tenant.
    // It rejects requests without a valid MCP token before TenantMiddleware could accept an X-Tenant-ID hint for an
    // anonymous caller. Per-tool RBAC policies are enforced by AddAuthorizationFilters.
    public static IEndpointConventionBuilder MapTenantMcpServer(this IEndpointRouteBuilder endpoints)
        => endpoints.MapMcp(RoutePattern).RequireAuthorization(policy => policy
            .AddAuthenticationSchemes(AuthenticationScheme)
            .RequireAuthenticatedUser());
}
