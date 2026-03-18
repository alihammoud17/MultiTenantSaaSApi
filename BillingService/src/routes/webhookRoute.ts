import { SubscriptionWebhookHandler } from '../webhooks/handlers/subscriptionWebhookHandler.ts';

export async function handleProviderWebhook(
  handler: SubscriptionWebhookHandler,
  request: {
    rawBody: string;
    signature?: string;
    headers: Record<string, string | string[] | undefined>;
  }
) {
  return handler.handle(request);
}
