import { BillingServiceConfig } from '../shared/types.ts';
import { BillingMetrics } from '../observability/metrics.ts';

export function getHealthPayload(config: BillingServiceConfig, metrics: BillingMetrics, correlationId: string) {
  return {
    status: 'ok',
    service: config.serviceName,
    provider: config.provider,
    nodeEnv: config.nodeEnv,
    correlationId,
    checks: {
      self: 'ok'
    },
    metrics: metrics.snapshot()
  };
}
