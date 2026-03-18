import { loadConfig } from '../config/env.ts';
import { BillingProviderAdapter } from '../shared/types.ts';
import { PlaceholderProviderAdapter } from './placeholderProviderAdapter.ts';

export function createProviderAdapter(): BillingProviderAdapter {
  const config = loadConfig();

  switch (config.provider) {
    case 'placeholder':
    case 'stripe':
    case 'paddle':
      return new PlaceholderProviderAdapter();
    default:
      return new PlaceholderProviderAdapter();
  }
}
