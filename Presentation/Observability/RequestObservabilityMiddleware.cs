using System.Diagnostics;
namespace Presentation.Observability;

public sealed class RequestObservabilityMiddleware
{
    public const string CorrelationHeaderName = "X-Correlation-ID";

    private readonly RequestDelegate _next;
    private readonly ApiObservability _observability;
    private readonly ILogger<RequestObservabilityMiddleware> _logger;

    public RequestObservabilityMiddleware(
        RequestDelegate next,
        ApiObservability observability,
        ILogger<RequestObservabilityMiddleware> logger)
    {
        _next = next;
        _observability = observability;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);
        context.TraceIdentifier = correlationId;
        context.Response.Headers[CorrelationHeaderName] = correlationId;

        using var requestScope = _observability.BeginRequest();
        using var logScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["TraceId"] = Activity.Current?.TraceId.ToString() ?? string.Empty,
            ["RequestPath"] = context.Request.Path.ToString()
        });

        var route = context.Request.Path.HasValue ? context.Request.Path.Value! : "/";
        var stopwatch = Stopwatch.StartNew();

        using var activity = ApiObservability.ActivitySource.StartActivity($"{context.Request.Method} {route}", ActivityKind.Server);
        activity?.SetTag("correlation.id", correlationId);
        activity?.SetTag("http.request.method", context.Request.Method);
        activity?.SetTag("url.path", route);
        activity?.SetTag("user_agent.original", context.Request.Headers.UserAgent.ToString());

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            var endpointRoute = context.GetEndpoint()?.DisplayName ?? route;
            _observability.RecordRequest(context.Request.Method, endpointRoute, context.Response.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

            activity?.SetTag("http.response.status_code", context.Response.StatusCode);
            _logger.LogInformation(
                "HTTP request completed {Method} {Path} => {StatusCode} in {ElapsedMilliseconds}ms",
                context.Request.Method,
                route,
                context.Response.StatusCode,
                stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(CorrelationHeaderName, out var headerValue) &&
            !string.IsNullOrWhiteSpace(headerValue))
        {
            return headerValue.ToString();
        }

        return Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("n");
    }
}
