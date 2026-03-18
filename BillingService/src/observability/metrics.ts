export interface RequestMetric {
  method: string;
  route: string;
  statusCode: number;
  durationMs: number;
}

export class BillingMetrics {
  private activeRequests = 0;
  private requestsByRoute = new Map<string, number>();
  private requestsByStatus = new Map<string, number>();
  private totalRequests = 0;

  beginRequest() {
    this.activeRequests += 1;

    return {
      end: () => {
        this.activeRequests = Math.max(0, this.activeRequests - 1);
      }
    };
  }

  recordRequest(metric: RequestMetric) {
    this.totalRequests += 1;
    const routeKey = `${metric.method} ${metric.route}`;
    const statusKey = String(metric.statusCode);

    this.requestsByRoute.set(routeKey, (this.requestsByRoute.get(routeKey) ?? 0) + 1);
    this.requestsByStatus.set(statusKey, (this.requestsByStatus.get(statusKey) ?? 0) + 1);
  }

  snapshot() {
    return {
      service: 'billing-service',
      activeRequests: this.activeRequests,
      totalRequests: this.totalRequests,
      requestsByRoute: Object.fromEntries([...this.requestsByRoute.entries()].sort(([a], [b]) => a.localeCompare(b))),
      requestsByStatus: Object.fromEntries([...this.requestsByStatus.entries()].sort(([a], [b]) => a.localeCompare(b)))
    };
  }
}
