# syntax=docker/dockerfile:1

# Build stage: compile TypeScript into dist/
FROM node:22-bookworm-slim AS build
WORKDIR /app

# Copy dependency manifests first for better layer caching
COPY BillingService/package.json BillingService/package-lock.json* ./

# BillingService includes a lock file, so use deterministic installs.
RUN if [ -f package-lock.json ]; then npm ci; else \
      # Fallback for repositories without a lock file (non-deterministic dependency resolution).
      npm install; \
    fi

# Copy service source and build output
COPY BillingService/ ./
RUN npm run build

# Runtime stage: production dependencies + compiled output only
FROM node:22-bookworm-slim AS runtime
WORKDIR /app
ENV NODE_ENV=production

COPY BillingService/package.json BillingService/package-lock.json* ./
RUN if [ -f package-lock.json ]; then npm ci --omit=dev; else \
      # Fallback for repositories without a lock file (non-deterministic dependency resolution).
      npm install --omit=dev; \
    fi

COPY --from=build /app/dist ./dist

EXPOSE 3001
CMD ["npm", "run", "start"]
