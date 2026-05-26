# Invoice delivery flow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship end-to-end invoice email delivery (Notifications BC delivery consumer + `InvoiceDeliveredEvent.avsc` + outbox publisher + `DeliveryChannel.Email` flip) on top of a clean immutable-`BlobName` blob reference, in one branch (`aaqwdqwd`). Closes [#123](https://github.com/DavidCapcuch/DotNetAtlas/issues/123) + [#131](https://github.com/DavidCapcuch/DotNetAtlas/issues/131).

**Architecture:** `IssueInvoice` raises in-process `InvoiceDeliveryRequestedDomainEvent`; Invoicing outbox publisher produces a generic `SendEmailNotificationCommand` (with a non-credential `ViewInvoiceUrl` built from `BuyerPortalOptions.BaseUrl`) on `notifications.email-commands`. Notifications consumes the command, renders via `IEmailTemplateRenderer`, sends via `IEmailGateway` (mock in Phase 1), and emits a generic `EmailNotificationSentEvent` on `notifications.email-events`. Invoicing's reciprocal consumer filters by `TemplateId` prefix `"invoicing."`, calls `Invoice.Deliver(now)`, and the new `InvoiceDeliveredOutboxPublisher` fans `InvoiceDeliveredEvent.avsc` to `invoicing.invoices`. SAS URLs never appear in any Kafka message or email body — the existing `GET /api/v1/invoices/{id}` endpoint remains the only SAS-minting code path.

**Tech Stack:** .NET 10, C#, EF Core (PostgreSQL), KafkaFlow + Confluent Schema Registry (FORWARD_TRANSITIVE for event topics; FULL_TRANSITIVE for command topics — see ADR-0007; breaking field rename on `InvoiceIssuedEvent.avsc` accepted per non-production reference-repo deviation), Mapperly, FluentResults, xUnit + AwesomeAssertions + NSubstitute, Testcontainers, Azurite for blob tests.

**Spec:** [`docs/superpowers/specs/2026-05-22-invoice-delivery-flow-design.md`](../specs/2026-05-22-invoice-delivery-flow-design.md)

---

## Pre-flight notes for the implementer

- **All test runs**: prefix with `unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && ` per CLAUDE.md (Testcontainers + corporate proxy workaround).
- **Never auto-generate EF Core migrations** (CLAUDE.md). Phase A includes a USER-CONTROLLED step where you stop and ask the user to run `dotnet ef migrations add ...`.
- **`dotnet build -m` solution-wide** after each commit per CLAUDE.md (Platform.SharedKernel touch is *not* expected, but the gate is cheap).
- **Format gates before each commit**: `dotnet format whitespace --no-restore --verify-no-changes` AND `dotnet format style --no-restore --verify-no-changes`.
- **Avro C# regen toolchain**: this repo regenerates `.cs` bindings via a `dotnet avrogen` MSBuild target on the `Platform.SchemaRegistry.Contracts` project (verify the exact command by reading `platform/Platform.SchemaRegistry.Contracts/Platform.SchemaRegistry.Contracts.csproj` at execution time). The typical invocation is `dotnet build platform/Platform.SchemaRegistry.Contracts/ -t:GenerateAvroClasses` or similar; if regen is purely on `dotnet build`, no extra step is needed.
- **Package additions**: per CLAUDE.md, place version pins in the closest applicable `Directory.Packages.props` (root / services / saga / platform / test).
- **Phase ordering is mandatory.** A → B → C → D → E. Phase A landing is a prerequisite for Phase C tests to compile (PdfBlobRef refactor).

---

## File Structure

### New files
| Path | Responsibility |
|---|---|
| `platform/Platform.SchemaRegistry.Contracts/Avro/Invoicing/Invoices/InvoiceDeliveredEvent.avsc` | External Avro event on `invoicing.invoices` (closes #123) |
| `platform/Platform.SchemaRegistry.Contracts/Avro/Notifications/Email/EmailNotificationSentEvent.avsc` | Generic Notifications "send-succeeded" event |
| `services/Invoicing/Invoicing.Application/Common/Notifications/BuyerPortalOptions.cs` | `BuyerPortal:BaseUrl` config for the non-credential email link |
| `services/Invoicing/Invoicing.Application/Outbox/InvoiceDeliveryRequestedOutboxPublisherDomainEventHandler.cs` | Builds `SendEmailNotificationCommand` outbox row |
| `services/Invoicing/Invoicing.Application/Outbox/InvoiceDeliveredOutboxPublisherDomainEventHandler.cs` | Fans `InvoiceDeliveredDomainEvent` to Avro outbox |
| `services/Invoicing/Invoicing.Application/Outbox/InvoiceDeliveredMapper.cs` | Mapperly extension for `ToInvoiceDeliveredEvent` |
| `services/Invoicing/Invoicing.Application/Messaging/EmailNotificationSentEventKafkaHandler.cs` | Invoicing's reciprocal consumer; calls `Invoice.Deliver` |
| `services/Notifications/Notifications/Email/IEmailGateway.cs` | Abstraction for email send |
| `services/Notifications/Notifications/Email/MockEmailGateway.cs` | Logs + returns success |
| `services/Notifications/Notifications/Email/IEmailTemplateRenderer.cs` | Abstraction for template rendering |
| `services/Notifications/Notifications/Email/EmailTemplateRenderer.cs` | In-process hardcoded template lookup |
| `services/Notifications/Notifications/Email/EmailMessage.cs` | Plain record carrying To/Subject/Body |
| `services/Notifications/Notifications/Notifications/SendEmailNotification/SendEmailNotificationCommandKafkaHandler.cs` | Consumes generic email-command, publishes generic email-event |
| `test/Notifications.UnitTests/Notifications.UnitTests.csproj` | New test project |
| `test/Notifications.UnitTests/Email/EmailTemplateRendererTests.cs` | Template renderer unit tests |
| `test/Notifications.UnitTests/Email/MockEmailGatewayTests.cs` | Mock gateway unit test |
| `test/Notifications.UnitTests/Notifications/SendEmailNotification/SendEmailNotificationCommandKafkaHandlerTests.cs` | Handler unit tests |
| `test/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj` | New integration test project |
| `test/Notifications.IntegrationTests/Common/IntegrationTestFixture.cs` | Testcontainers fixture |
| `test/Notifications.IntegrationTests/Messaging/Kafka/SendEmailNotificationCommandKafkaHandlerTests.cs` | E2E test for Notifications side |
| `test/Invoicing.UnitTests/Application/Invoices/Delivery/InvoiceDeliveryRequestedOutboxPublisherTests.cs` | Unit test for the new publisher |
| `test/Invoicing.UnitTests/Application/Invoices/Delivery/InvoiceDeliveredOutboxPublisherTests.cs` | Unit test mirroring `InvoiceIssuedOutboxPublisherTests` |
| `test/Invoicing.UnitTests/Application/Invoices/Delivery/InvoiceDeliveredMapperTests.cs` | Mapper test |
| `test/Invoicing.IntegrationTests/Messaging/Kafka/EmailNotificationSentEventKafkaHandlerTests.cs` | Reciprocal consumer integration test |
| `test/Invoicing.IntegrationTests/EndToEnd/InvoiceDeliveryFlowTests.cs` | End-to-end flow test |

### Modified files
| Path | Reason |
|---|---|
| `platform/Platform.SchemaRegistry.Contracts/Avro/Invoicing/Invoices/InvoiceIssuedEvent.avsc` | Rename `PdfBlobUri` → `PdfBlobName` (breaking; accepted) |
| Avro `.cs` bindings (regenerated) | `InvoiceIssuedEvent.cs`, `InvoiceDeliveredEvent.cs`, `EmailNotificationSentEvent.cs` |
| `services/Invoicing/Invoicing.Domain/Common/ValueObjects/PdfBlobRef.cs` | `BlobUri:Uri` → `BlobName:string` (positional record signature changes) |
| `services/Invoicing/Invoicing.Application/Outbox/InvoiceIssuedMapper.cs` | Map `PdfBlobName = source.PdfBlobRef.BlobName` |
| `services/Invoicing/Invoicing.Application/Invoices/IssueInvoice/IssueInvoiceCommandHandler.cs:159` | Flip `DeliveryChannel.None → Email` |
| `services/Invoicing/Invoicing.Application/Invoices/GetInvoiceById/GetInvoiceByIdQueryHandler.cs` (+ 3 sister query handlers) | Read `BlobName` from `PdfBlobRef` directly |
| `services/Invoicing/Invoicing.Application/Common/Messaging/TopicsOptions.cs` | Add `NotificationsEmailCommands`, `NotificationsEmailEvents` |
| `services/Invoicing/Invoicing.Application/Common/InfrastructureDependencyInjection.cs` (or wherever DI lives) | Register handlers + `BuyerPortalOptions` |
| `services/Invoicing/Invoicing.Infrastructure/Persistence/Database/EntityConfigurations/InvoiceConfiguration.cs` | `pdf_blob_uri` → `pdf_blob_name`, remove URI conversion |
| `services/Invoicing/Invoicing.Infrastructure/Persistence/Database/EntityConfigurations/CreditNoteConfiguration.cs` | Same |
| `services/Invoicing/Invoicing.Infrastructure/Blobs/AzureBlobStore.cs` | `PdfBlobRef.Create(blobName, ...)` |
| `services/Invoicing/Invoicing.Api/appsettings.json` (+ Development overrides) | `BuyerPortal`, `InvoicingTopics.NotificationsEmail*` |
| `services/Notifications/Notifications/Common/Config/TopicsOptions.cs` | Add `EmailCommands`, `EmailEvents` |
| `services/Notifications/Notifications/Common/MessagingDependencyInjection.cs` | Add `EmailCommands` consumer subscription + register `IEmailGateway`, `IEmailTemplateRenderer`, `SendEmailNotificationCommandKafkaHandler` |
| `services/Notifications/Notifications/appsettings.json` (+ Development) | `Topics.EmailCommands`, `Topics.EmailEvents` |
| `services/Invoicing/Invoicing.Application/Common/Messaging/*DependencyInjection*.cs` | Subscribe to `notifications.email-events` |
| `services/Invoicing/Invoicing.Infrastructure/Persistence/Database/Migrations/*` | USER GENERATES via `dotnet ef migrations add RenamePdfBlobUriToBlobName` |
| `test/Invoicing.UnitTests/Common/ValueObjects/PdfBlobRefTests.cs` | Update to BlobName |
| `test/Invoicing.UnitTests/Common/TestDataFactory.cs` | Update PdfBlobRef construction |
| `test/Invoicing.IntegrationTests/Application/IssueInvoiceCommandHandlerTests.cs` | Assert new outbox row + ViewInvoiceUrl |
| `test/Invoicing.FunctionalTests/Common/InvoiceSeed.cs` | Update PdfBlobRef construction if used |
| `Directory.Packages.props` (root) | Add KafkaFlow-test or Testcontainers packages if Notifications.* test projects need them |

### Tests-only files referenced
- `test/Inventory.UnitTests/Inventory.UnitTests.csproj` — copy structure for `Notifications.UnitTests.csproj`
- `test/Invoicing.IntegrationTests/Common/IntegrationTestFixture.cs` — pattern to mirror for Notifications.IntegrationTests

---

## Phase A — PdfBlobRef.BlobName refactor + new Avro contracts (foundation)

This phase lands the domain refactor and brings new Avro shapes into the build *without* wiring delivery flow yet. End-of-phase state: solution builds, all existing tests pass on the renamed domain VO + EF column + Avro field.

### Task A1: Add `InvoiceDeliveredEvent.avsc` (new — no compat conflict)

**Files:**
- Create: `platform/Platform.SchemaRegistry.Contracts/Avro/Invoicing/Invoices/InvoiceDeliveredEvent.avsc`

- [ ] **Step 1: Create the schema file**

```json
{
    "type": "record",
    "name": "InvoiceDeliveredEvent",
    "namespace": "Invoicing.Invoices",
    "doc": "Emitted when an Invoice transitions Issued -> Delivered (a downstream delivery channel reported success). Topic 'invoicing.invoices' has 10-year retention; consumers may need this to advance their own state machines (BFF cache, audit reports). FORWARD_TRANSITIVE compat.",
    "fields": [
        { "name": "InvoiceId",      "type": { "type": "string", "logicalType": "uuid" },             "doc": "Aggregate id." },
        { "name": "BuyerId",        "type": { "type": "string", "logicalType": "uuid" },             "doc": "Partition key; matches InvoiceIssuedEvent.BuyerId." },
        { "name": "DeliveredAtUtc", "type": { "type": "long",   "logicalType": "timestamp-millis" }, "doc": "UTC instant the channel reported success." },
        { "name": "Channel",        "type": "string",                                                "doc": "DeliveryChannel SmartEnum name: 'Email' (v1) or 'TaxAuthorityWebhook' (v2)." },
        { "name": "CorrelationId",  "type": { "type": "string", "logicalType": "uuid" },             "doc": "Checkout saga correlation id (passed through from Issuance)." },
        { "name": "OccurredOnUtc",  "type": { "type": "long",   "logicalType": "timestamp-millis" }, "doc": "Domain event occurrence time." }
    ]
}
```

- [ ] **Step 2: Trigger Avro C# regen**

Run:
```bash
dotnet build platform/Platform.SchemaRegistry.Contracts/Platform.SchemaRegistry.Contracts.csproj -m
```

Expected: build succeeds, `InvoiceDeliveredEvent.cs` appears next to the `.avsc`. If the project doesn't auto-regen, locate the `avrogen` target and run it manually; check `Platform.SchemaRegistry.Contracts.csproj` for the `<Target>` definition or a `BeforeBuild` hook.

- [ ] **Step 3: Commit**

```bash
git add platform/Platform.SchemaRegistry.Contracts/Avro/Invoicing/Invoices/InvoiceDeliveredEvent.avsc \
        platform/Platform.SchemaRegistry.Contracts/Avro/Invoicing/Invoices/InvoiceDeliveredEvent.cs
git commit -m "feat(invoicing-avro): add InvoiceDeliveredEvent schema + binding"
```

### Task A2: Add `EmailNotificationSentEvent.avsc` (new — no compat conflict)

**Files:**
- Create: `platform/Platform.SchemaRegistry.Contracts/Avro/Notifications/Email/EmailNotificationSentEvent.avsc`

- [ ] **Step 1: Create the schema file**

```json
{
    "type": "record",
    "name": "EmailNotificationSentEvent",
    "namespace": "Notifications.Email",
    "doc": "Emitted after IEmailGateway reports successful send for a SendEmailNotificationCommand. Generic — consumers route by TemplateId prefix.",
    "fields": [
        { "name": "UserId",         "type": { "type": "string", "logicalType": "uuid" },             "doc": "Recipient user id, copied from the originating command." },
        { "name": "TemplateId",     "type": "string",                                                "doc": "Template id from the originating command; consumers filter by prefix (e.g., 'invoicing.*')." },
        { "name": "IdempotencyKey", "type": "string",                                                "doc": "Copied from originating command. Carries the BC-specific correlation hint (e.g., 'invoice-delivered-{InvoiceId}-{Attempt}')." },
        { "name": "SentAtUtc",      "type": { "type": "long",   "logicalType": "timestamp-millis" }, "doc": "When IEmailGateway returned success." },
        { "name": "OccurredOnUtc",  "type": { "type": "long",   "logicalType": "timestamp-millis" }, "doc": "Domain event occurrence time." }
    ]
}
```

- [ ] **Step 2: Trigger Avro regen + commit**

```bash
dotnet build platform/Platform.SchemaRegistry.Contracts/Platform.SchemaRegistry.Contracts.csproj -m
git add platform/Platform.SchemaRegistry.Contracts/Avro/Notifications/Email/EmailNotificationSentEvent.avsc \
        platform/Platform.SchemaRegistry.Contracts/Avro/Notifications/Email/EmailNotificationSentEvent.cs
git commit -m "feat(notifications-avro): add EmailNotificationSentEvent schema + binding"
```

### Task A3: Refactor `PdfBlobRef` — `BlobUri:Uri` → `BlobName:string` (with TDD)

**Files:**
- Modify: `services/Invoicing/Invoicing.Domain/Common/ValueObjects/PdfBlobRef.cs`
- Modify: `test/Invoicing.UnitTests/Common/ValueObjects/PdfBlobRefTests.cs`
- Modify: `test/Invoicing.UnitTests/Common/TestDataFactory.cs` (rebuild constructions to use BlobName)

- [ ] **Step 1: Update PdfBlobRefTests to drive the new shape (RED)**

Replace all `Uri`-based tests with `string`-based BlobName tests. Cover:
- Valid path-style name like `"2026/05/INV-2026-000142.pdf"` → `Result.Ok`.
- `null` / empty / whitespace BlobName → `Result.Fail` with code `"Invoicing.InvalidBlobName"`.
- BlobName not ending in `.pdf` → `Result.Fail` with code `"Invoicing.InvalidBlobName"`.
- BlobName with leading `/` → `Result.Fail` (relative path required).
- Invalid hash / size cases unchanged.

Example test:
```csharp
[Fact]
public void Create_WithValidInputs_ReturnsOk()
{
    var result = PdfBlobRef.Create(
        blobName: "2026/05/INV-2026-000142.pdf",
        contentHash: new string('a', 64),
        sizeBytes: 12345L);

    result.IsSuccess.Should().BeTrue();
    result.Value.BlobName.Should().Be("2026/05/INV-2026-000142.pdf");
}

[Theory]
[InlineData(null)]
[InlineData("")]
[InlineData("   ")]
[InlineData("/leading-slash/INV.pdf")]
[InlineData("INV-2026-000142.txt")]
public void Create_WithInvalidBlobName_FailsWithInvalidBlobName(string? blobName)
{
    var result = PdfBlobRef.Create(
        blobName: blobName!,
        contentHash: new string('a', 64),
        sizeBytes: 12345L);

    result.IsFailed.Should().BeTrue();
    result.Errors.Should().Contain(e => e.Metadata.TryGetValue("ErrorCode", out var c) && (string)c! == "Invoicing.InvalidBlobName");
}
```

- [ ] **Step 2: Verify tests fail (compile error first — BlobName property doesn't exist)**

```bash
unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet test test/Invoicing.UnitTests/Invoicing.UnitTests.csproj
```

Expected: compile errors on `BlobName`, then failing tests once compile is unblocked.

- [ ] **Step 3: Rewrite PdfBlobRef (GREEN)**

```csharp
using FluentResults;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Errors;

namespace Invoicing.Domain.Common.ValueObjects;

/// <summary>
/// Content-addressed reference to a stored PDF artifact per ADR-0017.
/// <para>
/// Immutable once set on the aggregate (I-4) — PDFs are write-once.
/// <see cref="BlobName"/> is the canonical immutable identifier of the blob
/// (e.g., <c>"2026/05/INV-2026-000142.pdf"</c>). Callers compute fresh SAS URLs
/// on demand via <c>IBlobStore.GetSasUrlAsync</c>; the aggregate never persists
/// a bearer credential (issue #131).
/// </para>
/// </summary>
public sealed record PdfBlobRef(string BlobName, string ContentHash, long SizeBytes) : ValueObject
{
    public const int ContentHashLength = 64;
    public const int BlobNameMaxLength = 1024;
    private const string PdfExtension = ".pdf";

    public static Result<PdfBlobRef> Create(string blobName, string contentHash, long sizeBytes)
    {
        if (string.IsNullOrWhiteSpace(blobName) || blobName.Length > BlobNameMaxLength)
        {
            return Result.Fail<PdfBlobRef>(new ValidationError(
                nameof(BlobName),
                $"BlobName must be a non-empty path (max {BlobNameMaxLength} chars).",
                "Invoicing.InvalidBlobName"));
        }

        if (blobName.StartsWith('/') || blobName.StartsWith('\\'))
        {
            return Result.Fail<PdfBlobRef>(new ValidationError(
                nameof(BlobName), "BlobName must be a relative path (no leading slash).", "Invoicing.InvalidBlobName"));
        }

        if (!blobName.EndsWith(PdfExtension, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Fail<PdfBlobRef>(new ValidationError(
                nameof(BlobName), "BlobName must end with '.pdf'.", "Invoicing.InvalidBlobName"));
        }

        if (string.IsNullOrWhiteSpace(contentHash) || contentHash.Length != ContentHashLength)
        {
            return Result.Fail<PdfBlobRef>(new ValidationError(
                nameof(ContentHash),
                $"ContentHash must be {ContentHashLength} hex chars (SHA-256).",
                "Invoicing.InvalidContentHash"));
        }

        foreach (var ch in contentHash)
        {
            if (!IsLowerHex(ch))
            {
                return Result.Fail<PdfBlobRef>(new ValidationError(
                    nameof(ContentHash), "ContentHash must be lowercase hex.", "Invoicing.InvalidContentHash"));
            }
        }

        if (sizeBytes <= 0)
        {
            return Result.Fail<PdfBlobRef>(new ValidationError(
                nameof(SizeBytes), "SizeBytes must be strictly positive.", "Invoicing.InvalidBlobSize"));
        }

        return Result.Ok(new PdfBlobRef(blobName, contentHash, sizeBytes));
    }

    private static bool IsLowerHex(char ch) =>
        (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f');
}
```

- [ ] **Step 4: Update `TestDataFactory` and any other callers**

In `test/Invoicing.UnitTests/Common/TestDataFactory.cs`, replace any `PdfBlobRef.Create(new Uri(...), ...)` with `PdfBlobRef.Create("2026/05/INV-...-pdf", ...)`. Grep the test tree:

```bash
unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && \
grep -rn "PdfBlobRef.Create" test/ services/ | grep -v "\.cs:\d*:\s*//"
```

For each hit, replace `new Uri(absoluteUri)` arguments with a path-style string.

- [ ] **Step 5: Run unit tests to verify green**

```bash
unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet test test/Invoicing.UnitTests/Invoicing.UnitTests.csproj
```

Expected: PASS, all PdfBlobRef tests + downstream tests green.

- [ ] **Step 6: Commit**

```bash
git add services/Invoicing/Invoicing.Domain/Common/ValueObjects/PdfBlobRef.cs \
        test/Invoicing.UnitTests/Common/ValueObjects/PdfBlobRefTests.cs \
        test/Invoicing.UnitTests/Common/TestDataFactory.cs
git commit -m "refactor(invoicing-domain): PdfBlobRef.BlobUri -> BlobName (issue #131)"
```

### Task A4: Update `InvoiceIssuedEvent.avsc` — breaking field rename

**Files:**
- Modify: `platform/Platform.SchemaRegistry.Contracts/Avro/Invoicing/Invoices/InvoiceIssuedEvent.avsc`
- Modify (regenerated): `platform/Platform.SchemaRegistry.Contracts/Avro/Invoicing/Invoices/InvoiceIssuedEvent.cs`

- [ ] **Step 1: Replace `PdfBlobUri` field with `PdfBlobName`**

Locate the field block at lines 180-184. Replace with:

```json
        {
            "name": "PdfBlobName",
            "type": "string",
            "doc": "Canonical immutable blob name (e.g., '2026/05/INV-2026-000142.pdf'). Consumers must call Invoicing's GET endpoint (or re-mint via a shared IBlobStore for in-Invoicing readers) to get a fresh SAS URL — never embed long-lived URLs in this stream (issue #131)."
        },
```

- [ ] **Step 2: Trigger Avro regen**

```bash
dotnet build platform/Platform.SchemaRegistry.Contracts/Platform.SchemaRegistry.Contracts.csproj -m
```

Expected: `InvoiceIssuedEvent.cs` regenerates with `PdfBlobName` property. Build of dependent projects will fail (`InvoiceIssuedMapper.cs:43` still references `PdfBlobUri`) — that's expected; the next task fixes it.

- [ ] **Step 3: Commit just the Avro change**

```bash
git add platform/Platform.SchemaRegistry.Contracts/Avro/Invoicing/Invoices/InvoiceIssuedEvent.avsc \
        platform/Platform.SchemaRegistry.Contracts/Avro/Invoicing/Invoices/InvoiceIssuedEvent.cs
git commit -m "refactor(invoicing-avro)!: rename InvoiceIssuedEvent.PdfBlobUri -> PdfBlobName

Breaking change accepted per the reference-repo non-production deviation
from ADR-0007 FORWARD_TRANSITIVE policy. Existing Schema Registry subject
'Invoicing.Invoices.InvoiceIssuedEvent' must be deleted before re-registration
in any pre-existing environment (DELETE /subjects/{subject}); dev environments
require 'docker compose down -v' to reset the registry volume.

Refs #131"
```

### Task A5: Update `InvoiceIssuedMapper` + AzureBlobStore (downstream consumers)

**Files:**
- Modify: `services/Invoicing/Invoicing.Application/Outbox/InvoiceIssuedMapper.cs`
- Modify: `services/Invoicing/Invoicing.Infrastructure/Blobs/AzureBlobStore.cs:95`

- [ ] **Step 1: Update `InvoiceIssuedMapper.cs:43`**

Replace:
```csharp
PdfBlobUri = source.PdfBlobRef.BlobUri.AbsoluteUri,
```
with:
```csharp
PdfBlobName = source.PdfBlobRef.BlobName,
```

- [ ] **Step 2: Update `AzureBlobStore.UploadAsync` to pass `blobName` (param) to `PdfBlobRef.Create`**

`AzureBlobStore.cs` line 94-95: replace the `sasUri` argument with `blobName` and delete the `var sasUri = BuildSasUri(...)` line if it's no longer used elsewhere in `UploadAsync`. Verify `BuildSasUri` is still used by `GetSasUrlAsync` (it is — line 121). Keep it.

Before:
```csharp
var sasUri = BuildSasUri(blob, sasTtl, blobName);
var refResult = PdfBlobRef.Create(sasUri, contentHash, content.Length);
```
After:
```csharp
var refResult = PdfBlobRef.Create(blobName, contentHash, content.Length);
```

Also remove the inline xmldoc comment about `Wave-1 deferral` reference in the validation-failure block (lines 96-105) since the bug-class wording still applies but the URI-shape note is stale; update the comment to:

```csharp
// PdfBlobRef.Create validates blob name + hash + size — a failure here would indicate
// a bug in this adapter (e.g. malformed blobName passed in). Bug-class.
```

- [ ] **Step 3: Build to confirm everything compiles**

```bash
dotnet build -m
```

Expected: succeeds. (EF persistence still uses `pdf_blob_uri` column with URI conversion — that's broken at the column-mapping level but compiles because the model snapshot's `RuntimeType` for `BlobName` is now `string`. We'll fix that in Task A6.)

- [ ] **Step 4: Run unit tests**

```bash
unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet test test/Invoicing.UnitTests/Invoicing.UnitTests.csproj
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add services/Invoicing/Invoicing.Application/Outbox/InvoiceIssuedMapper.cs \
        services/Invoicing/Invoicing.Infrastructure/Blobs/AzureBlobStore.cs
git commit -m "refactor(invoicing): map PdfBlobName end-to-end through mapper + AzureBlobStore"
```

### Task A6: Update EF entity configurations (column rename + remove URI converter)

**Files:**
- Modify: `services/Invoicing/Invoicing.Infrastructure/Persistence/Database/EntityConfigurations/InvoiceConfiguration.cs:137-148`
- Modify: `services/Invoicing/Invoicing.Infrastructure/Persistence/Database/EntityConfigurations/CreditNoteConfiguration.cs` (analogous block)

- [ ] **Step 1: Replace the `OwnsOne(... PdfBlobRef ...)` block in `InvoiceConfiguration.cs`**

Replace the deferral comment block (lines 120-148, approximately — read the file fresh to confirm boundaries) with:

```csharp
// PdfBlobRef — owned, nullable until Issue(). BlobName is the canonical immutable
// identifier; the URI is computed on demand by callers via IBlobStore.GetSasUrlAsync.
builder.OwnsOne(i => i.PdfBlobRef, pdf =>
{
    pdf.Property(p => p.BlobName)
        .HasColumnName("pdf_blob_name")
        .HasMaxLength(Domain.Common.ValueObjects.PdfBlobRef.BlobNameMaxLength)
        .IsRequired();

    pdf.Property(p => p.ContentHash)
        .HasColumnName("pdf_content_hash")
        .HasMaxLength(Domain.Common.ValueObjects.PdfBlobRef.ContentHashLength)
        .IsRequired();

    pdf.Property(p => p.SizeBytes)
        .HasColumnName("pdf_size_bytes")
        .IsRequired();
});
```

Verify the actual existing config for `ContentHash`/`SizeBytes` properties in the same `OwnsOne` block — if they live below the `BlobUri` lines, retain them unchanged. Only the `BlobUri` block is changing.

Also remove the `private const int BlobUriMaxLength` if no longer referenced (grep first).

- [ ] **Step 2: Same change for `CreditNoteConfiguration.cs`**

Mirror the rename.

- [ ] **Step 3: Build**

```bash
dotnet build -m
```

Expected: succeeds, with a `PendingModelChangesWarning` at runtime startup until the migration lands.

- [ ] **Step 4: Commit (no test run yet — depends on migration)**

```bash
git add services/Invoicing/Invoicing.Infrastructure/Persistence/Database/EntityConfigurations/InvoiceConfiguration.cs \
        services/Invoicing/Invoicing.Infrastructure/Persistence/Database/EntityConfigurations/CreditNoteConfiguration.cs
git commit -m "refactor(invoicing-infra): EF config — pdf_blob_uri -> pdf_blob_name"
```

### Task A7: **USER STEP** — generate EF migration

**This task pauses execution and prompts the user.** Per CLAUDE.md, the agent must NEVER auto-generate EF Core migrations.

- [ ] **Step 1: Prompt the user**

Print to the user:

> Please generate the EF migration to apply the `pdf_blob_uri → pdf_blob_name` column rename. Run from the repo root (use whichever script/profile the team typically uses; the command shape is):
>
> ```bash
> dotnet ef migrations add RenamePdfBlobUriToBlobName \
>     --project services/Invoicing/Invoicing.Infrastructure \
>     --startup-project services/Invoicing/Invoicing.Api \
>     --output-dir Persistence/Database/Migrations
> ```
>
> Review the generated `Up()` method — it should use `RenameColumn` for `pdf_blob_uri → pdf_blob_name` on both `invoices` and `credit_notes` (and remove the URI `HasMaxLength` if it exceeds the new `BlobNameMaxLength`). If EF generates `DropColumn` + `AddColumn` instead of `RenameColumn`, manually edit the migration to use `migrationBuilder.RenameColumn(name: "pdf_blob_uri", schema: "invoicing", table: "invoices", newName: "pdf_blob_name");` — data preservation matters even in the reference repo (existing fixture seeds shouldn't get wiped).
>
> Once committed, tell me to resume.

- [ ] **Step 2: Wait for user confirmation**

Agent halts. User runs the EF command, commits, and replies.

### Task A8: Update Invoicing query handlers + functional tests + format/build sweep

Once Task A7 is unblocked:

**Files:**
- Modify: `services/Invoicing/Invoicing.Application/Invoices/GetInvoiceById/GetInvoiceByIdQueryHandler.cs`
- Modify: `services/Invoicing/Invoicing.Application/Invoices/GetInvoicesByBuyer/GetInvoicesByBuyerQueryHandler.cs`
- Modify: `services/Invoicing/Invoicing.Application/Invoices/GetInvoiceByOrderId/GetInvoiceByOrderIdQueryHandler.cs`
- Modify: `services/Invoicing/Invoicing.Application/CreditNotes/GetCreditNoteById/GetCreditNoteByIdQueryHandler.cs`
- Modify (likely): `test/Invoicing.FunctionalTests/Common/InvoiceSeed.cs`

- [ ] **Step 1: Read each query handler and update SAS-minting source**

In each handler, locate the call that produces the response's `pdfDownloadUrl`. Today it looks like:

```csharp
var sasUri = await _blobStore.GetSasUrlAsync(
    _blobOptions.InvoicesContainerName,
    InvoicePdfBlobName.For(invoice.InvoiceNumber!), // or wherever blobName comes from
    TimeSpan.FromMinutes(10),
    ct);
```

After the refactor the canonical source IS `invoice.PdfBlobRef.BlobName`. Update to:

```csharp
var sasUri = await _blobStore.GetSasUrlAsync(
    _blobOptions.InvoicesContainerName,
    invoice.PdfBlobRef!.BlobName,
    TimeSpan.FromMinutes(10),
    ct);
```

(`PdfBlobRef` is non-null for Issued/Delivered invoices per the aggregate invariant.)

- [ ] **Step 2: Update `InvoiceSeed.cs` if it constructs `PdfBlobRef`**

Grep:
```bash
grep -n "PdfBlobRef" test/Invoicing.FunctionalTests/Common/InvoiceSeed.cs
```

If any `new PdfBlobRef(...)` or `PdfBlobRef.Create(new Uri(...), ...)` exists, replace with the path-style blobName form.

- [ ] **Step 3: Format**

```bash
dotnet format whitespace --no-restore --verify-no-changes
dotnet format style --no-restore --verify-no-changes
```

If either reports diffs, run without `--verify-no-changes` to apply, then re-run with the verify flag to confirm.

- [ ] **Step 4: Full Invoicing build + per-slice test runs**

```bash
unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet build -m && \
dotnet test test/Invoicing.UnitTests/Invoicing.UnitTests.csproj && \
dotnet test test/Invoicing.ArchitectureTests/Invoicing.ArchitectureTests.csproj && \
dotnet test test/Invoicing.IntegrationTests/Invoicing.IntegrationTests.csproj && \
dotnet test test/Invoicing.FunctionalTests/Invoicing.FunctionalTests.csproj
```

Expected: all green. If `InvoiceIssuedEvent.PdfBlobName` assertions fire on existing tests, update the assertions from `PdfBlobUri` to `PdfBlobName`.

- [ ] **Step 5: Commit**

```bash
git add services/Invoicing/Invoicing.Application/Invoices/GetInvoiceById/ \
        services/Invoicing/Invoicing.Application/Invoices/GetInvoicesByBuyer/ \
        services/Invoicing/Invoicing.Application/Invoices/GetInvoiceByOrderId/ \
        services/Invoicing/Invoicing.Application/CreditNotes/GetCreditNoteById/ \
        test/Invoicing.FunctionalTests/Common/InvoiceSeed.cs \
        test/Invoicing.IntegrationTests/
git commit -m "refactor(invoicing): wire BlobName through query handlers + fixtures (issue #131)"
```

---

## Phase B — Notifications BC delivery consumer

End-of-phase state: Notifications consumes generic `SendEmailNotificationCommand`, renders via `EmailTemplateRenderer` (with one hardcoded template `"invoicing.invoice-delivered"`), sends via `MockEmailGateway`, and emits `EmailNotificationSentEvent`. Test projects exist with passing tests.

### Task B1: Scaffold `Notifications.UnitTests` project

**Files:**
- Create: `test/Notifications.UnitTests/Notifications.UnitTests.csproj`
- Modify (if needed): `DotNetAtlas.sln` (add project to solution)

- [ ] **Step 1: Create project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <ItemGroup>
        <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" />
        <PackageReference Include="NSubstitute" />
        <PackageReference Include="NSubstitute.Analyzers.CSharp" />
        <PackageReference Include="FluentResults.Extensions.FluentAssertions" />
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="..\..\services\Notifications\Notifications\Notifications.csproj" />
    </ItemGroup>
</Project>
```

- [ ] **Step 2: Add to solution**

```bash
dotnet sln add test/Notifications.UnitTests/Notifications.UnitTests.csproj
```

- [ ] **Step 3: Build to confirm references resolve**

```bash
dotnet build test/Notifications.UnitTests/Notifications.UnitTests.csproj -m
```

Expected: build succeeds (empty test project).

- [ ] **Step 4: Commit**

```bash
git add test/Notifications.UnitTests/Notifications.UnitTests.csproj DotNetAtlas.sln
git commit -m "test(notifications): scaffold Notifications.UnitTests project"
```

### Task B2: Add `EmailCommands` + `EmailEvents` to `TopicsOptions`

**Files:**
- Modify: `services/Notifications/Notifications/Common/Config/TopicsOptions.cs`
- Modify: `services/Notifications/Notifications/appsettings.json`
- Modify: `services/Notifications/Notifications/appsettings.Development.json` (if it exists)

- [ ] **Step 1: Extend `TopicsOptions`**

Add two `required` properties after the existing payment topics:

```csharp
/// <summary>Topic carrying SendEmailNotificationCommand (consumed by this BC).</summary>
[Required]
[Length(1, MaximumKafkaTopicLength)]
public required string EmailCommands { get; set; }

/// <summary>Topic carrying EmailNotificationSentEvent (produced by this BC).</summary>
[Required]
[Length(1, MaximumKafkaTopicLength)]
public required string EmailEvents { get; set; }
```

- [ ] **Step 2: Add config entries**

In `services/Notifications/Notifications/appsettings.json` extend the `Topics` section:

```json
"Topics": {
    "PaymentCommands": "...existing...",
    "Payments": "...existing...",
    "EmailCommands": "notifications.email-commands",
    "EmailEvents": "notifications.email-events",
    "DltTopicSuffix": "...existing..."
}
```

If `appsettings.Development.json` overrides topics, mirror.

- [ ] **Step 3: Confirm app boots (ValidateOnStart)**

```bash
dotnet build -m
```

Expected: build succeeds. (Boot-time validation only fires under `dotnet run` or tests; running the app isn't strictly necessary here.)

- [ ] **Step 4: Commit**

```bash
git add services/Notifications/Notifications/Common/Config/TopicsOptions.cs \
        services/Notifications/Notifications/appsettings.json \
        services/Notifications/Notifications/appsettings.Development.json 2>/dev/null
git commit -m "feat(notifications): add EmailCommands + EmailEvents to TopicsOptions"
```

### Task B3: Build `EmailMessage` + `IEmailGateway` + `MockEmailGateway`

**Files:**
- Create: `services/Notifications/Notifications/Email/EmailMessage.cs`
- Create: `services/Notifications/Notifications/Email/IEmailGateway.cs`
- Create: `services/Notifications/Notifications/Email/MockEmailGateway.cs`
- Create: `test/Notifications.UnitTests/Email/MockEmailGatewayTests.cs`

- [ ] **Step 1: Write failing test for `MockEmailGateway`**

```csharp
using AwesomeAssertions;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Notifications.Email;
using Xunit;

namespace Notifications.UnitTests.Email;

public sealed class MockEmailGatewayTests
{
    [Fact]
    public async Task SendAsync_AlwaysReturnsOk()
    {
        var gateway = new MockEmailGateway(NullLogger<MockEmailGateway>.Instance, new FakeTimeProvider());
        var result = await gateway.SendAsync(
            new EmailMessage("user-1", "Subject", "Body"),
            TestContext.Current.CancellationToken);

        result.Should().BeSuccess();
    }
}
```

- [ ] **Step 2: Run test — expect compile failure (types not defined)**

```bash
unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet test test/Notifications.UnitTests/Notifications.UnitTests.csproj
```

Expected: compile errors on `MockEmailGateway`, `EmailMessage`.

- [ ] **Step 3: Implement `EmailMessage`**

```csharp
namespace Notifications.Email;

/// <summary>Email envelope passed to <see cref="IEmailGateway"/>. ToUserId is the
/// recipient user identity; the gateway is responsible for resolving the actual address
/// (e.g., looking up the user-profile service). Mock gateway logs without delivering.</summary>
public sealed record EmailMessage(string ToUserId, string Subject, string Body);
```

- [ ] **Step 4: Implement `IEmailGateway`**

```csharp
using FluentResults;

namespace Notifications.Email;

/// <summary>Abstraction over the underlying email transport. Mock in Phase 1; real
/// gateway (SendGrid/SMTP) is a Phase-2 follow-up.</summary>
public interface IEmailGateway
{
    Task<Result> SendAsync(EmailMessage message, CancellationToken ct);
}
```

- [ ] **Step 5: Implement `MockEmailGateway`**

```csharp
using FluentResults;
using Microsoft.Extensions.Logging;

namespace Notifications.Email;

/// <summary>Logs the email and returns success. Default DI registration in dev/test.</summary>
internal sealed class MockEmailGateway : IEmailGateway
{
    private readonly ILogger<MockEmailGateway> _logger;
    private readonly TimeProvider _clock;

    public MockEmailGateway(ILogger<MockEmailGateway> logger, TimeProvider clock)
    {
        _logger = logger;
        _clock = clock;
    }

    public Task<Result> SendAsync(EmailMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        _logger.LogInformation(
            "[MOCK EMAIL] to={ToUserId} subject='{Subject}' body-len={BodyLen} at={At:O}",
            message.ToUserId, message.Subject, message.Body.Length, _clock.GetUtcNow());
        return Task.FromResult(Result.Ok());
    }
}
```

- [ ] **Step 6: Run test — expect PASS**

```bash
unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet test test/Notifications.UnitTests/Notifications.UnitTests.csproj
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add services/Notifications/Notifications/Email/ \
        test/Notifications.UnitTests/Email/MockEmailGatewayTests.cs
git commit -m "feat(notifications): IEmailGateway + MockEmailGateway"
```

### Task B4: Build `IEmailTemplateRenderer` + `EmailTemplateRenderer` with `invoicing.invoice-delivered`

**Files:**
- Create: `services/Notifications/Notifications/Email/IEmailTemplateRenderer.cs`
- Create: `services/Notifications/Notifications/Email/EmailTemplateRenderer.cs`
- Create: `test/Notifications.UnitTests/Email/EmailTemplateRendererTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using AwesomeAssertions;
using FluentResults.Extensions.FluentAssertions;
using Notifications.Email;
using Xunit;

namespace Notifications.UnitTests.Email;

public sealed class EmailTemplateRendererTests
{
    private readonly EmailTemplateRenderer _renderer = new();

    [Fact]
    public void Render_InvoicingInvoiceDelivered_WithAllFields_ReturnsOk()
    {
        var data = new Dictionary<string, string>
        {
            ["InvoiceNumber"]  = "INV-2026-000142",
            ["TotalAmount"]    = "152.00",
            ["Currency"]       = "EUR",
            ["ViewInvoiceUrl"] = "https://invoicing.example.com/invoices/00000000-0000-0000-0000-000000000001",
        };

        var result = _renderer.Render(toUserId: "00000000-0000-0000-0000-000000000099",
            templateId: "invoicing.invoice-delivered",
            data: data);

        result.Should().BeSuccess();
        result.Value.ToUserId.Should().Be("00000000-0000-0000-0000-000000000099");
        result.Value.Subject.Should().Be("Invoice INV-2026-000142 — your copy is ready");
        result.Value.Body.Should().Contain("INV-2026-000142");
        result.Value.Body.Should().Contain("https://invoicing.example.com/invoices/00000000-0000-0000-0000-000000000001");
    }

    [Fact]
    public void Render_InvoicingInvoiceDelivered_MissingInvoiceNumber_Fails()
    {
        var data = new Dictionary<string, string>
        {
            ["ViewInvoiceUrl"] = "https://invoicing.example.com/invoices/abc",
        };

        var result = _renderer.Render("user", "invoicing.invoice-delivered", data);
        result.Should().BeFailure();
        result.Errors.Should().Contain(e => e.Message.Contains("InvoiceNumber"));
    }

    [Fact]
    public void Render_InvoicingInvoiceDelivered_MissingViewInvoiceUrl_Fails()
    {
        var data = new Dictionary<string, string> { ["InvoiceNumber"] = "INV-2026-000001" };
        var result = _renderer.Render("user", "invoicing.invoice-delivered", data);
        result.Should().BeFailure();
        result.Errors.Should().Contain(e => e.Message.Contains("ViewInvoiceUrl"));
    }

    [Fact]
    public void Render_UnknownTemplate_Fails()
    {
        var result = _renderer.Render("user", "unknown.template", new Dictionary<string, string>());
        result.Should().BeFailure();
    }
}
```

- [ ] **Step 2: Run — expect compile failure**

```bash
unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet test test/Notifications.UnitTests/Notifications.UnitTests.csproj
```

- [ ] **Step 3: Implement `IEmailTemplateRenderer`**

```csharp
using FluentResults;

namespace Notifications.Email;

public interface IEmailTemplateRenderer
{
    Result<EmailMessage> Render(string toUserId, string templateId, IDictionary<string, string> data);
}
```

- [ ] **Step 4: Implement `EmailTemplateRenderer`**

```csharp
using FluentResults;

namespace Notifications.Email;

/// <summary>Phase-1 in-process renderer. One hardcoded template (<c>invoicing.invoice-delivered</c>);
/// future Phase-2 work introduces a template store + Razor/Liquid.</summary>
internal sealed class EmailTemplateRenderer : IEmailTemplateRenderer
{
    public Result<EmailMessage> Render(string toUserId, string templateId, IDictionary<string, string> data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        ArgumentNullException.ThrowIfNull(data);

        return templateId switch
        {
            "invoicing.invoice-delivered" => RenderInvoiceDelivered(toUserId, data),
            _ => Result.Fail<EmailMessage>($"Unknown template '{templateId}'."),
        };
    }

    private static Result<EmailMessage> RenderInvoiceDelivered(string toUserId, IDictionary<string, string> d)
    {
        if (!d.TryGetValue("InvoiceNumber", out var num) || string.IsNullOrWhiteSpace(num))
        {
            return Result.Fail<EmailMessage>("Missing 'InvoiceNumber'.");
        }

        if (!d.TryGetValue("ViewInvoiceUrl", out var url) || string.IsNullOrWhiteSpace(url))
        {
            return Result.Fail<EmailMessage>("Missing 'ViewInvoiceUrl'.");
        }

        var subject = $"Invoice {num} — your copy is ready";
        var total = d.GetValueOrDefault("TotalAmount", "");
        var currency = d.GetValueOrDefault("Currency", "");
        var totalLine = string.IsNullOrWhiteSpace(total) || string.IsNullOrWhiteSpace(currency)
            ? string.Empty
            : $"Total: {total} {currency}\n";

        var body = $"Hello,\n\nYour invoice {num} is ready.\n{totalLine}Sign in to view & download: {url}\n";
        return Result.Ok(new EmailMessage(toUserId, subject, body));
    }
}
```

- [ ] **Step 5: Run tests — expect PASS**

```bash
unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet test test/Notifications.UnitTests/Notifications.UnitTests.csproj
```

- [ ] **Step 6: Commit**

```bash
git add services/Notifications/Notifications/Email/IEmailTemplateRenderer.cs \
        services/Notifications/Notifications/Email/EmailTemplateRenderer.cs \
        test/Notifications.UnitTests/Email/EmailTemplateRendererTests.cs
git commit -m "feat(notifications): EmailTemplateRenderer with invoicing.invoice-delivered template"
```

### Task B5: Build `SendEmailNotificationCommandKafkaHandler`

**Files:**
- Create: `services/Notifications/Notifications/Notifications/SendEmailNotification/SendEmailNotificationCommandKafkaHandler.cs`
- Create: `test/Notifications.UnitTests/Notifications/SendEmailNotification/SendEmailNotificationCommandKafkaHandlerTests.cs`

- [ ] **Step 1: Write failing test — happy path + gateway-failure throw**

```csharp
using AwesomeAssertions;
using FluentResults;
using KafkaFlow;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Notifications.Common.Config;
using Notifications.Common.Persistence.Database;
using Notifications.Email;
using Notifications.Notifications.SendEmailNotification;
using NSubstitute;
using Notifications.Email; // EmailNotificationSentEvent ns
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore.Common;
using Xunit;

namespace Notifications.UnitTests.Notifications.SendEmailNotification;

public sealed class SendEmailNotificationCommandKafkaHandlerTests
{
    private readonly ITransactionalOutbox<INotificationsDbContext> _outbox = Substitute.For<ITransactionalOutbox<INotificationsDbContext>>();
    private readonly IEmailGateway _gateway = Substitute.For<IEmailGateway>();
    private readonly IEmailTemplateRenderer _renderer = new EmailTemplateRenderer();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 5, 22, 10, 0, 0, TimeSpan.Zero));

    private SendEmailNotificationCommandKafkaHandler CreateHandler() =>
        new(_outbox, _gateway, _renderer,
            Options.Create(new TopicsOptions
            {
                PaymentCommands = "n/a",
                Payments = "n/a",
                EmailCommands = "notifications.email-commands",
                EmailEvents = "notifications.email-events",
                DltTopicSuffix = ".DLT",
            }),
            _clock,
            NullLogger<SendEmailNotificationCommandKafkaHandler>.Instance);

    [Fact]
    public async Task Handle_HappyPath_SendsEmail_AndQueuesSentEvent()
    {
        _gateway.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok());

        var ctx = TestKafkaMessageContext.Create();
        var cmd = new global::Notifications.Email.SendEmailNotificationCommand
        {
            UserId = Guid.CreateVersion7().ToString(),
            TemplateId = "invoicing.invoice-delivered",
            TemplateData = new Dictionary<string, string>
            {
                ["InvoiceNumber"] = "INV-2026-000142",
                ["ViewInvoiceUrl"] = "https://invoicing.example.com/invoices/00000000-0000-0000-0000-000000000001",
            },
            IdempotencyKey = "invoice-delivered-00000000-0000-0000-0000-000000000001-1",
            OccurredOnUtc = _clock.GetUtcNow().UtcDateTime,
        };

        await CreateHandler().Handle(ctx, cmd);

        await _gateway.Received(1).SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
        _outbox.Received(1).AddOutboxMessage(
            "notifications.email-events",
            cmd.UserId,
            Arg.Is<EmailNotificationSentEvent>(e =>
                e.UserId == cmd.UserId && e.TemplateId == cmd.TemplateId && e.IdempotencyKey == cmd.IdempotencyKey));
    }

    [Fact]
    public async Task Handle_GatewayFailure_ThrowsForKafkaFlowRetry()
    {
        _gateway.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail("smtp down"));

        var act = async () => await CreateHandler().Handle(TestKafkaMessageContext.Create(), new global::Notifications.Email.SendEmailNotificationCommand
        {
            UserId = Guid.CreateVersion7().ToString(),
            TemplateId = "invoicing.invoice-delivered",
            TemplateData = new Dictionary<string, string>
            {
                ["InvoiceNumber"] = "INV-2026-000001",
                ["ViewInvoiceUrl"] = "https://x/",
            },
            IdempotencyKey = "invoice-delivered-x-1",
            OccurredOnUtc = _clock.GetUtcNow().UtcDateTime,
        });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*smtp down*");
    }
}
```

Note: `TestKafkaMessageContext.Create()` is a tiny helper that returns a `IMessageContext` substitute. If one doesn't exist in the test infrastructure yet, drop the indirection and substitute inline with `var ctx = Substitute.For<IMessageContext>(); ctx.ConsumerContext.WorkerStopped.Returns(CancellationToken.None);`. The `_outbox.Database.EnsureTransactionAsync(...)` is a no-op pass-through on the substitute by default; if needed configure `_outbox.Database.EnsureTransactionAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>()).Returns(callInfo => callInfo.Arg<Func<Task>>().Invoke());`.

- [ ] **Step 2: Run — expect compile errors**

- [ ] **Step 3: Implement the handler**

```csharp
using FluentResults;
using KafkaFlow;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notifications.Common.Config;
using Notifications.Common.Persistence.Database;
using Notifications.Email;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore.Common;

namespace Notifications.Notifications.SendEmailNotification;

/// <summary>
/// Consumes generic SendEmailNotificationCommand from notifications.email-commands.
/// Renders via IEmailTemplateRenderer, sends via IEmailGateway, and on success emits
/// EmailNotificationSentEvent to notifications.email-events. Inbox-deduped via
/// IdempotencyKey copied to the inbox primary key by KafkaFlow's AddInbox middleware.
/// </summary>
public sealed class SendEmailNotificationCommandKafkaHandler : IMessageHandler<SendEmailNotificationCommand>
{
    private readonly ITransactionalOutbox<INotificationsDbContext> _outbox;
    private readonly IEmailGateway _gateway;
    private readonly IEmailTemplateRenderer _renderer;
    private readonly TopicsOptions _topics;
    private readonly TimeProvider _clock;
    private readonly ILogger<SendEmailNotificationCommandKafkaHandler> _logger;

    public SendEmailNotificationCommandKafkaHandler(
        ITransactionalOutbox<INotificationsDbContext> outbox,
        IEmailGateway gateway,
        IEmailTemplateRenderer renderer,
        IOptions<TopicsOptions> topics,
        TimeProvider clock,
        ILogger<SendEmailNotificationCommandKafkaHandler> logger)
    {
        _outbox = outbox;
        _gateway = gateway;
        _renderer = renderer;
        _topics = topics.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task Handle(IMessageContext context, SendEmailNotificationCommand cmd)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(cmd);

        var token = context.ConsumerContext.WorkerStopped;

        await _outbox.Database.EnsureTransactionAsync(async () =>
        {
            var renderResult = _renderer.Render(cmd.UserId, cmd.TemplateId, cmd.TemplateData);
            if (renderResult.IsFailed)
            {
                // Bug-class — producer sent an unknown template or malformed data.
                _logger.LogError(
                    "EmailTemplateRenderer.Render failed for TemplateId={TemplateId}; IdempotencyKey={Key}; errors={Errors}",
                    cmd.TemplateId, cmd.IdempotencyKey,
                    string.Join("; ", renderResult.Errors.Select(e => e.Message)));
                throw new InvalidOperationException(
                    $"Template render failed: {string.Join("; ", renderResult.Errors.Select(e => e.Message))}");
            }

            var sendResult = await _gateway.SendAsync(renderResult.Value, token);
            if (sendResult.IsFailed)
            {
                // Transient — let KafkaFlow retry; eventually DLT.
                throw new InvalidOperationException(
                    $"Email gateway failed: {string.Join("; ", sendResult.Errors.Select(e => e.Message))}");
            }

            var now = _clock.GetUtcNow().UtcDateTime;
            _outbox.AddOutboxMessage(_topics.EmailEvents, cmd.UserId, new EmailNotificationSentEvent
            {
                UserId = cmd.UserId,
                TemplateId = cmd.TemplateId,
                IdempotencyKey = cmd.IdempotencyKey,
                SentAtUtc = now,
                OccurredOnUtc = now,
            });
            await _outbox.SaveChangesAsync(token);

            _logger.LogInformation(
                "Email sent and EmailNotificationSentEvent queued. UserId={UserId}, TemplateId={TemplateId}, IdempotencyKey={Key}",
                cmd.UserId, cmd.TemplateId, cmd.IdempotencyKey);
        }, token);
    }
}
```

- [ ] **Step 4: Run tests — expect PASS**

```bash
unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet test test/Notifications.UnitTests/Notifications.UnitTests.csproj
```

- [ ] **Step 5: Commit**

```bash
git add services/Notifications/Notifications/Notifications/SendEmailNotification/ \
        test/Notifications.UnitTests/Notifications/SendEmailNotification/
git commit -m "feat(notifications): SendEmailNotificationCommandKafkaHandler + tests"
```

### Task B6: Wire DI — register handler + add consumer to KafkaFlow

**Files:**
- Modify: `services/Notifications/Notifications/Common/MessagingDependencyInjection.cs`

- [ ] **Step 1: Register `IEmailGateway`, `IEmailTemplateRenderer`, handler**

Add inside `AddKafkaMessaging` before `services.AddKafka(...)`:

```csharp
services.AddScoped<IEmailGateway, MockEmailGateway>();
services.AddSingleton<IEmailTemplateRenderer, EmailTemplateRenderer>();
```

- [ ] **Step 2: Add a new `.AddConsumer` block for `EmailCommands`**

Inside the cluster builder (after the existing `PaymentCommands` consumer block), add:

```csharp
.AddConsumer(consumer => consumer
    .Topic(topicsOptions.EmailCommands)
    .WithConsumerConfig(consumerOptions)
    .WithBufferSize(consumerOptions.BufferSize)
    .WithWorkersCount(consumerOptions.WorkersCount)
    .AddMiddlewares(middlewares => middlewares
        .AddSchemaRegistryAvroDeserializer()
        .AddDeadLetter()
        .RetryForever(config => config
            .Handle<DbUpdateException>()
            .Handle<NpgsqlException>()
            .Handle<TimeoutException>()
            .WithTimeBetweenTriesPlan(
                TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)))
        .AddInbox(typeof(SendEmailNotificationCommand))
        .AddTypedHandlers(handlers => handlers
            .WithHandlerLifetime(InstanceLifetime.Scoped)
            .AddHandler<SendEmailNotificationCommandKafkaHandler>())
    ))
```

Add the `using Notifications.Email;` and `using Notifications.Notifications.SendEmailNotification;` directives.

- [ ] **Step 3: Build + run unit tests**

```bash
unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet build -m && dotnet test test/Notifications.UnitTests/Notifications.UnitTests.csproj
```

- [ ] **Step 4: Commit**

```bash
git add services/Notifications/Notifications/Common/MessagingDependencyInjection.cs
git commit -m "feat(notifications): wire EmailCommands consumer in KafkaFlow + DI for gateway/renderer"
```

### Task B7: Scaffold `Notifications.IntegrationTests` + Testcontainers fixture

**Files:**
- Create: `test/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj`
- Create: `test/Notifications.IntegrationTests/Common/IntegrationTestFixture.cs` (mirrors `test/Invoicing.IntegrationTests/Common/IntegrationTestFixture.cs`)
- Create: `test/Notifications.IntegrationTests/Common/IntegrationTestCollection.cs`

- [ ] **Step 1: Read `test/Invoicing.IntegrationTests/Common/IntegrationTestFixture.cs` to understand the pattern**

Read the file. Note the Testcontainers (Postgres + Schema Registry + Kafka) bootstrap structure, the `IAsyncLifetime` shape, the `IInvoicingDbContext` substitution mechanism, and the `IServiceScope` accessor.

- [ ] **Step 2: Create `Notifications.IntegrationTests.csproj`**

Mirror Invoicing.IntegrationTests' csproj — same `PackageReference` set (`Testcontainers`, `Microsoft.AspNetCore.Mvc.Testing`, `xunit`, `AwesomeAssertions`, etc.) with `ProjectReference` to `services/Notifications/Notifications`.

- [ ] **Step 3: Create `IntegrationTestFixture.cs`**

Adapt Invoicing's fixture: substitute `INotificationsDbContext` for `IInvoicingDbContext`, point to `NotificationsDbContext`, and remove Azurite container (Notifications doesn't touch blob storage). Keep Kafka + Schema Registry + Postgres containers.

- [ ] **Step 4: Create `IntegrationTestCollection.cs`**

```csharp
using Xunit;

namespace Notifications.IntegrationTests.Common;

[CollectionDefinition(nameof(IntegrationTestCollection))]
public sealed class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture> { }
```

- [ ] **Step 5: Add to solution + build**

```bash
dotnet sln add test/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj
unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet build test/Notifications.IntegrationTests/ -m
```

Expected: build succeeds. The empty fixture should be instantiable.

- [ ] **Step 6: Commit**

```bash
git add test/Notifications.IntegrationTests/ DotNetAtlas.sln
git commit -m "test(notifications): scaffold IntegrationTests project + Testcontainers fixture"
```

### Task B8: End-to-end integration test for `SendEmailNotificationCommandKafkaHandler`

**Files:**
- Create: `test/Notifications.IntegrationTests/Messaging/Kafka/SendEmailNotificationCommandKafkaHandlerTests.cs`

- [ ] **Step 1: Write the test**

```csharp
using AwesomeAssertions;
using FluentAssertions.Execution;
using KafkaFlow.Producers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Common.Config;
using Notifications.Common.Persistence.Database;
using Notifications.Email;
using Notifications.IntegrationTests.Common;
using Xunit;

namespace Notifications.IntegrationTests.Messaging.Kafka;

[Collection(nameof(IntegrationTestCollection))]
public sealed class SendEmailNotificationCommandKafkaHandlerTests
{
    private readonly IntegrationTestFixture _fixture;

    public SendEmailNotificationCommandKafkaHandlerTests(IntegrationTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Consume_InvoicingInvoiceDelivered_SendsEmail_AndWritesSentEventToOutbox()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.CreateVersion7().ToString();
        var idempotencyKey = $"invoice-delivered-{Guid.CreateVersion7()}-1";

        await using var producerScope = _fixture.CreateScope();
        var producers = producerScope.ServiceProvider.GetRequiredService<IProducerAccessor>();
        var topics = producerScope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<TopicsOptions>>().Value;

        // Use the integration-test fixture's helper to produce the Avro message directly to
        // the topic. The fixture should already expose a `ProduceAvroAsync<T>(topic, key, payload, ct)`
        // helper (mirror Invoicing.IntegrationTests' equivalent). If not, add one as part of this task —
        // it wraps a raw Confluent.Kafka producer configured against the fixture's schema-registry URL.
        await _fixture.ProduceAvroAsync(
            topics.EmailCommands, userId,
            new SendEmailNotificationCommand
            {
                UserId = userId,
                TemplateId = "invoicing.invoice-delivered",
                TemplateData = new Dictionary<string, string>
                {
                    ["InvoiceNumber"] = "INV-2026-INTEG-1",
                    ["ViewInvoiceUrl"] = "https://invoicing.test/invoices/abc",
                },
                IdempotencyKey = idempotencyKey,
                OccurredOnUtc = DateTime.UtcNow,
            });

        // Poll the outbox table for an EmailNotificationSentEvent row matching idempotencyKey.
        await using var assertScope = _fixture.CreateScope();
        var db = assertScope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        using var _ = new AssertionScope();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var sentRow = await PollUntilNonNull(async () =>
        {
            return await db.Set<Platform.ReliableMessaging.Outbox.EFCore.OutboxMessage>()
                .AsNoTracking()
                .Where(o => o.Topic == topics.EmailEvents && o.Payload.Contains(idempotencyKey))
                .FirstOrDefaultAsync(ct);
        }, TimeSpan.FromSeconds(30), ct);

        sentRow.Should().NotBeNull();
    }

    private static async Task<T?> PollUntilNonNull<T>(Func<Task<T?>> probe, TimeSpan timeout, CancellationToken ct) where T : class
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            ct.ThrowIfCancellationRequested();
            var v = await probe();
            if (v is not null) return v;
            await Task.Delay(200, ct);
        }
        return null;
    }
}
```

**NOTE**: the producer-accessor key and the `OutboxMessage` table shape must be verified against the actual codebase at execution time. The test may need to use a poll-via-`SchemaRegistryAvroDeserializer` consumer or query a different outbox table — read `test/Invoicing.IntegrationTests/Messaging/Kafka/` examples to align.

- [ ] **Step 2: Run**

```bash
unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet test test/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj
```

Expected: PASS. If the poll times out, inspect Notifications service logs (the `MockEmailGateway` should log a `[MOCK EMAIL]` line on receipt) to triage.

- [ ] **Step 3: Commit**

```bash
git add test/Notifications.IntegrationTests/Messaging/Kafka/SendEmailNotificationCommandKafkaHandlerTests.cs
git commit -m "test(notifications): integration test for SendEmailNotificationCommand round-trip"
```

---

## Phase C — Invoicing publishers + reciprocal consumer

End-of-phase state: Invoicing has the 3 new handlers (request publisher + sent consumer + delivered publisher), `BuyerPortalOptions`, and topic options + appsettings, but `IssueInvoiceCommandHandler.cs:159` still hard-codes `DeliveryChannel.None`. Phase D flips the channel.

### Task C1: `BuyerPortalOptions`

**Files:**
- Create: `services/Invoicing/Invoicing.Application/Common/Notifications/BuyerPortalOptions.cs`
- Modify: `services/Invoicing/Invoicing.Api/appsettings.json` (+ Development)

- [ ] **Step 1: Create the options class**

```csharp
using System.ComponentModel.DataAnnotations;

namespace Invoicing.Application.Common.Notifications;

/// <summary>
/// Configuration for the buyer-portal URL embedded in delivery-notification emails.
/// Production points at the buyer portal frontend host; dev defaults to the Invoicing API
/// itself (clicking through hits the existing GET endpoint that mints a SAS server-side).
/// </summary>
public sealed class BuyerPortalOptions
{
    public const string Section = "BuyerPortal";

    [Required(AllowEmptyStrings = false)]
    [Url]
    public required string BaseUrl { get; set; }
}
```

- [ ] **Step 2: Register options in DI**

Locate the Invoicing.Api `Program.cs` or `*DependencyInjection.cs` that already registers other option types. Add:

```csharp
services.AddOptionsWithValidateOnStart<BuyerPortalOptions>()
    .BindConfiguration(BuyerPortalOptions.Section)
    .ValidateDataAnnotations();
```

- [ ] **Step 3: appsettings**

In `services/Invoicing/Invoicing.Api/appsettings.json`:

```json
"BuyerPortal": {
    "BaseUrl": "https://invoicing.example.com"
}
```

In `services/Invoicing/Invoicing.Api/appsettings.Development.json`:

```json
"BuyerPortal": {
    "BaseUrl": "http://localhost:5400"
}
```

- [ ] **Step 4: Build + commit**

```bash
dotnet build -m
git add services/Invoicing/Invoicing.Application/Common/Notifications/BuyerPortalOptions.cs \
        services/Invoicing/Invoicing.Api/appsettings.json \
        services/Invoicing/Invoicing.Api/appsettings.Development.json
# Add the DI registration file (path depends on actual layout)
git add services/Invoicing/Invoicing.Application/Common/*DependencyInjection*.cs
git commit -m "feat(invoicing): BuyerPortalOptions for non-credential email-link URL"
```

### Task C2: Extend `TopicsOptions` with notifications email topics

**Files:**
- Modify: `services/Invoicing/Invoicing.Application/Common/Messaging/TopicsOptions.cs`
- Modify: `services/Invoicing/Invoicing.Api/appsettings.json` (+ Development, + test settings if any)

- [ ] **Step 1: Add two `required` properties**

```csharp
/// <summary>Outbound topic for SendEmailNotificationCommand to Notifications BC.</summary>
[Required(AllowEmptyStrings = false)]
[Length(1, MaximumKafkaTopicLength)]
public required string NotificationsEmailCommands { get; set; }

/// <summary>Inbound topic carrying EmailNotificationSentEvent from Notifications BC.</summary>
[Required(AllowEmptyStrings = false)]
[Length(1, MaximumKafkaTopicLength)]
public required string NotificationsEmailEvents { get; set; }
```

- [ ] **Step 2: appsettings entries**

In every `appsettings*.json` for Invoicing.Api, add to the `InvoicingTopics` section:

```json
"NotificationsEmailCommands": "notifications.email-commands",
"NotificationsEmailEvents": "notifications.email-events"
```

Also update `test/Invoicing.IntegrationTests/appsettings*.json` and `test/Invoicing.FunctionalTests/appsettings*.json` if they bind `InvoicingTopics`.

- [ ] **Step 3: Build + commit**

```bash
dotnet build -m
git add services/Invoicing/Invoicing.Application/Common/Messaging/TopicsOptions.cs \
        services/Invoicing/Invoicing.Api/appsettings.json \
        services/Invoicing/Invoicing.Api/appsettings.Development.json \
        test/Invoicing.IntegrationTests/appsettings*.json 2>/dev/null \
        test/Invoicing.FunctionalTests/appsettings*.json 2>/dev/null
git commit -m "feat(invoicing): wire NotificationsEmailCommands + Events into TopicsOptions"
```

### Task C3: `InvoiceDeliveryRequestedOutboxPublisherDomainEventHandler` + unit tests

**Files:**
- Create: `services/Invoicing/Invoicing.Application/Outbox/InvoiceDeliveryRequestedOutboxPublisherDomainEventHandler.cs`
- Create: `test/Invoicing.UnitTests/Application/Invoices/Delivery/InvoiceDeliveryRequestedOutboxPublisherTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using AwesomeAssertions;
using FluentAssertions.Execution;
using Invoicing.Application.Common.Data;
using Invoicing.Application.Common.Messaging;
using Invoicing.Application.Common.Notifications;
using Invoicing.Application.Outbox;
using Invoicing.Domain.Invoices;
using Invoicing.Domain.Invoices.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Notifications.Email;
using NSubstitute;
using Platform.ReliableMessaging.Outbox.EFCore;
using Xunit;

namespace Invoicing.UnitTests.Application.Invoices.Delivery;

public sealed class InvoiceDeliveryRequestedOutboxPublisherTests
{
    [Fact]
    public async Task Handle_QueuesSendEmailNotificationCommand_WithCorrectTopicKeyAndTemplateData()
    {
        // Arrange: a stub IInvoicingDbContext returning one issued Invoice.
        var invoiceId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();
        var invoice = InvoiceFactoryForTests.Issued(invoiceId, buyerId, "INV-2026-000042", totalAmount: 152.00m, "EUR");
        var dbStub = StubInvoicingDbContext.WithInvoice(invoice);

        var outbox = Substitute.For<ITransactionalOutbox<IInvoicingDbContext>>();
        var topics = Options.Create(new TopicsOptions
        {
            Invoices = "invoicing.invoices",
            OrderingOrders = "n/a",
            PaymentsTransactions = "n/a",
            NotificationsEmailCommands = "notifications.email-commands",
            NotificationsEmailEvents = "notifications.email-events",
            DltTopicSuffix = ".DLT",
        });
        var portal = Options.Create(new BuyerPortalOptions { BaseUrl = "https://invoicing.example.com" });
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 22, 10, 0, 0, TimeSpan.Zero));

        var handler = new InvoiceDeliveryRequestedOutboxPublisherDomainEventHandler(
            outbox, dbStub, topics, portal, clock, NullLogger<InvoiceDeliveryRequestedOutboxPublisherDomainEventHandler>.Instance);

        var domainEvent = new InvoiceDeliveryRequestedDomainEvent
        {
            InvoiceId = invoiceId,
            BuyerId = buyerId,
            Channel = DeliveryChannel.Email,
            Attempt = 1,
            CorrelationId = Guid.CreateVersion7(),
            OccurredOnUtc = clock.GetUtcNow(),
        };

        await handler.Handle(domainEvent, CancellationToken.None);

        using var _ = new AssertionScope();
        outbox.Received(1).AddOutboxMessage(
            "notifications.email-commands",
            buyerId.ToString(),
            Arg.Is<SendEmailNotificationCommand>(c =>
                c.UserId == buyerId.ToString() &&
                c.TemplateId == "invoicing.invoice-delivered" &&
                c.IdempotencyKey == $"invoice-delivered-{invoiceId}-1" &&
                c.TemplateData["InvoiceNumber"] == "INV-2026-000042" &&
                c.TemplateData["TotalAmount"] == "152.00" &&
                c.TemplateData["Currency"] == "EUR" &&
                c.TemplateData["ViewInvoiceUrl"] == $"https://invoicing.example.com/invoices/{invoiceId}"));
    }
}
```

Helper classes `InvoiceFactoryForTests.Issued(...)` and `StubInvoicingDbContext.WithInvoice(...)` likely already exist in `test/Invoicing.UnitTests/Common/`; if not, create thin shims under `test/Invoicing.UnitTests/Common/`.

- [ ] **Step 2: Run — expect compile failure (handler class not found)**

- [ ] **Step 3: Implement the handler**

```csharp
using System.Globalization;
using Invoicing.Application.Common.Data;
using Invoicing.Application.Common.Messaging;
using Invoicing.Application.Common.Notifications;
using Invoicing.Domain.Invoices.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notifications.Email;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.Exceptions;

namespace Invoicing.Application.Outbox;

/// <summary>
/// Fans out <see cref="InvoiceDeliveryRequestedDomainEvent"/> as a generic
/// <c>SendEmailNotificationCommand</c> on the Notifications email-commands topic.
/// Runs inside the same EF transaction as the aggregate save (DispatchDomainEventsInterceptor
/// dispatches before SaveChangesAsync), so the outbox row is atomic with the aggregate.
/// No blob-store access — the email body links to the buyer portal, which mints a SAS
/// server-side via the existing GET endpoint (issue #131).
/// </summary>
public sealed class InvoiceDeliveryRequestedOutboxPublisherDomainEventHandler
    : IDomainEventHandler<InvoiceDeliveryRequestedDomainEvent>
{
    private readonly ITransactionalOutbox<IInvoicingDbContext> _outbox;
    private readonly IInvoicingDbContext _db;
    private readonly TopicsOptions _topics;
    private readonly BuyerPortalOptions _portal;
    private readonly TimeProvider _clock;
    private readonly ILogger<InvoiceDeliveryRequestedOutboxPublisherDomainEventHandler> _logger;

    public InvoiceDeliveryRequestedOutboxPublisherDomainEventHandler(
        ITransactionalOutbox<IInvoicingDbContext> outbox,
        IInvoicingDbContext db,
        IOptions<TopicsOptions> topics,
        IOptions<BuyerPortalOptions> portal,
        TimeProvider clock,
        ILogger<InvoiceDeliveryRequestedOutboxPublisherDomainEventHandler> logger)
    {
        _outbox = outbox;
        _db = db;
        _topics = topics.Value;
        _portal = portal.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task Handle(InvoiceDeliveryRequestedDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var invoice = await _db.Invoices
            .SingleOrDefaultAsync(i => i.Id == domainEvent.InvoiceId, ct)
            ?? throw new DataIntegrityException(
                "Invoicing.InvoiceMissingOnDeliveryRequest",
                $"No invoice for id '{domainEvent.InvoiceId}' (raised by domain event in same transaction).");

        var portalUrl = $"{_portal.BaseUrl.TrimEnd('/')}/invoices/{domainEvent.InvoiceId}";

        var command = new SendEmailNotificationCommand
        {
            UserId = domainEvent.BuyerId.ToString(),
            TemplateId = "invoicing.invoice-delivered",
            TemplateData = new Dictionary<string, string>
            {
                ["InvoiceNumber"]  = invoice.InvoiceNumber!.Value,
                ["TotalAmount"]    = invoice.Total.Amount.ToString(CultureInfo.InvariantCulture),
                ["Currency"]       = invoice.Total.Currency.Name,
                ["ViewInvoiceUrl"] = portalUrl,
            },
            IdempotencyKey = $"invoice-delivered-{domainEvent.InvoiceId}-{domainEvent.Attempt}",
            OccurredOnUtc = _clock.GetUtcNow().UtcDateTime,
        };

        _outbox.AddOutboxMessage(_topics.NotificationsEmailCommands, domainEvent.BuyerId.ToString(), command);
        _logger.LogInformation(
            "Queued invoice-delivery email request. InvoiceId={InvoiceId}, Attempt={Attempt}",
            domainEvent.InvoiceId, domainEvent.Attempt);
    }
}
```

- [ ] **Step 4: Register handler in DI**

In `services/Invoicing/Invoicing.Application/Common/InfrastructureDependencyInjection.cs` (or equivalent), add the registration alongside the existing 3 publishers. Pattern:

```csharp
services.AddScoped<IDomainEventHandler<InvoiceDeliveryRequestedDomainEvent>, InvoiceDeliveryRequestedOutboxPublisherDomainEventHandler>();
```

- [ ] **Step 5: Run unit tests — expect PASS**

```bash
unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet test test/Invoicing.UnitTests/Invoicing.UnitTests.csproj
```

- [ ] **Step 6: Commit**

```bash
git add services/Invoicing/Invoicing.Application/Outbox/InvoiceDeliveryRequestedOutboxPublisherDomainEventHandler.cs \
        services/Invoicing/Invoicing.Application/Common/*DependencyInjection*.cs \
        test/Invoicing.UnitTests/Application/Invoices/Delivery/InvoiceDeliveryRequestedOutboxPublisherTests.cs
git commit -m "feat(invoicing): InvoiceDeliveryRequestedOutboxPublisher emits SendEmailNotificationCommand"
```

### Task C4: `InvoiceDeliveredMapper`

**Files:**
- Create: `services/Invoicing/Invoicing.Application/Outbox/InvoiceDeliveredMapper.cs`
- Create: `test/Invoicing.UnitTests/Application/Invoices/Delivery/InvoiceDeliveredMapperTests.cs`

- [ ] **Step 1: Write test**

```csharp
using AwesomeAssertions;
using Invoicing.Application.Outbox;
using Invoicing.Domain.Invoices.Events;
using Invoicing.Domain.Invoices.ValueObjects;
using Xunit;

namespace Invoicing.UnitTests.Application.Invoices.Delivery;

public sealed class InvoiceDeliveredMapperTests
{
    [Fact]
    public void ToInvoiceDeliveredEvent_MapsAllFields()
    {
        var now = new DateTimeOffset(2026, 5, 22, 10, 0, 0, TimeSpan.Zero);
        var domainEvent = new InvoiceDeliveredDomainEvent
        {
            InvoiceId = Guid.CreateVersion7(),
            BuyerId = Guid.CreateVersion7(),
            DeliveredAtUtc = now,
            Channel = DeliveryChannel.Email,
            CorrelationId = Guid.CreateVersion7(),
            OccurredOnUtc = now,
        };

        var result = domainEvent.ToInvoiceDeliveredEvent();

        result.InvoiceId.Should().Be(domainEvent.InvoiceId);
        result.BuyerId.Should().Be(domainEvent.BuyerId);
        result.DeliveredAtUtc.Should().Be(now.UtcDateTime);
        result.Channel.Should().Be("Email");
        result.CorrelationId.Should().Be(domainEvent.CorrelationId);
        result.OccurredOnUtc.Should().Be(now.UtcDateTime);
    }
}
```

- [ ] **Step 2: Implement**

```csharp
using Invoicing.Domain.Invoices.Events;
using Invoicing.Invoices;
using Riok.Mapperly.Abstractions;

namespace Invoicing.Application.Outbox;

[Mapper]
public static partial class InvoiceDeliveredMapper
{
    public static InvoiceDeliveredEvent ToInvoiceDeliveredEvent(this InvoiceDeliveredDomainEvent source) =>
        new()
        {
            InvoiceId = source.InvoiceId,
            BuyerId = source.BuyerId,
            DeliveredAtUtc = source.DeliveredAtUtc.UtcDateTime,
            Channel = source.Channel.Name,
            CorrelationId = source.CorrelationId,
            OccurredOnUtc = source.OccurredOnUtc.UtcDateTime,
        };
}
```

- [ ] **Step 3: Run + commit**

```bash
unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet test test/Invoicing.UnitTests/Invoicing.UnitTests.csproj
git add services/Invoicing/Invoicing.Application/Outbox/InvoiceDeliveredMapper.cs \
        test/Invoicing.UnitTests/Application/Invoices/Delivery/InvoiceDeliveredMapperTests.cs
git commit -m "feat(invoicing): InvoiceDeliveredMapper for domain -> Avro"
```

### Task C5: `InvoiceDeliveredOutboxPublisherDomainEventHandler` + tests

**Files:**
- Create: `services/Invoicing/Invoicing.Application/Outbox/InvoiceDeliveredOutboxPublisherDomainEventHandler.cs`
- Create: `test/Invoicing.UnitTests/Application/Invoices/Delivery/InvoiceDeliveredOutboxPublisherTests.cs`

- [ ] **Step 1: Write failing test (mirror `InvoiceIssuedOutboxPublisher` test shape)**

```csharp
using AwesomeAssertions;
using Invoicing.Application.Common.Data;
using Invoicing.Application.Common.Messaging;
using Invoicing.Application.Outbox;
using Invoicing.Domain.Invoices.Events;
using Invoicing.Domain.Invoices.ValueObjects;
using Invoicing.Invoices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Platform.ReliableMessaging.Outbox.EFCore;
using Xunit;

namespace Invoicing.UnitTests.Application.Invoices.Delivery;

public sealed class InvoiceDeliveredOutboxPublisherTests
{
    [Fact]
    public async Task Handle_QueuesInvoiceDeliveredEventOnInvoicesTopic_WithBuyerIdKey()
    {
        var outbox = Substitute.For<ITransactionalOutbox<IInvoicingDbContext>>();
        var topics = Options.Create(new TopicsOptions
        {
            Invoices = "invoicing.invoices",
            OrderingOrders = "n/a", PaymentsTransactions = "n/a",
            NotificationsEmailCommands = "n/a", NotificationsEmailEvents = "n/a",
            DltTopicSuffix = ".DLT",
        });

        var handler = new InvoiceDeliveredOutboxPublisherDomainEventHandler(
            outbox, topics, NullLogger<InvoiceDeliveredOutboxPublisherDomainEventHandler>.Instance);

        var buyerId = Guid.CreateVersion7();
        var domainEvent = new InvoiceDeliveredDomainEvent
        {
            InvoiceId = Guid.CreateVersion7(),
            BuyerId = buyerId,
            DeliveredAtUtc = DateTimeOffset.UtcNow,
            Channel = DeliveryChannel.Email,
            CorrelationId = Guid.CreateVersion7(),
            OccurredOnUtc = DateTimeOffset.UtcNow,
        };

        await handler.Handle(domainEvent, CancellationToken.None);

        outbox.Received(1).AddOutboxMessage(
            "invoicing.invoices", buyerId.ToString(),
            Arg.Is<InvoiceDeliveredEvent>(e => e.InvoiceId == domainEvent.InvoiceId && e.Channel == "Email"));
    }
}
```

- [ ] **Step 2: Implement**

```csharp
using Invoicing.Application.Common.Data;
using Invoicing.Application.Common.Messaging;
using Invoicing.Domain.Invoices.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;

namespace Invoicing.Application.Outbox;

/// <summary>
/// Fans out <see cref="InvoiceDeliveredDomainEvent"/> to <c>invoicing.invoices</c> as
/// <c>InvoiceDeliveredEvent</c>. Mirrors the 3 sibling publishers in this folder.
/// </summary>
public sealed class InvoiceDeliveredOutboxPublisherDomainEventHandler
    : IDomainEventHandler<InvoiceDeliveredDomainEvent>
{
    private readonly ITransactionalOutbox<IInvoicingDbContext> _outbox;
    private readonly TopicsOptions _topics;
    private readonly ILogger<InvoiceDeliveredOutboxPublisherDomainEventHandler> _logger;

    public InvoiceDeliveredOutboxPublisherDomainEventHandler(
        ITransactionalOutbox<IInvoicingDbContext> outbox,
        IOptions<TopicsOptions> topics,
        ILogger<InvoiceDeliveredOutboxPublisherDomainEventHandler> logger)
    {
        _outbox = outbox;
        _topics = topics.Value;
        _logger = logger;
    }

    public Task Handle(InvoiceDeliveredDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var avro = domainEvent.ToInvoiceDeliveredEvent();
        _outbox.AddOutboxMessage(_topics.Invoices, domainEvent.BuyerId.ToString(), avro);
        _logger.LogInformation(
            "Queued InvoiceDeliveredEvent to outbox. InvoiceId={InvoiceId}, CorrelationId={CorrelationId}",
            domainEvent.InvoiceId, domainEvent.CorrelationId);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 3: Register in DI**

```csharp
services.AddScoped<IDomainEventHandler<InvoiceDeliveredDomainEvent>, InvoiceDeliveredOutboxPublisherDomainEventHandler>();
```

- [ ] **Step 4: Run + commit**

```bash
unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet test test/Invoicing.UnitTests/Invoicing.UnitTests.csproj
git add services/Invoicing/Invoicing.Application/Outbox/InvoiceDeliveredOutboxPublisherDomainEventHandler.cs \
        services/Invoicing/Invoicing.Application/Common/*DependencyInjection*.cs \
        test/Invoicing.UnitTests/Application/Invoices/Delivery/InvoiceDeliveredOutboxPublisherTests.cs
git commit -m "feat(invoicing): InvoiceDeliveredOutboxPublisher emits InvoiceDeliveredEvent.avsc"
```

---

## Phase D — Reciprocal consumer + flip the channel

### Task D1: `EmailNotificationSentEventKafkaHandler` (Invoicing-side)

**Files:**
- Create: `services/Invoicing/Invoicing.Application/Messaging/EmailNotificationSentEventKafkaHandler.cs`
- Create: `test/Invoicing.IntegrationTests/Messaging/Kafka/EmailNotificationSentEventKafkaHandlerTests.cs`

The integration test goes here (not unit) because the handler exercises EF persistence + outbox + domain-event dispatch — that's an integration concern.

- [ ] **Step 1: Write integration test (RED)**

```csharp
using AwesomeAssertions;
using FluentAssertions.Execution;
using Invoicing.Application.Common.Data;
using Invoicing.Application.Messaging;
using Invoicing.Domain.Invoices;
using Invoicing.Domain.Invoices.ValueObjects;
using Invoicing.IntegrationTests.Common;
using KafkaFlow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Email;
using NSubstitute;
using Xunit;

namespace Invoicing.IntegrationTests.Messaging.Kafka;

[Collection(nameof(IntegrationTestCollection))]
public sealed class EmailNotificationSentEventKafkaHandlerTests
{
    private readonly IntegrationTestFixture _fixture;

    public EmailNotificationSentEventKafkaHandlerTests(IntegrationTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Handle_InvoicingPrefixedTemplate_TransitionsInvoiceToDelivered_AndEnqueuesAvroEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var (invoiceId, buyerId) = await _fixture.SeedIssuedInvoiceAsync(ct);

        await using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<EmailNotificationSentEventKafkaHandler>();

        var ctx = TestKafkaMessageContext.Create(); // mirrors helper used elsewhere in IntegrationTests
        var sent = new EmailNotificationSentEvent
        {
            UserId = buyerId.ToString(),
            TemplateId = "invoicing.invoice-delivered",
            IdempotencyKey = $"invoice-delivered-{invoiceId}-1",
            SentAtUtc = DateTime.UtcNow,
            OccurredOnUtc = DateTime.UtcNow,
        };

        await handler.Handle(ctx, sent);

        using var _ = new AssertionScope();
        await using var assertScope = _fixture.CreateScope();
        var db = assertScope.ServiceProvider.GetRequiredService<IInvoicingDbContext>();
        var invoice = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId, ct);
        invoice.Status.Should().Be(InvoiceStatus.Delivered);

        // Outbox row for InvoiceDeliveredEvent on invoicing.invoices
        var outboxRows = await db.Set<Platform.ReliableMessaging.Outbox.EFCore.OutboxMessage>()
            .AsNoTracking().Where(r => r.PartitionKey == buyerId.ToString())
            .ToListAsync(ct);
        outboxRows.Should().Contain(r => r.Topic == "invoicing.invoices" && r.Payload.Contains("\"InvoiceId\""));
    }

    [Fact]
    public async Task Handle_NonInvoicingPrefix_NoOps()
    {
        var ct = TestContext.Current.CancellationToken;
        var (invoiceId, buyerId) = await _fixture.SeedIssuedInvoiceAsync(ct);

        await using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<EmailNotificationSentEventKafkaHandler>();

        await handler.Handle(TestKafkaMessageContext.Create(), new EmailNotificationSentEvent
        {
            UserId = buyerId.ToString(),
            TemplateId = "weather.alert",
            IdempotencyKey = $"alert-{Guid.CreateVersion7()}-1",
            SentAtUtc = DateTime.UtcNow,
            OccurredOnUtc = DateTime.UtcNow,
        });

        await using var assert = _fixture.CreateScope();
        var db = assert.ServiceProvider.GetRequiredService<IInvoicingDbContext>();
        var invoice = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId, ct);
        invoice.Status.Should().Be(InvoiceStatus.Issued); // unchanged
    }

    [Fact]
    public async Task Handle_InvoiceAlreadyDelivered_NoOpsAndDoesNotEnqueueDuplicate()
    {
        var ct = TestContext.Current.CancellationToken;
        var (invoiceId, buyerId) = await _fixture.SeedDeliveredInvoiceAsync(ct);

        await using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<EmailNotificationSentEventKafkaHandler>();

        await handler.Handle(TestKafkaMessageContext.Create(), new EmailNotificationSentEvent
        {
            UserId = buyerId.ToString(),
            TemplateId = "invoicing.invoice-delivered",
            IdempotencyKey = $"invoice-delivered-{invoiceId}-2",
            SentAtUtc = DateTime.UtcNow,
            OccurredOnUtc = DateTime.UtcNow,
        });

        // Invoice still Delivered (no second .Deliver applied); no additional
        // InvoiceDeliveredEvent outbox row enqueued for this invoice on this attempt.
        await using var assert = _fixture.CreateScope();
        var db = assert.ServiceProvider.GetRequiredService<IInvoicingDbContext>();
        var invoice = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId, ct);
        invoice.Status.Should().Be(InvoiceStatus.Delivered);

        var deliveredRows = await db.Set<Platform.ReliableMessaging.Outbox.EFCore.OutboxMessage>()
            .AsNoTracking()
            .Where(r => r.Topic == "invoicing.invoices" && r.PartitionKey == buyerId.ToString()
                        && r.Payload.Contains("InvoiceDeliveredEvent"))
            .CountAsync(ct);
        deliveredRows.Should().Be(1, "redelivery is a no-op once the aggregate is already Delivered");
    }

    [Fact]
    public async Task Handle_UnknownInvoiceId_ThrowsDataIntegrityException()
    {
        await using var scope = _fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<EmailNotificationSentEventKafkaHandler>();

        var unknownInvoiceId = Guid.CreateVersion7();
        var act = async () => await handler.Handle(TestKafkaMessageContext.Create(), new EmailNotificationSentEvent
        {
            UserId = Guid.CreateVersion7().ToString(),
            TemplateId = "invoicing.invoice-delivered",
            IdempotencyKey = $"invoice-delivered-{unknownInvoiceId}-1",
            SentAtUtc = DateTime.UtcNow,
            OccurredOnUtc = DateTime.UtcNow,
        });

        await act.Should().ThrowAsync<Platform.SharedKernel.Exceptions.DataIntegrityException>()
            .Where(ex => ex.ErrorCode == "Invoicing.InvoiceUnknownOnDeliveryConfirmation");
    }
}
```

Helpers needed on `IntegrationTestFixture`: `SeedIssuedInvoiceAsync(ct)` and `SeedDeliveredInvoiceAsync(ct)` returning `(Guid invoiceId, Guid buyerId)`. Mirror existing seed helpers like `SeedConvergedPendingInvoiceAsync`.

- [ ] **Step 2: Implement the handler**

```csharp
using Invoicing.Application.Common.Data;
using Invoicing.Domain.Invoices;
using KafkaFlow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Notifications.Email;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Exceptions;

namespace Invoicing.Application.Messaging;

/// <summary>
/// Invoicing-side consumer for the generic EmailNotificationSentEvent. Filters by
/// TemplateId prefix "invoicing." and only handles "invoicing.invoice-delivered".
/// Parses InvoiceId from the IdempotencyKey ("invoice-delivered-{guid}-{attempt}"),
/// loads the Invoice, and calls Invoice.Deliver(now), which raises
/// InvoiceDeliveredDomainEvent → InvoiceDeliveredOutboxPublisher → Avro outbox row.
/// </summary>
public sealed class EmailNotificationSentEventKafkaHandler : IMessageHandler<EmailNotificationSentEvent>
{
    private const string InvoicingPrefix = "invoicing.";
    private const string InvoiceDeliveredTemplate = "invoicing.invoice-delivered";

    private readonly IInvoicingDbContext _db;
    private readonly ITransactionalOutbox<IInvoicingDbContext> _outbox;
    private readonly TimeProvider _clock;
    private readonly ILogger<EmailNotificationSentEventKafkaHandler> _logger;

    public EmailNotificationSentEventKafkaHandler(
        IInvoicingDbContext db,
        ITransactionalOutbox<IInvoicingDbContext> outbox,
        TimeProvider clock,
        ILogger<EmailNotificationSentEventKafkaHandler> logger)
    {
        _db = db;
        _outbox = outbox;
        _clock = clock;
        _logger = logger;
    }

    public async Task Handle(IMessageContext context, EmailNotificationSentEvent message)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(message);

        if (!message.TemplateId.StartsWith(InvoicingPrefix, StringComparison.Ordinal))
        {
            return;
        }

        if (!string.Equals(message.TemplateId, InvoiceDeliveredTemplate, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Unknown invoicing-prefixed template '{TemplateId}'; ignoring.",
                message.TemplateId);
            return;
        }

        if (!TryParseInvoiceIdFromIdempotencyKey(message.IdempotencyKey, out var invoiceId))
        {
            throw new DataIntegrityException(
                "Invoicing.MalformedDeliveryIdempotencyKey",
                $"Cannot parse InvoiceId from IdempotencyKey '{message.IdempotencyKey}'.");
        }

        var token = context.ConsumerContext.WorkerStopped;

        await _outbox.Database.EnsureTransactionAsync(async () =>
        {
            var invoice = await _db.Invoices.SingleOrDefaultAsync(i => i.Id == invoiceId, token)
                ?? throw new DataIntegrityException(
                    "Invoicing.InvoiceUnknownOnDeliveryConfirmation",
                    $"No invoice for id '{invoiceId}'.");

            var deliverResult = invoice.Deliver(_clock.GetUtcNow());
            if (deliverResult.IsFailed)
            {
                _logger.LogWarning(
                    "Invoice.Deliver no-op for {InvoiceId}: {Errors}",
                    invoiceId,
                    string.Join("; ", deliverResult.Errors.Select(e => e.Message)));
                return;
            }

            await _db.SaveChangesAsync(token);
        }, token);
    }

    private static bool TryParseInvoiceIdFromIdempotencyKey(string key, out Guid id)
    {
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(key)) return false;
        // "invoice-delivered-{guid}-{attempt}"
        // Split by '-' yields 7 parts because the guid itself has 4 hyphens. Strip the
        // prefix and the trailing "-{attempt}" then parse the middle as a Guid.
        const string prefix = "invoice-delivered-";
        if (!key.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var rest = key.AsSpan(prefix.Length);
        var lastDash = rest.LastIndexOf('-');
        if (lastDash < 0) return false;
        return Guid.TryParse(rest[..lastDash], out id);
    }
}
```

- [ ] **Step 3: Register in DI + add to KafkaFlow consumer**

In the Invoicing messaging DI file: add a new `.AddConsumer` block for `notifications.email-events` mirroring the Ordering/Payments consumers Invoicing already runs. Subscribe with `.AddInbox(typeof(EmailNotificationSentEvent))` and `.AddHandler<EmailNotificationSentEventKafkaHandler>()`.

- [ ] **Step 4: Run integration tests**

```bash
unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet test test/Invoicing.IntegrationTests/Invoicing.IntegrationTests.csproj
```

- [ ] **Step 5: Commit**

```bash
git add services/Invoicing/Invoicing.Application/Messaging/EmailNotificationSentEventKafkaHandler.cs \
        services/Invoicing/Invoicing.Application/Common/*DependencyInjection*.cs \
        test/Invoicing.IntegrationTests/Messaging/Kafka/EmailNotificationSentEventKafkaHandlerTests.cs
git commit -m "feat(invoicing): EmailNotificationSentEvent reciprocal consumer + KafkaFlow subscription"
```

### Task D2: Flip `DeliveryChannel.None → DeliveryChannel.Email`

**Files:**
- Modify: `services/Invoicing/Invoicing.Application/Invoices/IssueInvoice/IssueInvoiceCommandHandler.cs:159`

- [ ] **Step 1: Edit the line**

Replace:
```csharp
var deliveryChannel = DeliveryChannel.None; // M8 wires Email + SendEmailNotificationCommand fan-out.
```
with:
```csharp
var deliveryChannel = DeliveryChannel.Email; // M8 ships SendEmailNotificationCommand fan-out via InvoiceDeliveryRequestedOutboxPublisher.
```

- [ ] **Step 2: Re-run all Invoicing test slices**

```bash
unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && \
dotnet test test/Invoicing.UnitTests/Invoicing.UnitTests.csproj && \
dotnet test test/Invoicing.IntegrationTests/Invoicing.IntegrationTests.csproj
```

If the existing `IssueInvoiceCommandHandlerTests` now fails because it expects `DeliveryChannel.None`, update the assertion to `Email` (or to a new assertion that also checks the new outbox row — see Task E1).

- [ ] **Step 3: Commit**

```bash
git add services/Invoicing/Invoicing.Application/Invoices/IssueInvoice/IssueInvoiceCommandHandler.cs
git commit -m "feat(invoicing)!: flip IssueInvoice DeliveryChannel.None -> Email (M8 wired)"
```

---

## Phase E — Tests + close-out

### Task E1: Update `IssueInvoiceCommandHandlerTests` to assert the new outbox row

**Files:**
- Modify: `test/Invoicing.IntegrationTests/Application/IssueInvoiceCommandHandlerTests.cs`

- [ ] **Step 1: Extend `Example_1_1_HappyPath_IssuesInvoice_...` test**

After the existing outbox-row assertion for `InvoiceIssuedEvent`, add an assertion for a `SendEmailNotificationCommand` row on the `notifications.email-commands` topic with the expected `ViewInvoiceUrl`:

```csharp
// New: SendEmailNotificationCommand row also written in the same EF transaction.
var emailCommandRow = await db.Set<Platform.ReliableMessaging.Outbox.EFCore.OutboxMessage>()
    .AsNoTracking()
    .Where(r => r.Topic == "notifications.email-commands" && r.PartitionKey == buyerId.ToString())
    .SingleAsync(ct);
emailCommandRow.Payload.Should().Contain("invoicing.invoice-delivered");
emailCommandRow.Payload.Should().Contain($"invoice-delivered-{invoiceId}-1");
emailCommandRow.Payload.Should().Contain("ViewInvoiceUrl");
// Portal URL built from BuyerPortalOptions.BaseUrl (configured in IntegrationTests/appsettings.json).
emailCommandRow.Payload.Should().Contain($"/invoices/{invoiceId}");
```

(Adjust to your actual `OutboxMessage` table shape; if payload is binary Avro rather than JSON, deserialize via `IUniversalAvroSerializer` before asserting.)

- [ ] **Step 2: Update existing assertion on `Status`/`DeliveryChannel` if it expected `.None`**

Find any `invoice.DeliveryChannel.Should().Be(DeliveryChannel.None)` and change to `Email`.

- [ ] **Step 3: Run**

```bash
unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet test test/Invoicing.IntegrationTests/Invoicing.IntegrationTests.csproj
```

- [ ] **Step 4: Commit**

```bash
git add test/Invoicing.IntegrationTests/Application/IssueInvoiceCommandHandlerTests.cs
git commit -m "test(invoicing): assert SendEmailNotificationCommand outbox row in IssueInvoice happy path"
```

### Task E2: End-to-end `InvoiceDeliveryFlowTests`

**Files:**
- Create: `test/Invoicing.IntegrationTests/EndToEnd/InvoiceDeliveryFlowTests.cs`

- [ ] **Step 1: Write the test — "from IssueInvoice to InvoiceDeliveredEvent outbox row"**

```csharp
using AwesomeAssertions;
using FluentAssertions.Execution;
using Invoicing.Application.Common.Data;
using Invoicing.Application.Invoices.IssueInvoice;
using Invoicing.Application.Messaging;
using Invoicing.Domain.Invoices;
using Invoicing.IntegrationTests.Common;
using KafkaFlow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Email;
using NSubstitute;
using Platform.CQRS;
using Xunit;

namespace Invoicing.IntegrationTests.EndToEnd;

[Collection(nameof(IntegrationTestCollection))]
public sealed class InvoiceDeliveryFlowTests
{
    private readonly IntegrationTestFixture _fixture;

    public InvoiceDeliveryFlowTests(IntegrationTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task IssueInvoice_To_InvoiceDeliveredEvent_RoundTrips_WithSimulatedNotificationsAck()
    {
        var ct = TestContext.Current.CancellationToken;
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var paymentId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();

        await _fixture.SeedConvergedPendingInvoiceAsync(correlationId, orderId, paymentId, buyerId, 152.00m, "EUR", ct);

        // 1) IssueInvoice
        await using var s1 = _fixture.CreateScope();
        var handler = s1.ServiceProvider.GetRequiredService<ICommandHandler<IssueInvoiceCommand, Guid>>();
        var issueResult = await handler.HandleAsync(new IssueInvoiceCommand { CorrelationId = correlationId }, ct);
        issueResult.IsSuccess.Should().BeTrue();
        var invoiceId = issueResult.Value;

        // 2) Assert: InvoiceIssuedEvent + SendEmailNotificationCommand outbox rows
        await using var s2 = _fixture.CreateScope();
        var db = s2.ServiceProvider.GetRequiredService<IInvoicingDbContext>();
        var initialRows = await db.Set<Platform.ReliableMessaging.Outbox.EFCore.OutboxMessage>()
            .AsNoTracking().ToListAsync(ct);
        initialRows.Should().Contain(r => r.Topic == "invoicing.invoices" && r.Payload.Contains("InvoiceIssuedEvent"));
        initialRows.Should().Contain(r => r.Topic == "notifications.email-commands");

        // 3) Simulate Notifications BC ack by directly dispatching EmailNotificationSentEvent
        //    to Invoicing's reciprocal consumer (in-process; no real Kafka round-trip).
        await using var s3 = _fixture.CreateScope();
        var reciprocal = s3.ServiceProvider.GetRequiredService<EmailNotificationSentEventKafkaHandler>();
        var ctx = TestKafkaMessageContext.Create();
        await reciprocal.Handle(ctx, new EmailNotificationSentEvent
        {
            UserId = buyerId.ToString(),
            TemplateId = "invoicing.invoice-delivered",
            IdempotencyKey = $"invoice-delivered-{invoiceId}-1",
            SentAtUtc = DateTime.UtcNow,
            OccurredOnUtc = DateTime.UtcNow,
        });

        // 4) Assert: invoice in Delivered + InvoiceDeliveredEvent outbox row
        using var _ = new AssertionScope();
        await using var s4 = _fixture.CreateScope();
        var db4 = s4.ServiceProvider.GetRequiredService<IInvoicingDbContext>();
        var invoice = await db4.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId, ct);
        invoice.Status.Should().Be(InvoiceStatus.Delivered);

        var finalRows = await db4.Set<Platform.ReliableMessaging.Outbox.EFCore.OutboxMessage>()
            .AsNoTracking().ToListAsync(ct);
        finalRows.Should().Contain(r =>
            r.Topic == "invoicing.invoices" && r.Payload.Contains("InvoiceDeliveredEvent"));
    }
}
```

- [ ] **Step 2: Run**

```bash
unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet test test/Invoicing.IntegrationTests/Invoicing.IntegrationTests.csproj
```

- [ ] **Step 3: Commit**

```bash
git add test/Invoicing.IntegrationTests/EndToEnd/InvoiceDeliveryFlowTests.cs
git commit -m "test(invoicing): end-to-end IssueInvoice -> InvoiceDeliveredEvent flow"
```

### Task E3: Final full-solution sweep

- [ ] **Step 1: Format gates**

```bash
dotnet format whitespace --no-restore --verify-no-changes
dotnet format style --no-restore --verify-no-changes
```

If either reports diffs, apply (drop `--verify-no-changes`) and re-verify.

- [ ] **Step 2: Solution build (CS9035 defense — see CLAUDE.md)**

```bash
dotnet build -m
```

- [ ] **Step 3: All test slices, all green**

```bash
unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && \
dotnet test test/Invoicing.UnitTests/Invoicing.UnitTests.csproj && \
dotnet test test/Invoicing.ArchitectureTests/Invoicing.ArchitectureTests.csproj && \
dotnet test test/Invoicing.IntegrationTests/Invoicing.IntegrationTests.csproj && \
dotnet test test/Invoicing.FunctionalTests/Invoicing.FunctionalTests.csproj && \
dotnet test test/Notifications.UnitTests/Notifications.UnitTests.csproj && \
dotnet test test/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj
```

Expected: 6/6 slices green.

- [ ] **Step 4: Commit format-only fixes if any**

```bash
git status
# If any format-only diff lingered, stage + commit.
git commit -am "chore(format): apply format sweep after delivery-flow landing"
```

### Task E4: Close issues + write session summary

- [ ] **Step 1: Capture commit refs**

```bash
git log --oneline aaqwdqwd ^origin/aaqwdqwd  # newest first; copy SHAs
```

- [ ] **Step 2: Close #123**

```bash
gh issue close 123 -c "Resolved by branch aaqwdqwd: shipped InvoiceDeliveredEvent.avsc + InvoiceDeliveredOutboxPublisher (commit <sha>), InvoiceDeliveryRequestedOutboxPublisher + SendEmailNotificationCommand pipeline through Notifications BC (commits <shas>), reciprocal EmailNotificationSentEventKafkaHandler (commit <sha>), and flipped DeliveryChannel.None -> Email in IssueInvoiceCommandHandler.cs:159 (commit <sha>)."
```

- [ ] **Step 3: Close #131**

```bash
gh issue close 131 -c "Resolved by branch aaqwdqwd: PdfBlobRef.BlobUri -> BlobName (commit <sha>); InvoiceIssuedEvent.avsc field renamed PdfBlobUri -> PdfBlobName (breaking change accepted per non-production reference-repo deviation from ADR-0007 FORWARD_TRANSITIVE policy, commit <sha>); query handlers now mint SAS from BlobName via IBlobStore.GetSasUrlAsync (commit <sha>)."
```

- [ ] **Step 4: Write session summary**

Create `docs/implementation-prompts/session-summaries/invoicing-delivery-flow.md` documenting:
- TL;DR table of commits + their issue refs
- Phase-by-phase summary
- CI gate output
- Any deferred Phase-2 follow-ups (file as new GitHub issues with `needs-triage`)

- [ ] **Step 5: Commit session summary**

```bash
git add docs/implementation-prompts/session-summaries/invoicing-delivery-flow.md
git commit -m "docs(invoicing): session summary — delivery flow + #123/#131 closeout"
```

---

## Self-review checklist (run before claiming complete)

- [ ] All 6 test slices green (Phase E3 step 3)
- [ ] `dotnet format` clean on whitespace + style
- [ ] `dotnet build -m` succeeds
- [ ] Issues #123 + #131 closed with PR/commit refs
- [ ] No `DeliveryChannel.None` left in production code (only in test cases that explicitly assert the legacy path, if any)
- [ ] No `PdfBlobRef.BlobUri` references anywhere (`grep -rn "BlobUri" services/Invoicing/`)
- [ ] No SAS URLs in any `SendEmailNotificationCommand` payload (grep test fixtures + assertion strings)
- [ ] Spec referenced in the plan header is unchanged or has matching updates

## Phase-2 follow-ups (file as new issues with `needs-triage`)

These are out-of-scope per the spec; file as separate issues at close-out so they're not lost:

1. Real `IEmailGateway` implementation (SendGrid/SES/SMTP).
2. Template store (database-backed templates).
3. Multi-channel delivery (SMS, InApp) — schema enum supports them; consumer logic doesn't.
4. Magic-link / passwordless portal URL for unauthenticated B2C buyers.
5. Re-delivery retry policy beyond KafkaFlow default (attempt counting → eventual user-visible failure state).
