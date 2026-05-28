# ADR-0017: Blob Storage + CDN — Azurite + nginx (local) / Azure Blob Storage + Front Door (production)

## Status

Accepted (2026-04-19, revised 2026-04-19 to use Azurite + Azure Blob Storage SDK instead of MinIO + S3 SDK after license-posture audit and Aspire-integration evaluation)

## Context

The Invoicing BC ([ADR-0018](0018-invoice-numbering.md), [invoicing.md](../bc-design/invoicing.md)) generates PDF invoices that must be:

- Stored durably in a write-once fashion (legal retention of fiscal records).
- Retrievable by buyers via time-limited authenticated URLs.
- Served with CDN-like caching semantics (respect `Cache-Control`, allow edge cache).
- Runnable on a developer laptop via either **Aspire AppHost** OR **`docker-compose --profile full up -d`** with zero external dependencies.

Production systems deploy to cloud object storage fronted by a CDN. The reference solution must teach this pattern without requiring AWS/Azure/GCP credentials or network access for local development.

The original chunk-3 ADR-0017 picked MinIO + AWS S3 SDK. Two issues surfaced on review:

1. **MinIO open-source posture eroded in 2024** — features stripped from the OSS build, license shifted to AGPLv3, community sentiment soured. Picking it for a *new* reference solution in 2026 is hard to defend.
2. **No first-class Aspire integration** — MinIO is a generic container in Aspire AppHost (`builder.AddContainer(...)` with manual env var wiring). Azurite has the first-party `builder.AddAzureStorage("storage").RunAsEmulator()` integration that swaps emulator ↔ real Azure Blob with one config flag.

This ADR replaces the MinIO choice with Azurite + Azure Blob Storage SDK. The pattern (origin + CDN, presigned URLs, write-once, `IBlobStore` abstraction) is preserved.

## Decision Drivers (ranked)

1. **First-party Aspire integration** — ergonomic emulator-vs-cloud swap; typed connection-string injection.
2. **Works in raw docker-compose** — contributors who don't run Aspire still get the local experience.
3. **Production migration is a config swap** — local SDK and call sites must be unchanged when migrating to cloud.
4. **License clarity** — long-term-safe license posture; no "is the open-source build still maintained?" questions in 2026.
5. **Presigned URL support + write-once semantics** — invoices are buyer-private; fiscal artifacts must not be overwritable.

## Considered Options

### Option 1: Azurite (local origin) + nginx-cdn (local CDN), Azure.Storage.Blobs SDK, abstracted via `IBlobStore`

Azurite runs in `docker-compose` (`mcr.microsoft.com/azure-storage/azurite`) AND via Aspire's first-party `builder.AddAzureStorage("storage").RunAsEmulator()`. Application uses `Azure.Storage.Blobs` against an `IBlobStore` abstraction in `Invoicing.Infrastructure`. Production swaps the connection string to a real Azure Blob Storage account; Aspire AppHost does the same swap automatically when `Production` environment is detected. nginx-cdn fronts the blob endpoint locally to demonstrate edge-cache semantics; production uses Azure Front Door / CDN.

### Option 2: LocalStack (local AWS emulator) + nginx-cdn, AWS S3 SDK

LocalStack emulates S3 (and many other AWS services); Aspire community integration via `CommunityToolkit.Aspire.Hosting.LocalStack`. Production target is AWS S3.

### Option 3: MinIO + nginx-cdn, AWS S3 SDK *(superseded — original chunk-3 decision)*

S3-compatible object store running in `docker-compose`. No first-party Aspire integration. License/community posture eroded in 2024.

### Option 4: SeaweedFS + nginx-cdn, AWS S3 SDK

Apache 2.0, S3-compatible, single Go binary. No first-party Aspire integration.

### Option 5: Local filesystem + ASP.NET static file server

Store PDFs on a mounted volume; serve via static files. Simplest infrastructure.

## Evaluation Matrix

| Driver (ranked) | Option 1: Azurite + Azure SDK | Option 2: LocalStack + S3 SDK | Option 3: MinIO + S3 SDK | Option 4: SeaweedFS | Option 5: Filesystem |
|---|---|---|---|---|---|
| 1. First-party Aspire integration | Yes — `AddAzureStorage().RunAsEmulator()` | Community integration only | Generic container only | Generic container only | N/A |
| 2. Works in docker-compose | Official Microsoft image | Official LocalStack image | Official MinIO image | Official SeaweedFS image | Trivial |
| 3. Production swap | One config flag — Aspire handles emulator↔cloud automatically | Same SDK; swap endpoint to AWS | Same SDK; swap endpoint to AWS S3 / on-prem MinIO | Same SDK; swap endpoint | Re-architect needed |
| 4. License clarity | MIT (Azurite) + Microsoft-maintained | Apache 2.0 community edition | AGPLv3 + eroded OSS posture | Apache 2.0, less-known | N/A |
| 5. Presigned URL + write-once | SAS tokens + immutable blob policies (Azure native) | S3 presigned URLs + Object Lock | S3 presigned URLs + bucket versioning | S3 presigned URLs | Custom token flow needed |

## Decision

We will use **Option 1: Azurite (local origin) + nginx-cdn (local CDN front), Azure.Storage.Blobs SDK, with first-party Aspire integration** (`builder.AddAzureStorage("storage").RunAsEmulator()` for AppHost; `azurite` container in `docker-compose.yaml` for non-Aspire flows). Production target: Azure Blob Storage + Azure Front Door.

Containers (Azure Blob terminology for "buckets"): `invoices` (private, SAS-token GET, immutable until retention expires); reserved future containers: `product-images`, `payment-receipts`.

## Rationale

The tightest "Aspire AND docker-compose" story in the .NET ecosystem in 2026 is Azurite. It is Microsoft-maintained, MIT-licensed, has first-party Aspire integration that swaps emulator ↔ real cloud with one flag, and ships an official Docker image for non-Aspire flows. The Azure Blob SDK (`Azure.Storage.Blobs`) is the same SDK readers will encounter at any .NET shop deploying to Azure. Picking Azurite optimizes for the .NET-on-Azure path that the reference solution's audience most plausibly walks.

Option 2 (LocalStack) is the right choice for AWS-bound deployments and stays competitive — but the Aspire integration is community-maintained rather than first-party, which trades teaching-clarity for ecosystem flexibility. For an Azure-bound reference, Option 1 wins.

Options 3 (MinIO) and 4 (SeaweedFS) require generic-container wiring in Aspire — workable, but loses the typed-API ergonomics of `AddAzureStorage`. Combined with MinIO's license erosion, neither is the obvious 2026 choice.

The pattern (origin + CDN, presigned URLs, write-once, `IBlobStore` abstraction) is unchanged from the superseded MinIO version — the swap is purely the local origin + SDK choice.

## Consequences

### Positive

- Aspire AppHost: one-line registration `builder.AddAzureStorage("storage").RunAsEmulator()` provisions Azurite locally; production swaps to a real account automatically based on environment.
- docker-compose: `azurite` container with the official Microsoft image — runs identically.
- `IBlobStore` abstraction in `Invoicing.Infrastructure` keeps SDK choice swappable; if Azure ever proves wrong, the abstraction is one-day-of-work to replace.
- SAS tokens demonstrate the buyer-private file-access pattern with first-party Azure semantics; production migration to Azure Blob is configuration-only.
- nginx-cdn demonstrates local edge-cache semantics — production uses Azure Front Door / Azure CDN with the same `Cache-Control` honoring.
- Immutable blob policies enforce the fiscal-record write-once requirement at the Azure level, both locally (Azurite supports it) and in production.
- Microsoft-maintained: the "is this still maintained in 5 years?" question has a confident answer.

### Negative

- Azure-specific SDK — production migration to AWS S3 / GCS / on-prem would require an `IBlobStore` adapter swap (~1 day). Acceptable given the .NET-on-Azure target.
- Azurite has historically lagged behind real Azure Blob features on a few edges (some advanced lifecycle-policy semantics). For our v1 use cases (write-once invoices + SAS reads), feature parity is complete.
- One more SDK (`Azure.Storage.Blobs`) in the Invoicing dependency tree. ~5 MB; acceptable.

### Risks

- **SAS token leakage** — a SAS URL shared by a buyer grants time-bounded access to anyone with the link. Mitigation: 10-minute expiry on invoice GET; production audit logs via Azure Storage diagnostic logs.
- **nginx cache poisoning** — if nginx caches a response for the wrong user. Mitigation: SAS URLs include a signed query string unique per request; nginx cache key is the full URL; no cross-user pollution possible.
- **Azurite container accidentally deleted in dev** — `docker compose down -v` wipes volumes, invoices gone. Acceptable in dev; production uses Azure Storage GRS / RA-GRS durability.
- **PII on the wire** — SAS URLs to invoice PDFs are over HTTP in local dev. Mitigation: production uses HTTPS via Azure Front Door; referenced in [ADR-0011](0011-pii-handling-gdpr.md).

## Implementation Notes

### Aspire AppHost integration

In the AppHost project (whenever Aspire support lands per Wave 0):

```csharp
var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(emulator => emulator
        .WithDataVolume("azurite-data")           // persistent across restarts
        .WithImageTag("3.31.0")                   // pin emulator version
    );

var invoicesContainer = storage.AddBlobs("invoices");

var invoicing = builder.AddProject<Projects.Invoicing_Api>("invoicing-api")
    .WithReference(invoicesContainer);            // injects connection string as env var
```

In production environment, `RunAsEmulator()` is bypassed and Aspire wires the real Azure Storage account via Bicep / azd.

### `docker-compose.yaml` integration (for non-Aspire flows)

```yaml
services:
  azurite:
    image: mcr.microsoft.com/azure-storage/azurite:3.31.0
    command: ["azurite-blob", "--blobHost", "0.0.0.0", "--location", "/data", "--debug", "/data/debug.log"]
    ports: ["10000:10000"]                        # blob service port
    volumes: ["azurite-data:/data"]
    profiles: ["core", "full"]

  azurite-init:
    image: mcr.microsoft.com/azure-cli:latest
    depends_on: [azurite]
    environment:
      AZURE_STORAGE_CONNECTION_STRING: "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://azurite:10000/devstoreaccount1;"
    entrypoint: |
      /bin/sh -c "
      az storage container create --name invoices --public-access off;
      az storage container immutability-policy create --container-name invoices --period 3650 --resource-group dev --account-name devstoreaccount1 || true;
      exit 0;
      "
    profiles: ["core", "full"]

  nginx-cdn:
    image: nginx:1.27-alpine
    ports: ["8080:8080"]
    volumes: ["./src/nginx-cdn/nginx.conf:/etc/nginx/nginx.conf:ro"]
    depends_on: [azurite]
    profiles: ["full"]

volumes:
  azurite-data:
```

The `devstoreaccount1` account name + key is the Azurite well-known dev account — same constants Microsoft tooling expects. Production swaps to a real account name + managed identity.

### nginx-cdn config

`src/nginx-cdn/nginx.conf` proxies to Azurite with `proxy_cache`:

```nginx
proxy_cache_path /var/cache/nginx levels=1:2 keys_zone=blob_cache:10m max_size=1g inactive=1h use_temp_path=off;

server {
  listen 8080;

  location / {
    proxy_pass http://azurite:10000;
    proxy_cache blob_cache;
    proxy_cache_valid 200 1h;
    proxy_cache_use_stale error timeout invalid_header updating http_500 http_502 http_503 http_504;
    proxy_cache_key "$scheme$request_method$host$request_uri";
    proxy_ignore_headers X-Accel-Expires;
    add_header X-Cache-Status $upstream_cache_status;
  }
}
```

### `IBlobStore` abstraction (in `Invoicing.Infrastructure` for v1)

For v1, the abstraction lives in `Invoicing.Infrastructure`. Extracted to `Platform.BlobStorage.AzureBlobs` only when a 2nd consumer (e.g., Catalog product images) emerges:

```csharp
public interface IBlobStore
{
    Task<PdfBlobRef> UploadAsync(string containerName, string blobName, ReadOnlyMemory<byte> content, string contentType, IReadOnlyDictionary<string, string>? metadata, TimeSpan sasTtl, CancellationToken ct);
    Task<Uri> GetSasUrlAsync(string containerName, string blobName, TimeSpan expiry, CancellationToken ct);
    Task<Stream> DownloadAsync(string containerName, string blobName, CancellationToken ct);
}
```

> **Self-correction:** The upload payload type is `ReadOnlyMemory<byte>` rather than the original `Stream`. Rationale: invoice PDFs are ~30 KB per ADR-0019 § Performance, and the adapter must compute a SHA-256 digest of the payload before handing it to `BlobClient.UploadAsync`. Buffering once at the boundary (caller passes an already-materialized byte array / `Memory<byte>`) eliminates a `CryptoStream` indirection and makes the digest deterministic without double-reading. The `sasTtl` parameter was added so callers pick the presigned-URL lifetime explicitly (10 min for buyer-facing reads per this ADR; admin bulk export uses 1 hour).

Adapter uses `Azure.Storage.Blobs` (`BlobServiceClient`, `BlobContainerClient`, `BlobClient`) bound to the connection string injected by Aspire (in AppHost mode) or read from `appsettings.json:ConnectionStrings:AzureStorage` (in raw docker-compose mode).

### Container conventions

| Container | Purpose | Retention | Access |
|---|---|---|---|
| `invoices` | PDF invoices + credit notes | 10-year immutable blob policy | Private; SAS-URL required |
| `product-images` (v2) | Product catalog images | None | Public anonymous (or SAS in production for paid catalogs) |
| `payment-receipts` (v2) | Gateway-returned transaction receipts | 7 years | Private |

### SAS URL contract (Invoicing)

- Invoice GET: 10-minute SAS expiry, signed by the Invoicing service identity (managed identity in production)
- Admin bulk export: 1-hour SAS expiry, signed by the admin-service token
- SAS URLs include `rscd=attachment; filename=INV-YYYY-NNNNNN.pdf` (response-content-disposition) so browsers download with the right filename

### Production migration

- Azurite endpoint → real Azure Blob Storage account endpoint
- Connection string → managed-identity authentication (Aspire's `AddAzureStorage` already supports this; no code change)
- nginx-cdn → Azure Front Door / Azure CDN routing + caching
- Container-immutability policies migrate from Azurite (limited support) to full Azure Storage Immutable Blob policies (locked time-based retention)

### Observability

- `blob.upload.duration.seconds` (histogram)
- `blob.download.duration.seconds`
- `blob.sas_url.issued.count`
- nginx-cdn access logs with `$upstream_cache_status` (HIT / MISS / BYPASS) for edge-cache analysis

## Related Decisions

- [ADR-0009: Reference-Solution Target Profile](0009-reference-solution-target-profile.md) — single-AZ, best-effort DR; Azurite is not a DR solution
- [ADR-0011: PII Handling & GDPR](0011-pii-handling-gdpr.md) — v1 PDFs are plaintext; v2 would add SSE-CMK encryption (Azure Storage native customer-managed keys)
- [ADR-0018: Invoice Numbering](0018-invoice-numbering.md) — invoice PDFs are the primary consumer of this infrastructure
- [ADR-0019: PDF Generation (QuestPDF)](0019-pdf-generation-questpdf.md) — produces the content this ADR stores
