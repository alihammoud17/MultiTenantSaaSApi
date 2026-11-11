using Domain.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Application.Services;

public sealed class InternalRequestSignatureValidator : IInternalRequestSignatureValidator
{
    private readonly string _sharedSecret;
    private readonly TimeSpan _allowedClockSkew;

    public InternalRequestSignatureValidator(IConfiguration configuration)
    {
        _sharedSecret = configuration["BillingIntegration:SharedSecret"] ?? string.Empty;
        _allowedClockSkew = HmacRequestSignature.ReadClockSkew(configuration["BillingIntegration:AllowedClockSkewMinutes"]);
    }

    public bool IsSignatureValid(string payload, string? timestamp, string? signature)
    {
        return HmacRequestSignature.IsValid(_sharedSecret, _allowedClockSkew, payload, timestamp, signature);
    }
}
