import { resolve } from 'node:path';
import { BillingServiceConfig, BillingProvider } from '../shared/types.ts';

const supportedProviders: BillingProvider[] = ['placeholder', 'stripe', 'paddle'];

function toNumber(value: string | undefined, fallback: number): number {
  const parsed = Number(value ?? fallback);
  if (!Number.isFinite(parsed) || parsed <= 0) {
    return fallback;
  }

  return parsed;
}

export function loadConfig(env: Record<string, string | undefined> = process.env): BillingServiceConfig {
  const provider = (env.BILLING_PROVIDER ?? 'placeholder') as BillingProvider;

  if (!supportedProviders.includes(provider)) {
    throw new Error(`Unsupported BILLING_PROVIDER: ${provider}`);
  }

  return {
    port: toNumber(env.PORT, 3001),
    nodeEnv: env.NODE_ENV ?? 'development',
    provider,
    serviceName: env.SERVICE_NAME ?? 'billing-service',
    webhookSigningSecret: env.WEBHOOK_SIGNING_SECRET,
    callbackBaseUrl: env.DOTNET_CALLBACK_BASE_URL,
    workflowStatePath: env.WORKFLOW_STATE_PATH ?? resolve(process.cwd(), '.billing-workflow-state.json'),
    workflowMaxAttempts: toNumber(env.WORKFLOW_MAX_ATTEMPTS, 3),
    workflowInitialBackoffMs: toNumber(env.WORKFLOW_INITIAL_BACKOFF_MS, 1_000),
    workflowMaxBackoffMs: toNumber(env.WORKFLOW_MAX_BACKOFF_MS, 30_000),
    workflowPollIntervalMs: toNumber(env.WORKFLOW_POLL_INTERVAL_MS, 2_000),
    reconciliationIntervalMs: toNumber(env.RECONCILIATION_INTERVAL_MS, 300_000)
  };
}
