namespace Domain.Interfaces;

/// <summary>Verifies operator requests to the internal tenant-operations endpoints (separate secret from billing).</summary>
public interface ITenantOperationsSignatureValidator
{
    bool IsSignatureValid(string payload, string? timestamp, string? signature);
}
