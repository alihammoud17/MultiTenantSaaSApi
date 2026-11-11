# syntax=docker/dockerfile:1

FROM node:22-bookworm-slim AS build
WORKDIR /app

# Copy package manifests first to maximize layer cache reuse.
COPY BillingService/package.json BillingService/package-lock.json ./

RUN npm ci

# Copy BillingService sources and build TypeScript output.
COPY BillingService/ ./
RUN npm run build

FROM node:22-bookworm-slim AS runtime
WORKDIR /app

ENV NODE_ENV=production

# Copy package manifests for a production-only dependency install.
COPY BillingService/package.json BillingService/package-lock.json ./

RUN npm ci --omit=dev

# Fallback note: if package-lock.json is not present in a future revision,
# switch the install commands in both stages to `npm install` because `npm ci`
# requires an existing lock file.

COPY --from=build /app/dist ./dist

EXPOSE 3001

CMD ["npm", "run", "start"]
