import { BillingMetrics } from './metrics.ts';
import { logger } from './logger.ts';

export const correlationHeaderName = 'x-correlation-id';

export interface RequestObserver {
  correlationId: string;
  traceId: string;
  finish(statusCode: number): void;
}

export function beginObservedRequest(
  metrics: BillingMetrics,
  method: string,
  route: string,
  incomingCorrelationId?: string
): RequestObserver {
  const correlationId = incomingCorrelationId?.trim() || generateCorrelationId();
  const traceId = correlationId.replace(/-/g, '').padEnd(32, '0').slice(0, 32);
  const startedAt = Date.now();
  const activeRequest = metrics.beginRequest();

  logger.info('http.request.started', { method, route, correlationId, traceId });

  return {
    correlationId,
    traceId,
    finish(statusCode) {
      const durationMs = Date.now() - startedAt;
      activeRequest.end();
      metrics.recordRequest({ method, route, statusCode, durationMs });
      logger.info('http.request.completed', {
        method,
        route,
        statusCode,
        durationMs,
        correlationId,
        traceId
      });
    }
  };
}

function generateCorrelationId(): string {
  return `corr-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
}
