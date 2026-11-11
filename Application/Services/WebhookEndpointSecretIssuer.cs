using System.Security.Cryptography;
using Domain.Interfaces;

namespace Application.Services;

public sealed class WebhookEndpointSecretIssuer : IWebhookEndpointSecretIssuer
{
    public string IssueSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(48);
        return Convert.ToBase64String(bytes);
    }
}
