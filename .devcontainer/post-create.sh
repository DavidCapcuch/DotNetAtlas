#!/bin/bash
set -e

echo "Starting DotNetAtlas development environment setup..."
echo ""

# Restore .NET tools from .config/dotnet-tools.json
# (includes dotnet-ef, avrogen, reportgenerator, dotnet-stryker — see manifest).
echo "📦 Restoring .NET tools..."
dotnet tool restore

# Restore NuGet packages
echo "📦 Restoring NuGet packages..."
dotnet restore

# Generate development HTTPS certificate
echo "🔐 Setting up HTTPS development certificate..."
dotnet dev-certs https --trust 2>/dev/null || echo "   HTTPS certificate trust not supported in this environment"

# Build the solution to verify everything is set up correctly
# Note: OpenAPI generation is disabled because infrastructure services (Postgres, Kafka, etc.)
# are not running yet during postCreateCommand. They start in postAttachCommand.
# -m enables parallel builds across all available CPU cores (not enabled by default)
echo "🔨 Building solution..."
dotnet build --no-restore -p:OpenApiGenerateDocuments=false -m

echo ""
echo "✅ Build complete! Infrastructure services will start next..."
echo ""

