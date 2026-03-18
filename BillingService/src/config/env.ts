import { BillingServiceConfig, BillingProvider } from '../shared/types.ts';

const supportedProviders: BillingProvider[] = ['placeholder', 'stripe', 'paddle'];

export function loadConfig(env: Record<string, string | undefined> = process.env): BillingServiceConfig {
  const provider = (env.BILLING_PROVIDER ?? 'placeholder') as BillingProvider;

  if (!supportedProviders.includes(provider)) {
    throw new Error(`Unsupported BILLING_PROVIDER: ${provider}`);
  }

  return {
    port: Number(env.PORT ?? 3001),
    nodeEnv: env.NODE_ENV ?? 'development',
    provider,
    webhookSigningSecret: env.WEBHOOK_SIGNING_SECRET,
    callbackBaseUrl: env.DOTNET_CALLBACK_BASE_URL
  };
}
