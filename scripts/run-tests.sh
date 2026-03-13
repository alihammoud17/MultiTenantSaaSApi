#!/usr/bin/env bash
set -euo pipefail

echo "[1/3] Restoring dependencies..."
dotnet restore MultiTenantSaaSApi.sln

echo "[2/3] Building solution..."
dotnet build MultiTenantSaaSApi.sln --configuration Release --no-restore

echo "[3/3] Running tests..."
dotnet test MultiTenantSaaSApi.sln --configuration Release --no-build --verbosity normal
