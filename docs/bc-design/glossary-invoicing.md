# Invoicing Bounded Context — Ubiquitous Language Glossary

> Scope: the Invoicing BC only. Terms here are how Invoicing speaks — not how Ordering, Payments, or Notifications speak, even when those BCs use the same word for a related concept. Translator boundaries are called out explicitly.

---

## Core terms

| Term | Definition |
|------|------------|
| **Invoice** | The primary aggregate in the Invoicing BC. A legally binding record that a purchase occurred at a given price on a given date, addressed to a specific buyer. Not to be confused with `Order` (Ordering BC) — an Order is the *commercial commitment*; an Invoice is the *fiscal record* that follows after the money actually moved. |
| **CreditNote** | The second aggregate. A negative-amount counterpart to an Invoice, issued when a captured payment is refunded. Retains its own sequence (`CN-YYYY-NNNNNN`). A credit note cannot itself be cancelled. |
| **InvoiceNumber** | Gap-free, year-scoped sequential identifier formatted `INV-YYYY-NNNNNN` (e.g., `INV-2026-000142`). Allocated via the `invoice_number_allocator` table's `SELECT ... FOR UPDATE` + `UPDATE` inside the issuing transaction. |
| **CreditNoteNumber** | Gap-free sequential identifier formatted `CN-YYYY-NNNNNN`. Separate sequence from invoices. |
| **Gap-free sequencing** | The requirement (driven by EU VAT law) that invoice numbers have no missing values. Rollbacks of the issuing transaction must release the number. Implemented via an allocator table with row-level locks, not via `SEQUENCE` (which leaks gaps on rollback). |
| **InvoiceLine** | Immutable value object: a single line item on an invoice. Carries Sku, description, quantity, unit price, VAT rate, line total. Frozen at issuance. |
| **VatLine** | Value object per VAT rate: `{ Rate, Base, Amount }`. An invoice with mixed-rate items produces one VatLine per rate. |
| **VatRate** | Value object wrapping a percentage (0..100). Not a SmartEnum — rates vary by jurisdiction and change over time. |
| **Subtotal** | Sum of `InvoiceLine.LineTotal` across all lines, pre-tax. |
| **Total** | Subtotal + sum of VatLine amounts. The amount paid by the buyer. |
| **BillingAddress** | Address snapshotted from `OrderConfirmedEvent` at issue time. Frozen; the Invoice aggregate does not update when the buyer later changes their address. |

---

## Lifecycle / state terms

| Term | Definition |
|------|------------|
| **Draft** | Transient state: the aggregate exists but `InvoiceNumber` not yet allocated and PDF not yet stored. Brief window within a single command handler; rare to observe in production. Retained as a state so partial failures (PDF generated but blob upload failed) are resumable. |
| **Issued** | Terminal happy state: number allocated, PDF stored, aggregate persisted. `InvoiceIssuedEvent` fired. |
| **Delivered** | Follow-on state: delivery channel confirmed receipt (email accepted by SMTP — additional channels are planned scope; see [roadmap.md § 2.3 Invoicing](../roadmap.md)). Does not affect legal validity — issuance is the legal moment. |
| **Archived** | Terminal state reached via `Invoice.Archive()` (`Delivered → Archived`), held for long-term retention. The aggregate transition exists; the background job that drives it and moves PDFs to cold storage after N years is planned scope — see [roadmap.md § 2.3 Invoicing](../roadmap.md). |
| **Cancelled** | Off-ramp: the invoice is reversed by issuing a CreditNote. Requires `CancellationInfo.CreditNoteId` to be populated — an invoice cannot be cancelled without a corresponding credit note. |

---

## Issuance & enrichment terms

| Term | Definition |
|------|------------|
| **Enrichment projection** | The `pending_invoices` table that buffers partial state. When a consumer observes `OrderConfirmedEvent`, it writes the order half; when `PaymentCapturedEvent` arrives, it writes the payment half. When both halves are present, `IssueInvoiceCommand` fires. Pattern generalizes to any async multi-source composition. |
| **IssuanceReady** | The internal domain event raised by the projection when both halves land. Triggers `IssueInvoiceCommand`. Not a Kafka event — purely in-process. |
| **Convergent enrichment** | The pattern: two or more independent upstream events that must all arrive before a downstream action can occur. Invoicing is the canonical example in this solution. A saga would be overkill for this; projection + state machine is simpler. |
| **Gateway** *(not used)* | Invoicing has no gateway — it writes PDFs to an object store, not money to a bank. "Gateway" in this solution refers exclusively to the Payments BC's payment gateway. |

---

## Delivery terms

| Term | Definition |
|------|------------|
| **DeliveryChannel** | SmartEnum: `None`, `Email`, `TaxAuthorityWebhook`. v1 delivers on `Email` only; the `TaxAuthorityWebhook` value exists but its delivery behavior — plus a future `PostalMail` channel — is planned scope, see [roadmap.md § 2.3 Invoicing](../roadmap.md). |
| **DeliveryAttempt** | Monotonic counter per `(InvoiceId, Channel)`. Each resend increments. Recorded in `invoice_delivery_log`. |
| **SAS URL** (Shared Access Signature) | A time-bounded (10-minute) Azure Blob URL that grants GET access to the PDF without exposing storage account credentials. Served through nginx-cdn locally / Azure Front Door in production for CDN semantics. |
| **Write-once blob** | The guarantee that once a PDF is uploaded to Azurite/Azure Blob, its content never changes. Enforced by Azure immutable-blob policy (10-year time-based retention). The content hash is stored on the aggregate's `PdfBlobRef`. |
| **Azurite** | Microsoft's open-source Azure Storage emulator. The reference solution uses it locally for blob storage; production swaps to a real Azure Blob Storage account via Aspire's `AddAzureStorage("storage").RunAsEmulator()` integration (see [ADR-0017](../adr/0017-blob-storage-cdn.md)). |

---

## PDF / document terms

| Term | Definition |
|------|------------|
| **QuestPDF** | The MIT-licensed fluent-DSL PDF library Invoicing uses. Declarative — you describe the document composition, the library emits the PDF. Teaching choice over PDFsharp (which is imperative); real-world it's competitive with cloud PDF services. |
| **InvoiceDocument** | The `IDocument` implementation (in `Invoicing.Infrastructure.Pdf`) that composes the header, buyer block, line-item table, VAT summary, total, and footer from `Invoice` aggregate state. |
| **Legal footer** | Configurable text (tax ID, registered office, bank details) placed at the bottom of every invoice PDF. V1 loads from `appsettings.json`. Not modelled as domain state; it's a presentation-layer concern. |

---

## Cross-BC disambiguation

| Term | In Invoicing | In other BCs |
|------|--------------|--------------|
| **Order** | A reference (`OrderId`) — Invoicing knows nothing of order lifecycle, just that an order was confirmed with certain line items and a total | Ordering owns the `Order` aggregate with its 8-state FSM |
| **Payment** | A reference (`PaymentId`) — Invoicing waits for `PaymentCapturedEvent` | Payments owns the `PaymentTransaction` aggregate |
| **Buyer** | `BuyerId` + snapshotted `BillingAddress` at issuance time | Ordering's `Order.BuyerId` is the source of truth; Invoicing snapshots it so later buyer-profile changes don't modify issued invoices |
| **Line** | `InvoiceLine` — fiscal record row with VAT rate | Ordering: `OrderItem` — commercial line item; Basket: `BasketItem` — mutable pre-checkout |
| **Number** | `InvoiceNumber` / `CreditNoteNumber` — gap-free, year-scoped | No other BC uses numeric document IDs — Ordering uses `OrderId : Guid`, not a number |
| **Cancel** | `Invoice → Cancelled` requires a CreditNote to reverse fiscal state | Ordering's `OrderCancelledEvent` is what *triggers* Invoicing's credit-note path |
| **Archive** | A lifecycle state (`InvoiceStatus.Archived`) | Not a concept elsewhere; retention in Kafka (10 years) is orthogonal |

---

## Deliberately omitted from current scope

Planned extensions are catalogued in [roadmap.md § 2.3 Invoicing](../roadmap.md):

| Term | Reason |
|------|--------|
| **Partial credit note** | Refunds today are full-amount, so credit notes mirror the full invoice |
| **Recurring invoice** | No subscription semantics |
| **Multi-currency invoice** | Matches Order single-currency constraint |
| **Tax-authority webhook** | Slot in `DeliveryChannel` but no implementation yet |
| **Invoice correction (not via credit note)** | Some jurisdictions allow amending an invoice without issuing a credit note. Current scope is strict: corrections are credit note + new invoice |
| **Regional sequence formats** (Czech tax ID patterns, German formal requirements) | Today uses a single global pattern |

---

*End of Invoicing glossary — 32 terms defined.*
