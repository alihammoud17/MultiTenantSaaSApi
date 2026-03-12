# Multi-Tenant SaaS API

A starter ASP.NET Core 8 Web API for multi-tenant SaaS authentication.

## Current Implementation

- Tenant-aware domain entities (`Tenant`, `User`, `Subscription`, `Plan`, `AuditLog`).
- Registration endpoint that creates:
  - a tenant,
  - an admin user,
  - and a default free subscription.
- Login endpoint with BCrypt password verification.
- JWT token generation with issuer/audience/secret validation.
- Request-scoped tenant context middleware.
- Health checks endpoint (`/health`).
- Plan catalog + tenant plan upgrade endpoint (`/api/plans`, `/api/plans/upgrade`).
- PostgreSQL persistence via Entity Framework Core (Npgsql provider).
- Redis connection registration for shared caching/session scenarios.

## Project Structure

- `Presentation/` – API host, middleware, and controllers.
- `Application/` – application-level services (e.g., JWT service).
- `Domain/` – entities, DTOs, responses, and interfaces.
- `Infrastructure/` – data access and tenant context implementations.

## Prerequisites

- .NET 8 SDK
- PostgreSQL 16
- Redis 7

## Local Development Setup

This project uses User Secrets for local development. **Do not commit sensitive data to Git.**

### Manual Setup

Run from `Presentation/`:

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=saasapi;Username=postgres;Password=YOUR_PASSWORD"
dotnet user-secrets set "Redis:ConnectionString" "localhost:6379"
dotnet user-secrets set "Jwt:Secret" "YOUR_256_BIT_SECRET"
dotnet user-secrets set "Jwt:Issuer" "MultiTenantSaasApi"
dotnet user-secrets set "Jwt:Audience" "MultiTenantSaasApi"
dotnet user-secrets set "Jwt:ExpirationMinutes" "60"
```

### Verify Configuration

```bash
dotnet user-secrets list
```

## Run the API

```bash
dotnet run --project Presentation
```

Swagger UI is available in development mode.
