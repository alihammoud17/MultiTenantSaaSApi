import { createServer, IncomingMessage, ServerResponse } from 'node:http';
import { loadConfig } from './config/env.ts';
import { SubscriptionSyncJob } from './jobs/subscriptionSyncJob.ts';
import { beginObservedRequest, correlationHeaderName } from './observability/requestContext.ts';
import { BillingMetrics } from './observability/metrics.ts';
import { logger } from './observability/logger.ts';
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

function writeJson(
  res: ServerResponse,
  statusCode: number,
  body: Record<string, unknown>,
  correlationId: string,
  traceId: string
) {
  res.statusCode = statusCode;
  res.setHeader('content-type', 'application/json');
  res.setHeader(correlationHeaderName, correlationId);
  res.setHeader('x-trace-id', traceId);
  res.end(JSON.stringify(body));
}

export function createApp() {
  const config = loadConfig();
  const metrics = new BillingMetrics();
  const providerAdapter = createProviderAdapter();
  const webhookHandler = new SubscriptionWebhookHandler(providerAdapter, new SubscriptionSyncJob());

  const server = createServer(async (req, res) => {
    const method = req.method ?? 'GET';
    const url = req.url ?? '/';
    const observer = beginObservedRequest(metrics, method, url, req.headers[correlationHeaderName] as string | undefined);

    try {
      if (method === 'GET' && url === '/health') {
        writeJson(res, 200, getHealthPayload(config, metrics, observer.correlationId), observer.correlationId, observer.traceId);
        observer.finish(200);
        return;
      }

      if (method === 'GET' && url === '/metrics') {
        writeJson(res, 200, {
          service: config.serviceName,
          correlationId: observer.correlationId,
          traceId: observer.traceId,
          generatedAtUtc: new Date().toISOString(),
          metrics: metrics.snapshot()
        }, observer.correlationId, observer.traceId);
        observer.finish(200);
        return;
      }

      if (method === 'POST' && url === '/webhooks/provider') {
        const rawBody = await readRawBody(req);
        const response = await handleProviderWebhook(webhookHandler, {
          rawBody,
          signature: req.headers['x-provider-signature'] as string | undefined,
          headers: req.headers
        });

        writeJson(res, response.status, {
          ...response.body,
          correlationId: observer.correlationId,
          traceId: observer.traceId
        }, observer.correlationId, observer.traceId);
        observer.finish(response.status);
        return;
      }

      writeJson(res, 404, { error: 'Not found', correlationId: observer.correlationId }, observer.correlationId, observer.traceId);
      observer.finish(404);
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Unknown error';
      logger.error('billing-service.unhandled-error', {
        message,
        correlationId: observer.correlationId,
        traceId: observer.traceId,
        route: url,
        method
      });
      writeJson(res, 500, { error: 'Internal server error', correlationId: observer.correlationId }, observer.correlationId, observer.traceId);
      observer.finish(500);
    }
  });

  return { server, config, metrics };
}
