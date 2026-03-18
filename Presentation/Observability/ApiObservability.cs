using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Presentation.Observability;

public sealed class ApiObservability
{
    public const string ServiceName = "multi-tenant-saas-api";
    public static readonly ActivitySource ActivitySource = new(ServiceName);
    private static readonly Meter Meter = new(ServiceName);

    private readonly Counter<long> _requestCounter = Meter.CreateCounter<long>(
        "http.server.requests",
        unit: "requests",
        description: "Total HTTP requests handled by the API.");

    private readonly Histogram<double> _requestDurationMs = Meter.CreateHistogram<double>(
        "http.server.request.duration",
        unit: "ms",
        description: "HTTP request duration in milliseconds.");

    private long _activeRequests;
    private readonly ConcurrentDictionary<string, long> _requestsByRoute = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _requestsByStatus = new(StringComparer.OrdinalIgnoreCase);

    public ApiObservability()
    {
        Meter.CreateObservableGauge(
            "http.server.active_requests",
            () => Interlocked.Read(ref _activeRequests),
            unit: "requests",
            description: "Current in-flight HTTP requests.");
    }

    public IDisposable BeginRequest() => new ActiveRequestScope(this);

    public void RecordRequest(string method, string route, int statusCode, double durationMs)
    {
        var tags = new TagList
        {
            { "http.request.method", method },
            { "http.route", route },
            { "http.response.status_code", statusCode }
        };

        _requestCounter.Add(1, tags);
        _requestDurationMs.Record(durationMs, tags);

        _requestsByRoute.AddOrUpdate($"{method} {route}", 1, static (_, count) => count + 1);
        _requestsByStatus.AddOrUpdate(statusCode.ToString(), 1, static (_, count) => count + 1);
    }

    public object GetSnapshot() => new
    {
        service = ServiceName,
        activeRequests = Interlocked.Read(ref _activeRequests),
        requestsByRoute = _requestsByRoute.OrderBy(x => x.Key).ToDictionary(),
        requestsByStatus = _requestsByStatus.OrderBy(x => x.Key).ToDictionary()
    };

    private sealed class ActiveRequestScope : IDisposable
    {
        private readonly ApiObservability _observability;
        private bool _disposed;

        public ActiveRequestScope(ApiObservability observability)
        {
            _observability = observability;
            Interlocked.Increment(ref _observability._activeRequests);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Interlocked.Decrement(ref _observability._activeRequests);
            _disposed = true;
        }
    }
}
