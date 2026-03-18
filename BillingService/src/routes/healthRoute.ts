import { BillingServiceConfig } from '../shared/types.ts';

export function getHealthPayload(config: BillingServiceConfig) {
  return {
    status: 'ok',
    service: 'billing-service',
    provider: config.provider,
    nodeEnv: config.nodeEnv
  };
}
