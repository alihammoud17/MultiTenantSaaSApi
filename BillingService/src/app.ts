import { createServer, IncomingMessage, ServerResponse } from 'node:http';
import { loadConfig } from './config/env.ts';
import { SubscriptionSyncJob } from './jobs/subscriptionSyncJob.ts';
import { createProviderAdapter } from './providers/index.ts';
import { getHealthPayload } from './routes/healthRoute.ts';
import { handleProviderWebhook } from './routes/webhookRoute.ts';
import { SubscriptionWebhookHandler } from './webhooks/handlers/subscriptionWebhookHandler.ts';

async function readRawBody(req: IncomingMessage): Promise<string> {
  const chunks: Buffer[] = [];

  for await (const chunk of req) {
    chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk));
  }

  return Buffer.concat(chunks).toString('utf8');
}

function writeJson(res: ServerResponse, statusCode: number, body: Record<string, unknown>) {
  res.statusCode = statusCode;
  res.setHeader('content-type', 'application/json');
  res.end(JSON.stringify(body));
}

export function createApp() {
  const config = loadConfig();
  const providerAdapter = createProviderAdapter();
  const webhookHandler = new SubscriptionWebhookHandler(providerAdapter, new SubscriptionSyncJob());

  const server = createServer(async (req, res) => {
    try {
      const method = req.method ?? 'GET';
      const url = req.url ?? '/';

      if (method === 'GET' && url === '/health') {
        writeJson(res, 200, getHealthPayload(config));
        return;
      }

      if (method === 'POST' && url === '/webhooks/provider') {
        const rawBody = await readRawBody(req);
        const response = await handleProviderWebhook(webhookHandler, {
          rawBody,
          signature: req.headers['x-provider-signature'] as string | undefined,
          headers: req.headers
        });

        writeJson(res, response.status, response.body);
        return;
      }

      writeJson(res, 404, { error: 'Not found' });
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Unknown error';
      console.error('billing-service.unhandled-error', { message });
      writeJson(res, 500, { error: 'Internal server error' });
    }
  });

  return { server, config };
}
