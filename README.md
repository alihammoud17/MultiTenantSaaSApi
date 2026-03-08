Multi-Tenant SaaS Api


## Local Development Setup
===

# 

# \### Prerequisites

# \- .NET 8 SDK

# \- PostgreSQL 16

# \- Redis 7

# 

# \### Configuration

# 

# This project uses User Secrets for local development. \*\*Do not commit sensitive data to Git.\*\*

# 

# \#### Quick Setup

# 

# \*\*Mac/Linux:\*\*

# ```bash

# chmod +x setup-secrets.sh

# ./setup-secrets.sh

# ```

# 

# \*\*Windows:\*\*

# ```powershell

# .\\setup-secrets.ps1

# ```

# 

# \#### Manual Setup

# ```bash

# dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=saasapi;Username=postgres;Password=YOUR\_PASSWORD"

# dotnet user-secrets set "Redis:ConnectionString" "localhost:6379"

# dotnet user-secrets set "Jwt:Secret" "YOUR\_256\_BIT\_SECRET"

# dotnet user-secrets set "Jwt:Issuer" "MultiTenantSaasApi"

# dotnet user-secrets set "Jwt:Audience" "MultiTenantSaasApi"

# dotnet user-secrets set "Jwt:ExpirationMinutes" "60"

# ```

# 

# \#### Verify Configuration

# ```bash

# dotnet user-secrets list

# ```

# 

# \### Running the Application

# ```bash

# dotnet run

# ```

# 

# The API will be available at `https://localhost:5001`

