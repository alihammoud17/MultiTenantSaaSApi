namespace Domain.Interfaces;

public interface IWebhookEndpointSecretIssuer
{
    string IssueSecret();
}
