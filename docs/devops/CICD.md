<div align="center">

# 🔄 CI/CD

</div>

| ⚡ TL;DR |
| -------- |
| DotNetAtlas includes GitHub Actions workflows for build, test, and deploy. The pipeline runs unit tests, integration tests (with TestContainers), architecture tests, builds Docker images, and deploys to environments. |

Continuous Integration and Continuous Deployment automate the path from code to production. DotNetAtlas demonstrates a complete CI/CD pipeline using GitHub Actions.

## 🏗️ Pipeline Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    CI/CD Pipeline                            │
│                                                              │
│  ┌─────────┐    ┌─────────┐    ┌─────────┐    ┌─────────┐  │
│  │  Build  │───►│  Test   │───►│  Scan   │───►│ Publish │  │
│  └─────────┘    └─────────┘    └─────────┘    └─────────┘  │
│       │              │              │              │        │
│       ▼              ▼              ▼              ▼        │
│  ┌─────────┐    ┌─────────┐    ┌─────────┐    ┌─────────┐  │
│  │ Restore │    │  Unit   │    │Security │    │ Docker  │  │
│  │ Compile │    │  Integ  │    │  SAST   │    │  Push   │  │
│  │         │    │  Arch   │    │         │    │         │  │
│  └─────────┘    └─────────┘    └─────────┘    └─────────┘  │
│                                                              │
│                         │                                    │
│                         ▼                                    │
│  ┌─────────────────────────────────────────────────────────┐│
│  │                    Deploy                               ││
│  │  ┌─────────┐    ┌─────────┐    ┌─────────┐            ││
│  │  │   Dev   │───►│ Staging │───►│  Prod   │            ││
│  │  └─────────┘    └─────────┘    └─────────┘            ││
│  └─────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────┘
```

## 📦 GitHub Actions Workflow

### Main CI Workflow

```yaml
# .github/workflows/ci.yml
name: CI

on:
  push:
    branches: [main, develop]
  pull_request:
    branches: [main]

env:
  DOTNET_VERSION: '9.0.x'
  REGISTRY: ghcr.io
  IMAGE_NAME: ${{ github.repository }}

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}
      
      - name: Restore dependencies
        run: dotnet restore
      
      - name: Build
        run: dotnet build --no-restore --configuration Release
      
      - name: Upload build artifacts
        uses: actions/upload-artifact@v4
        with:
          name: build
          path: |
            **/bin/Release
            **/obj/Release

  test-unit:
    needs: build
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}
      
      - name: Run unit tests
        run: |
          dotnet test tests/DotNetAtlas.Domain.UnitTests \
            --configuration Release \
            --logger "trx;LogFileName=unit-tests.trx" \
            --collect:"XPlat Code Coverage"
      
      - name: Upload test results
        uses: actions/upload-artifact@v4
        if: always()
        with:
          name: unit-test-results
          path: "**/TestResults/**"

  test-integration:
    needs: build
    runs-on: ubuntu-latest
    services:
      docker:
        image: docker:dind
        options: --privileged
    steps:
      - uses: actions/checkout@v4
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}
      
      - name: Run integration tests
        run: |
          dotnet test tests/DotNetAtlas.Api.IntegrationTests \
            --configuration Release \
            --logger "trx;LogFileName=integration-tests.trx"
      
      - name: Upload test results
        uses: actions/upload-artifact@v4
        if: always()
        with:
          name: integration-test-results
          path: "**/TestResults/**"

  test-architecture:
    needs: build
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}
      
      - name: Run architecture tests
        run: |
          dotnet test tests/DotNetAtlas.ArchitectureTests \
            --configuration Release

  security-scan:
    needs: build
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      
      - name: Run security scan
        uses: github/codeql-action/analyze@v3
        with:
          languages: csharp

  publish:
    needs: [test-unit, test-integration, test-architecture, security-scan]
    runs-on: ubuntu-latest
    if: github.ref == 'refs/heads/main'
    permissions:
      contents: read
      packages: write
    steps:
      - uses: actions/checkout@v4
      
      - name: Log in to Container Registry
        uses: docker/login-action@v3
        with:
          registry: ${{ env.REGISTRY }}
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}
      
      - name: Extract metadata
        id: meta
        uses: docker/metadata-action@v5
        with:
          images: ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}
          tags: |
            type=sha,prefix=
            type=ref,event=branch
            latest
      
      - name: Build and push Docker image
        uses: docker/build-push-action@v5
        with:
          context: .
          file: src/DotNetAtlas.Api/Dockerfile
          push: true
          tags: ${{ steps.meta.outputs.tags }}
          labels: ${{ steps.meta.outputs.labels }}
```

### Deployment Workflow

```yaml
# .github/workflows/deploy.yml
name: Deploy

on:
  workflow_run:
    workflows: [CI]
    types: [completed]
    branches: [main]

jobs:
  deploy-staging:
    if: ${{ github.event.workflow_run.conclusion == 'success' }}
    runs-on: ubuntu-latest
    environment: staging
    steps:
      - name: Deploy to staging
        run: |
          # Deploy using your preferred method
          # kubectl, helm, terraform, etc.
          echo "Deploying to staging..."

  deploy-production:
    needs: deploy-staging
    runs-on: ubuntu-latest
    environment: production
    steps:
      - name: Deploy to production
        run: |
          echo "Deploying to production..."
```

## 📊 Code Coverage

```yaml
- name: Upload coverage to Codecov
  uses: codecov/codecov-action@v4
  with:
    files: "**/coverage.cobertura.xml"
    fail_ci_if_error: true
```

## 🔐 Secrets Management

| Secret | Purpose |
|--------|---------|
| `GITHUB_TOKEN` | Container registry auth (automatic) |
| `CODECOV_TOKEN` | Code coverage upload |
| `AZURE_CREDENTIALS` | Azure deployment |
| `KUBE_CONFIG` | Kubernetes deployment |

## 🏷️ Branch Strategy

```
main ─────────────────────────────────────────► Production
  │
  └── develop ────────────────────────────────► Staging
        │
        ├── feature/xyz ──────► PR ──► develop
        │
        └── bugfix/abc ───────► PR ──► develop
```

## 📖 Further Reading

- [**Docker**](Docker.md) - Container configuration
- [**Testing Overview**](../testing/Overview.md) - Test strategy
- [GitHub Actions Documentation](https://docs.github.com/actions)

