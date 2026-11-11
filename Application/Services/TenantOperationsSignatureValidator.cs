using Domain.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Application.Services;

// Uses its own secret so a leaked billing secret cannot suspend tenants. Unset secret = endpoints disabled.
public sealed class TenantOperationsSignatureValidator : ITenantOperationsSignatureValidator
{
    private readonly string _sharedSecret;
    private readonly TimeSpan _allowedClockSkew;

    public TenantOperationsSignatureValidator(IConfiguration configuration)
    {
        _sharedSecret = configuration["TenantOperations:SharedSecret"] ?? string.Empty;
        _allowedClockSkew = HmacRequestSignature.ReadClockSkew(configuration["TenantOperations:AllowedClockSkewMinutes"]);
    }

    public bool IsSignatureValid(string payload, string? timestamp, string? signature)
    {
        return HmacRequestSignature.IsValid(_sharedSecret, _allowedClockSkew, payload, timestamp, signature);
    }
}
