#!/bin/bash
set -e

echo "Starting DotNetAtlas development environment setup..."
echo ""

# Restore .NET tools (if any defined in .config/dotnet-tools.json)
echo "📦 Restoring .NET tools..."
dotnet tool restore 2>/dev/null || echo "   No .NET tools manifest found, skipping..."

# Restore NuGet packages
echo "📦 Restoring NuGet packages..."
dotnet restore

# Install Entity Framework Core tools globally if not present
if ! dotnet ef --version &>/dev/null; then
    echo "🔧 Installing Entity Framework Core tools..."
    dotnet tool install --global dotnet-ef
fi

# Generate development HTTPS certificate
echo "🔐 Setting up HTTPS development certificate..."
dotnet dev-certs https --trust 2>/dev/null || echo "   HTTPS certificate trust not supported in this environment"

# Build the solution to verify everything is set up correctly
# Note: OpenAPI generation is disabled because infrastructure services (SQL Server, etc.)
# are not running yet during postCreateCommand. They start in postAttachCommand.
# -m enables parallel builds across all available CPU cores (not enabled by default)
echo "🔨 Building solution..."
dotnet build --no-restore -p:OpenApiGenerateDocuments=false -m

echo ""
echo "✅ Build complete! Infrastructure services will start next..."
echo ""

