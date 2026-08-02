# BillingService Technical Documentation

## Purpose

`BillingService` is a minimal Node.js + TypeScript companion service intended to become the billing, webhook, and subscription-workflow boundary for the platform.

At the current stage, it is deliberately small and placeholder-oriented:
- it exposes a health endpoint
- it exposes a placeholder webhook endpoint
- it defines internal billing contracts and extension points
- it now includes a durable file-backed workflow queue, retry/backoff worker, dead-letter handling, and a reconciliation summary skeleton
- it still does **not** yet integrate with Stripe/Paddle live webhooks or authenticated .NET callback delivery

The .NET API remains the system of record. `BillingService` is only a future-facing orchestration boundary.

---

## High-level architecture

The service is organized into the following areas:

- `src/index.ts` - process entrypoint
- `src/app.ts` - composition root and HTTP server wiring
- `src/config/` - runtime configuration loading
- `src/routes/` - route-level request/response helpers
- `src/webhooks/handlers/` - webhook orchestration logic
- `src/providers/` - billing provider adapter selection and implementations
- `src/jobs/` - placeholder background job abstractions
- `src/shared/` - shared contracts and internal types
- `src/node-shims.d.ts` - local type declarations used by TypeScript in this minimal setup
- `tests/` - endpoint-level tests

### Runtime request flow

For the current placeholder webhook flow, the request path is:

1. `src/index.ts` starts the server.
2. `src/app.ts` creates the app dependencies.
3. `createServer(...)` receives an incoming HTTP request.
4. `GET /health` is handled by `getHealthPayload(...)`.
5. `POST /webhooks/provider` reads the raw body and delegates to `handleProviderWebhook(...)`.
6. `handleProviderWebhook(...)` forwards the request to `SubscriptionWebhookHandler.handle(...)`.
7. `SubscriptionWebhookHandler` asks the selected `BillingProviderAdapter` to verify and normalize the webhook.
8. The current `PlaceholderProviderAdapter` rejects live processing and returns a placeholder result.
9. The handler returns a `202 Accepted` response with an explanatory payload.

When a provider adapter returns a normalized internal event, `SubscriptionSyncJob` now writes that event to a durable queue, triggers a background worker with exponential retry/backoff, and dead-letters exhausted items. A reconciliation summary job periodically logs queued/dead-letter counts for diagnostics.

---

## File-by-file reference

## 1. `src/shared/types.ts`

This file contains the core internal contracts used across the service. It is the shared vocabulary that other modules depend on.

### `BillingProvider`

```ts
export type BillingProvider = 'placeholder' | 'stripe' | 'paddle';
```

#### Role
A string union describing which billing provider mode the service is configured to use.

#### Values
- `placeholder` - safe default used by the current scaffold
- `stripe` - reserved for future Stripe integration
- `paddle` - reserved for future Paddle integration

#### Relationships
- Used by `BillingServiceConfig.provider`
- Used by `InternalSubscriptionEvent.provider`
- Used by `BillingProviderAdapter.name`
- Validated in `loadConfig(...)`
- Read in `createProviderAdapter(...)`

---

### `BillingServiceConfig`

```ts
export interface BillingServiceConfig {
  port: number;
  nodeEnv: string;
  provider: BillingProvider;
  webhookSigningSecret?: string;
  callbackBaseUrl?: string;
}
```

#### Role
Represents all runtime configuration currently understood by the billing service.

#### Properties

##### `port: number`
The TCP port the HTTP server listens on.

##### `nodeEnv: string`
The runtime environment name, for example `development` or `production`.

##### `provider: BillingProvider`
The currently selected billing provider mode.

##### `webhookSigningSecret?: string`
Optional placeholder for a future webhook signature verification secret.

##### `callbackBaseUrl?: string`
Optional placeholder for a future authenticated callback destination pointing at the .NET API.

#### Relationships
- Constructed by `loadConfig(...)`
- Returned from `createApp()` together with the server
- Consumed by `getHealthPayload(...)`

---

### `InternalSubscriptionEvent`

```ts
export interface InternalSubscriptionEvent {
  eventId: string;
  eventType: 'subscription.updated' | 'subscription.canceled' | 'invoice.payment_failed';
  provider: BillingProvider;
  tenantId: string;
  subscriptionId: string;
  occurredAt: string;
  correlationId: string;
  payload: Record<string, unknown>;
}
```

#### Role
Defines the service's internal normalized event shape for billing/subscription lifecycle processing.

This contract is intentionally provider-neutral. The goal is to keep raw provider payloads out of downstream internal workflow logic.

#### Properties

##### `eventId: string`
A unique identifier for the external billing event after normalization.

##### `eventType: 'subscription.updated' | 'subscription.canceled' | 'invoice.payment_failed'`
The normalized event category used internally by the service.

##### `provider: BillingProvider`
The provider that originated the event.

##### `tenantId: string`
The internally validated tenant identifier associated with the event.

##### `subscriptionId: string`
The internal or mapped subscription identifier associated with the event.

##### `occurredAt: string`
A timestamp representing when the event happened.

##### `correlationId: string`
A value intended for tracing and diagnostics across service boundaries.

##### `payload: Record<string, unknown>`
A generic object containing normalized or retained event details.

#### Relationships
- Returned through `ProviderWebhookResult.normalizedEvent`
- Consumed by `SubscriptionSyncJob.enqueue(...)`

---

### `ProviderWebhookResult`

```ts
export interface ProviderWebhookResult {
  accepted: boolean;
  reason?: string;
  normalizedEvent?: InternalSubscriptionEvent;
}
```

#### Role
Represents the outcome of asking a provider adapter to verify and normalize a webhook request.

#### Properties

##### `accepted: boolean`
Whether the webhook was accepted for internal processing.

##### `reason?: string`
Optional explanation used when a webhook is not accepted.

##### `normalizedEvent?: InternalSubscriptionEvent`
Optional normalized internal event returned when processing succeeds.

#### Relationships
- Returned by `BillingProviderAdapter.verifyAndNormalizeWebhook(...)`
- Interpreted by `SubscriptionWebhookHandler.handle(...)`

---

### `BillingProviderAdapter`

```ts
export interface BillingProviderAdapter {
  readonly name: BillingProvider;
  verifyAndNormalizeWebhook(input: {
    rawBody: string;
    signature?: string;
    headers: Record<string, string | string[] | undefined>;
  }): Promise<ProviderWebhookResult>;
}
```

#### Role
Defines the provider integration boundary.

Every real billing provider implementation should conform to this interface so the rest of the service can remain provider-agnostic.

#### Properties and methods

##### `name: BillingProvider`
Read-only identifier for the adapter implementation.

##### `verifyAndNormalizeWebhook(...)`
Accepts raw webhook input and returns a `ProviderWebhookResult`.

Expected future responsibilities:
- verify authenticity/signatures
- parse provider payloads
- map provider-specific states to internal event types
- reject unsupported or invalid webhook shapes
- produce a normalized internal event for downstream handling

#### Relationships
- Implemented by `PlaceholderProviderAdapter`
- Constructed via `createProviderAdapter()`
- Consumed by `SubscriptionWebhookHandler`

---

## 2. `src/config/env.ts`

This file converts environment variables into a `BillingServiceConfig` object.

### `supportedProviders`

```ts
const supportedProviders: BillingProvider[] = ['placeholder', 'stripe', 'paddle'];
```

#### Role
An internal whitelist used to validate `BILLING_PROVIDER` input.

#### Relationships
- Used by `loadConfig(...)`

---

### `loadConfig(...)`

```ts
export function loadConfig(env: Record<string, string | undefined> = process.env): BillingServiceConfig
```

#### Role
Builds and validates the runtime configuration object for the service.

#### Parameters

##### `env: Record<string, string | undefined> = process.env`
A dictionary of environment variables. It defaults to the real process environment but can be overridden for tests or future composition scenarios.

#### Return value
A `BillingServiceConfig` object.

#### Behavior
- Reads `BILLING_PROVIDER` and defaults to `placeholder`
- Verifies that the provider is in `supportedProviders`
- Reads `PORT` and defaults to `3001`
- Reads `NODE_ENV` and defaults to `development`
- Reads optional placeholders for signing secret and .NET callback base URL

#### Error behavior
Throws an `Error` if the provider value is unsupported.

#### Relationships
- Called by `createApp()`
- Called by `createProviderAdapter()`

#### Design note
`createProviderAdapter()` currently calls `loadConfig()` again rather than receiving a config object from the composition root. That keeps the current scaffold small, though a future refactor might centralize config injection once the service grows.

---

## 3. `src/providers/placeholderProviderAdapter.ts`

This file contains the placeholder adapter used today.

### `PlaceholderProviderAdapter`

```ts
export class PlaceholderProviderAdapter implements BillingProviderAdapter
```

#### Role
A no-op provider adapter that satisfies the billing provider boundary while intentionally rejecting live webhook processing.

This class makes the webhook pipeline executable without implementing real Stripe/Paddle logic yet.

#### Properties

##### `name = 'placeholder'`
Read-only provider identifier exposed to callers.

#### Methods

##### `verifyAndNormalizeWebhook(): Promise<ProviderWebhookResult>`
Returns a placeholder response indicating that live webhook processing is not implemented.

Current return value:
- `accepted: false`
- `reason: 'Placeholder provider adapter does not accept live webhooks yet.'`

#### Relationships
- Implements `BillingProviderAdapter`
- Created by `createProviderAdapter()`
- Used by `SubscriptionWebhookHandler`

#### Future evolution
Real adapters such as `StripeProviderAdapter` or `PaddleProviderAdapter` would replace or sit alongside this class.

---

## 4. `src/providers/index.ts`

This file selects the adapter implementation to use.

### `createProviderAdapter()`

```ts
export function createProviderAdapter(): BillingProviderAdapter
```

#### Role
Factory function that chooses which provider adapter instance to create.

#### Behavior
- Reads runtime config via `loadConfig()`
- Switches on `config.provider`
- Currently returns `new PlaceholderProviderAdapter()` for all configured modes

#### Why it exists
It isolates provider selection logic from the rest of the application. That means route and handler code only need a `BillingProviderAdapter` contract, not provider-specific construction logic.

#### Relationships
- Called by `createApp()`
- Returns an object implementing `BillingProviderAdapter`
- Uses `loadConfig()`
- Uses `PlaceholderProviderAdapter`

#### Current limitation
Even `stripe` and `paddle` currently map to the placeholder adapter. This is intentional preparation for future expansion.

---

## 5. `src/jobs/subscriptionSyncJob.ts`

This file contains the placeholder background job abstraction.

### `SubscriptionSyncJob`

```ts
export class SubscriptionSyncJob
```

#### Role
Represents the future point where normalized billing events will be handed off for asynchronous subscription synchronization or lifecycle processing.

At present, it only logs metadata. It does not enqueue to a queue, persist work, retry, or call the .NET API.

#### Methods

##### `enqueue(event: InternalSubscriptionEvent): Promise<void>`
Receives a normalized subscription event and logs a structured message.

#### Parameters

##### `event: InternalSubscriptionEvent`
The normalized event that should eventually be processed by background infrastructure.

#### Logged fields
- `eventId`
- `eventType`
- `provider`
- `tenantId`
- `correlationId`

#### Relationships
- Called by `SubscriptionWebhookHandler.handle(...)` when a webhook is accepted and normalized
- Depends on `InternalSubscriptionEvent`

#### Future evolution
This class is a natural place to introduce:
- queue publishing
- idempotency checks
- retry-safe background processing
- .NET callback dispatch

---

## 6. `src/webhooks/handlers/subscriptionWebhookHandler.ts`

This file contains the main orchestration logic for incoming webhook requests.

### `SubscriptionWebhookHandler`

```ts
export class SubscriptionWebhookHandler
```

#### Role
Coordinates webhook processing between the provider adapter and the background job layer.

It does **not** know about HTTP transport directly. Its job is to work with normalized request data and return a transport-friendly result.

#### Properties

##### `adapter: BillingProviderAdapter`
Private read-only dependency used to verify and normalize incoming webhook payloads.

##### `syncJob: SubscriptionSyncJob`
Private read-only dependency used to enqueue accepted internal events.

#### Constructor

##### `constructor(adapter: BillingProviderAdapter, syncJob: SubscriptionSyncJob)`
Initializes the handler with its required dependencies.

###### Parameters
- `adapter` - provider-specific verification and normalization implementation
- `syncJob` - downstream job abstraction for accepted normalized events

#### Methods

##### `handle(input): Promise<{ status: number; body: Record<string, unknown> }>`
Processes a webhook request and returns an HTTP-ready response model.

###### Input shape
- `rawBody: string` - the exact request body content
- `signature?: string` - optional provider signature header
- `headers: Record<string, string | string[] | undefined>` - all request headers

###### Behavior
1. Calls `adapter.verifyAndNormalizeWebhook(input)`.
2. If the result is not accepted, returns a `202` payload describing the rejection.
3. If the result is accepted and includes a normalized event, calls `syncJob.enqueue(...)`.
4. Returns a `202` payload containing `accepted: true`, the provider name, and event id.

###### Return value
A small object shaped for easy conversion into an HTTP response.

#### Relationships
- Created in `createApp()`
- Used by `handleProviderWebhook(...)`
- Depends on `BillingProviderAdapter`
- Depends on `SubscriptionSyncJob`

#### Design note
The handler returns `202 Accepted` in both the accepted and ignored paths. This keeps the placeholder flow stable and communicates that processing is asynchronous or intentionally deferred.

---

## 7. `src/routes/healthRoute.ts`

This file contains the route-level response helper for health checks.

### `getHealthPayload(config: BillingServiceConfig)`

```ts
export function getHealthPayload(config: BillingServiceConfig)
```

#### Role
Produces the JSON payload returned by `GET /health`.

#### Parameters

##### `config: BillingServiceConfig`
The active application configuration.

#### Return value
An object containing:
- `status: 'ok'`
- `service: 'billing-service'`
- `provider: config.provider`
- `nodeEnv: config.nodeEnv`

#### Relationships
- Called by `createApp()`
- Uses `BillingServiceConfig`

#### Why it exists
Separating the payload builder from the HTTP server logic makes the health route response easier to understand, test, and extend.

---

## 8. `src/routes/webhookRoute.ts`

This file contains a small route-level helper for provider webhooks.

### `handleProviderWebhook(...)`

```ts
export async function handleProviderWebhook(handler: SubscriptionWebhookHandler, request: ...)
```

#### Role
A lightweight route adapter that forwards request data to the domain-oriented webhook handler.

#### Parameters

##### `handler: SubscriptionWebhookHandler`
The orchestrator responsible for webhook processing.

##### `request`
An object containing:
- `rawBody: string`
- `signature?: string`
- `headers: Record<string, string | string[] | undefined>`

#### Return value
Returns exactly what `handler.handle(...)` returns.

#### Relationships
- Called by `createApp()` when `POST /webhooks/provider` is hit
- Delegates to `SubscriptionWebhookHandler.handle(...)`

#### Why it exists
This function is currently thin, but it marks a clean separation between route wiring and webhook orchestration.

---

## 9. `src/app.ts`

This file is the application composition root and the central HTTP request router.

### `readRawBody(req: IncomingMessage): Promise<string>`

#### Role
Reads the entire incoming HTTP request body into a UTF-8 string.

#### Parameters

##### `req: IncomingMessage`
The Node HTTP request object.

#### Behavior
- Iterates over request body chunks
- Converts chunks into `Buffer` instances when needed
- Concatenates all chunks
- Returns the final UTF-8 string

#### Relationships
- Called by `createApp()` when handling `POST /webhooks/provider`

#### Why it exists
Webhook verification often depends on the raw body rather than a parsed JSON object. Keeping this helper separate makes that requirement explicit.

---

### `writeJson(res: ServerResponse, statusCode: number, body: Record<string, unknown>)`

#### Role
Writes a JSON HTTP response.

#### Parameters

##### `res: ServerResponse`
The outgoing Node HTTP response object.

##### `statusCode: number`
The HTTP status code to send.

##### `body: Record<string, unknown>`
The serializable response payload.

#### Behavior
- sets `res.statusCode`
- sets `content-type` to `application/json`
- serializes the payload with `JSON.stringify(...)`
- ends the response

#### Relationships
- Used multiple times inside the server request handler

---

### `createApp()`

```ts
export function createApp()
```

#### Role
Constructs the application's runtime dependencies and returns the server plus active configuration.

#### Internal composition
Inside `createApp()` the following objects are created:
- `config` via `loadConfig()`
- `providerAdapter` via `createProviderAdapter()`
- `webhookHandler` via `new SubscriptionWebhookHandler(...)`
- `server` via `createServer(...)`

#### Request handling behavior
The created server implements three behaviors:

##### `GET /health`
Returns the payload produced by `getHealthPayload(config)` with HTTP `200`.

##### `POST /webhooks/provider`
- reads the raw body using `readRawBody(...)`
- extracts `x-provider-signature`
- passes `rawBody`, `signature`, and all headers to `handleProviderWebhook(...)`
- writes the returned result as JSON

##### fallback behavior
Any other route returns `404` with `{ error: 'Not found' }`.

##### error behavior
Any unhandled exception logs `billing-service.unhandled-error` and returns `500` with `{ error: 'Internal server error' }`.

#### Return value
Returns an object with:

##### `server`
The Node HTTP server instance.

##### `config`
The resolved `BillingServiceConfig` instance.

#### Relationships
- Called by `src/index.ts`
- Called by tests in `tests/health.test.ts`
- Depends on nearly every major module in the service

#### Why it is important
This is the true composition root of the service. It wires together configuration, provider selection, routing, transport, and webhook handling.

---

## 10. `src/index.ts`

This file is the executable startup script.

### Top-level composition

```ts
const { server, config } = createApp();
```

#### Role
Retrieves the fully wired server and active configuration from the composition root.

---

### `server.listen(config.port, () => { ... })`

#### Role
Starts the HTTP server.

#### Behavior
When startup succeeds, it logs:
- `port`
- `provider`
- `nodeEnv`

#### Relationships
- Depends on `createApp()`
- Uses `config.port` from `BillingServiceConfig`

#### Why it exists
It keeps process startup separate from composition logic. `createApp()` can therefore be reused in tests without automatically starting the server.

---

## 11. `src/node-shims.d.ts`

This file contains local TypeScript declarations used by the minimal setup.

### Why this file exists
The service intentionally avoids a heavier dependency setup. These declarations provide just enough type information for the current source and test files to compile in this lightweight environment.

This file is a development-time aid only. It does not produce runtime behavior.

### Declared globals and modules

#### `process`
Provides a minimal declaration for `process.env`.

#### `fetch`
Provides a minimal declaration for the global `fetch` used by tests.

#### `Buffer`
Provides a minimal declaration for the `Buffer` API used by `readRawBody(...)`.

#### `module 'node:http'`
Declares:
- `IncomingMessage`
- `ServerResponse`
- `AddressInfo`
- `Server`
- `createServer(...)`

These are the types used by `src/app.ts`.

#### `module 'node:test'`
Declares the default `test(...)` function used by the test file.

#### `module 'node:assert/strict'`
Declares the minimal `assert` API used by tests.

#### `module 'node:events'`
Declares the `once(...)` helper used by tests to await server lifecycle events.

#### Relationships
- Supports `src/app.ts`
- Supports `src/config/env.ts`
- Supports `tests/health.test.ts`

---

## 12. `tests/health.test.ts`

This file contains the current automated tests.

### `makeRequest(path, options?)`

#### Role
Starts the app on an ephemeral port, performs an HTTP request against it, captures the JSON response, and shuts the server down.

#### Parameters

##### `path: string`
The route path to call.

##### `options?: { method?: string; body?: string }`
Optional request options.

#### Behavior
- forces `BILLING_PROVIDER = 'placeholder'`
- creates the app using `createApp()`
- listens on port `0` so the OS picks a free port
- performs a `fetch(...)`
- returns `{ status, body }`
- always closes the server in a `finally` block

#### Relationships
- Shared by both tests
- Depends on `createApp()`

---

### Test: `GET /health returns billing service status payload`

#### Role
Validates that the health route responds successfully and reports the expected service metadata.

#### Assertions
- status is `200`
- `body.status` is `ok`
- `body.service` is `billing-service`
- `body.provider` is `placeholder`

---

### Test: `POST /webhooks/provider returns placeholder response`

#### Role
Validates the placeholder webhook behavior.

#### Assertions
- status is `202`
- `body.accepted` is `false`
- `body.reason` contains the placeholder adapter message

---

## Dependency relationships summary

### Composition graph

- `src/index.ts`
  - depends on `createApp()` from `src/app.ts`
- `src/app.ts`
  - depends on `loadConfig()`
  - depends on `createProviderAdapter()`
  - depends on `SubscriptionWebhookHandler`
  - depends on `SubscriptionSyncJob`
  - depends on `getHealthPayload()`
  - depends on `handleProviderWebhook()`
- `src/providers/index.ts`
  - depends on `loadConfig()`
  - depends on `PlaceholderProviderAdapter`
- `src/webhooks/handlers/subscriptionWebhookHandler.ts`
  - depends on `BillingProviderAdapter`
  - depends on `SubscriptionSyncJob`
- `src/jobs/subscriptionSyncJob.ts`
  - depends on `InternalSubscriptionEvent`
- `src/routes/healthRoute.ts`
  - depends on `BillingServiceConfig`
- `src/routes/webhookRoute.ts`
  - depends on `SubscriptionWebhookHandler`
- `src/config/env.ts`
  - depends on shared types
- `tests/health.test.ts`
  - depends on `createApp()`

### Layering summary

From outermost to innermost:

1. **Process layer** - `src/index.ts`
2. **Transport/composition layer** - `src/app.ts`, `src/routes/*`
3. **Webhook orchestration layer** - `src/webhooks/handlers/*`
4. **Provider abstraction and job layer** - `src/providers/*`, `src/jobs/*`
5. **Shared contract layer** - `src/shared/types.ts`

This layering keeps future provider logic separated from route plumbing and internal contracts.

---

## Current limitations

The following are intentionally not implemented yet:

- real Stripe or Paddle adapters
- webhook signature verification
- persistent idempotency tracking
- queue-backed job processing
- authenticated callbacks into the .NET API
- provider-to-internal subscription mapping persistence
- structured logger abstraction beyond `console.info` / `console.error`

These are appropriate next steps once the scaffold moves beyond placeholder behavior.

---

## Recommended future documentation updates

As the service evolves, this document should be expanded to include:
- provider-specific adapter sections
- webhook signature verification flow
- idempotency model and storage design
- retry/job execution architecture
- .NET callback contract details
- configuration matrix by environment
- sequence diagrams for subscription lifecycle events
