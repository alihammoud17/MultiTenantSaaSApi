using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.DTOs
{
    public record RegisterTenantRequest(
        string CompanyName,
        string Subdomain,
        string AdminEmail,
        string AdminPassword
    );

    public record LoginRequest(
        string Email,
        string Password
    );

    public record RefreshTokenRequest(
        Guid TenantId,
        string RefreshToken
    );

    public record RevokeRefreshTokenRequest(
        Guid TenantId,
        string RefreshToken,
        string? Reason = null
    );
}
