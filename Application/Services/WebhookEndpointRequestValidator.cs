namespace Application.Services;

public static class WebhookEndpointRequestValidator
{
    // Match the TenantWebhookEndpoint column lengths, so a valid request never fails on save.
    public const int MaxNameLength = 120;
    public const int MaxCallbackUrlLength = 500;
    public const int MaxSubscribedEventTypesLength = 500;

    /// <summary>
    /// Validates the trimmed fields of a create/update request. The callback URL must be an absolute http(s) URL
    /// with a host, because the delivery dispatcher posts to it as-is.
    /// </summary>
    public static bool TryValidate(string? name, string? callbackUrl, string? subscribedEventTypes, out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(callbackUrl))
        {
            error = "Name and callbackUrl are required";
            return false;
        }

        if (name.Length > MaxNameLength)
        {
            error = $"Name must be at most {MaxNameLength} characters";
            return false;
        }

        if (callbackUrl.Length > MaxCallbackUrlLength)
        {
            error = $"callbackUrl must be at most {MaxCallbackUrlLength} characters";
            return false;
        }

        if (!Uri.TryCreate(callbackUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || string.IsNullOrEmpty(uri.Host))
        {
            error = "callbackUrl must be an absolute http or https URL";
            return false;
        }

        if (subscribedEventTypes is { Length: > MaxSubscribedEventTypesLength })
        {
            error = $"subscribedEventTypes must be at most {MaxSubscribedEventTypesLength} characters";
            return false;
        }

        return true;
    }
}
