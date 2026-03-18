import test from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import { createApp } from '../src/app.ts';

async function makeRequest(path: string, options?: { method?: string; body?: string }) {
  process.env.BILLING_PROVIDER = 'placeholder';
  const { server } = createApp();
  server.listen(0);
  await once(server, 'listening');

  const address = server.address();
  if (!address || typeof address === 'string') {
    throw new Error('Expected TCP server address');
  }

  try {
    const response = await fetch(`http://127.0.0.1:${address.port}${path}`, {
      method: options?.method,
      headers: { 'content-type': 'application/json' },
      body: options?.body
    });

    return {
      status: response.status,
      body: await response.json()
    };
  } finally {
    server.close();
    await once(server, 'close');
  }
}

test('GET /health returns billing service status payload', async () => {
  const response = await makeRequest('/health');

  assert.equal(response.status, 200);
  assert.equal(response.body.status, 'ok');
  assert.equal(response.body.service, 'billing-service');
  assert.equal(response.body.provider, 'placeholder');
});

test('POST /webhooks/provider returns placeholder response', async () => {
  const response = await makeRequest('/webhooks/provider', {
    method: 'POST',
    body: JSON.stringify({ type: 'subscription.updated' })
  });

  assert.equal(response.status, 202);
  assert.equal(response.body.accepted, false);
  assert.match(response.body.reason, /Placeholder provider adapter/);
});
