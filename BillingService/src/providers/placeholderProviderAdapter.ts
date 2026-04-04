import type { BillingProviderAdapter, ProviderWebhookResult } from '../shared/types.ts';

export class PlaceholderProviderAdapter implements BillingProviderAdapter {
  public readonly name = 'placeholder' as const;

  public async verifyAndNormalizeWebhook(): Promise<ProviderWebhookResult> {
    return {
      accepted: false,
      reason: 'Placeholder provider adapter does not accept live webhooks yet.'
    };
  }
}
