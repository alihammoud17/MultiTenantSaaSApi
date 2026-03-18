namespace Domain.Interfaces;

public interface IInternalRequestSignatureValidator
{
    bool IsSignatureValid(string payload, string? timestamp, string? signature);
}
