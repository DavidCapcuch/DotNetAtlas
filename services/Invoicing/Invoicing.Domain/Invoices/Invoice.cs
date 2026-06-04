using FluentResults;
using Invoicing.Domain.Common.Errors;
using Invoicing.Domain.Common.ValueObjects;
using Invoicing.Domain.Invoices.Events;
using Invoicing.Domain.Invoices.ValueObjects;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Exceptions;
using Platform.SharedKernel.ValueObjects;

namespace Invoicing.Domain.Invoices;

/// <summary>
/// Aggregate root \u2014 fiscal invoice issued after an order is confirmed AND its payment is
/// captured. Authority for fiscal records per [invoicing.md](../../../../docs/bc-design/invoicing.md).
/// </summary>
/// <remarks>
/// Invariants ([invoicing.md \u00a7 2.1](../../../../docs/bc-design/invoicing.md) lines 59\u201365):
/// <list type="number">
/// <item><c>I-1</c> \u2014 <c>Total == Subtotal + sum(VatLines.Amount)</c>; enforced at factory, re-asserted on read.</item>
/// <item><c>I-2</c> \u2014 <see cref="Lines"/> is non-empty.</item>
/// <item><c>I-3</c> \u2014 <see cref="InvoiceNumber"/> is immutable post-allocation.</item>
/// <item><c>I-4</c> \u2014 <see cref="PdfBlobRef"/> is immutable once set (write-once blob per ADR-0017).</item>
/// <item><c>I-5</c> \u2014 Transitions gated by <see cref="InvoiceStatus.CanTransitionTo(InvoiceStatus)"/>; invalid \u2192 <see cref="DataIntegrityException"/>.</item>
/// <item><c>I-6</c> \u2014 <see cref="InvoiceStatus.Cancelled"/> requires a <see cref="CancellationInfo.CreditNoteId"/>.</item>
/// </list>
/// Time is injected via <c>DateTimeOffset utcNow</c> on every mutating method (ADR-0015).
/// Concurrency is via Postgres <c>xmin</c> mapped to <c>Entity&lt;TId&gt;.RowVersion</c> (see EF
/// configuration); no explicit <c>Version</c> property.
/// </remarks>
public sealed class Invoice : AggregateRoot<Guid>
{
    private readonly List<InvoiceLine> _lines = [];
    private readonly List<VatLine> _vatLines = [];

    public InvoiceNumber? InvoiceNumber { get; private set; }

    public Guid BuyerId { get; private set; }

    public Guid OrderId { get; private set; }

    public Guid PaymentId { get; private set; }

    public DateTimeOffset IssueDate { get; private set; }

    public Address BillingAddress { get; private set; } = default!;

    public IReadOnlyList<InvoiceLine> Lines => _lines;

    public IReadOnlyList<VatLine> VatLines => _vatLines;

    public Money Subtotal { get; private set; } = default!;

    public Money Total { get; private set; } = default!;

    public PdfBlobRef? PdfBlobRef { get; private set; }

    public DeliveryChannel DeliveryChannel { get; private set; } = DeliveryChannel.None;

    public InvoiceStatus Status { get; private set; } = InvoiceStatus.Draft;

    public CancellationInfo? CancellationInfo { get; private set; }

    public DateTimeOffset? DeliveredAtUtc { get; private set; }

    /// <summary>
    /// NotificationId (GUID v7) minted when delivery is requested on
    /// <see cref="Issue(PdfBlobRef, DateTimeOffset)"/> — the producer-assigned correlation key
    /// (ADR-0031) the delivery confirmation echoes back. Null until the invoice is issued with a
    /// non-<see cref="DeliveryChannel.None"/> delivery channel.
    /// </summary>
    public Guid? DeliveryNotificationId { get; private set; }

    private Invoice()
    {
    }

    /// <summary>
    /// Creates a new <c>Draft</c> invoice with lines, VAT breakdown, and billing address
    /// snapshotted from the enrichment projection. Raises
    /// <see cref="InvoiceCreatedDomainEvent"/>. The invoice number is allocated separately
    /// (ADR-0018) and stamped by <see cref="Issue"/>.
    /// </summary>
    /// <remarks>
    /// Enforces I-1 (<c>Total == Subtotal + \u03a3 VatLines.Amount</c>) and I-2 (lines non-empty).
    /// Mismatches throw <see cref="DataIntegrityException"/> \u2014 the caller guarantees totals
    /// by the time enrichment fires.
    /// </remarks>
    public static Result<Invoice> Create(
        Guid buyerId,
        Guid orderId,
        Guid paymentId,
        Address billingAddress,
        IReadOnlyList<InvoiceLine> lines,
        IReadOnlyList<VatLine> vatLines,
        DeliveryChannel deliveryChannel,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(billingAddress);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(vatLines);
        ArgumentNullException.ThrowIfNull(deliveryChannel);

        Throw.If(buyerId == Guid.Empty, new DataIntegrityException(
            "Invoicing.InvalidBuyerId", "Invoice BuyerId must not be empty."));
        Throw.If(orderId == Guid.Empty, new DataIntegrityException(
            "Invoicing.InvalidOrderId", "Invoice OrderId must not be empty."));
        Throw.If(paymentId == Guid.Empty, new DataIntegrityException(
            "Invoicing.InvalidPaymentId", "Invoice PaymentId must not be empty."));

        // I-2: Lines non-empty.
        if (lines.Count == 0)
        {
            throw new DataIntegrityException(
                "Invoicing.EmptyLines", "Invoice must have at least one line (I-2).");
        }

        // All lines share the same currency.
        var currency = lines[0].UnitPrice.Currency;
        for (var i = 1; i < lines.Count; i++)
        {
            if (lines[i].UnitPrice.Currency != currency)
            {
                throw new DataIntegrityException(
                    "Invoicing.MixedCurrency",
                    $"Invoice lines must share a single currency; line {lines[i].LineNumber} differs.");
            }
        }

        var (subtotal, total) = ComputeTotals(lines, vatLines, currency);

        // Defense-in-depth: by construction subtotal/total are non-negative (lines.Count >= 1,
        // InvoiceLine.UnitPrice > 0, VatRate >= 0). Untestable through valid call paths;
        // documents the invariant for refactor safety after the School-B Money sign-neutrality.
        Throw.If(subtotal.Amount < 0, new DataIntegrityException(
            "Invoicing.InvoiceSubtotalNegative",
            $"Invoice subtotal must be non-negative; was {subtotal.Amount} {subtotal.Currency.Name}."));
        Throw.If(total.Amount < 0, new DataIntegrityException(
            "Invoicing.InvoiceTotalNegative",
            $"Invoice total must be non-negative; was {total.Amount} {total.Currency.Name}."));

        var invoice = new Invoice
        {
            Id = Guid.CreateVersion7(),
            BuyerId = buyerId,
            OrderId = orderId,
            PaymentId = paymentId,
            BillingAddress = billingAddress,
            Subtotal = subtotal,
            Total = total,
            DeliveryChannel = deliveryChannel,
            Status = InvoiceStatus.Draft,
        };

        invoice._lines.AddRange(lines);
        invoice._vatLines.AddRange(vatLines);

        invoice.AddDomainEvent(new InvoiceCreatedDomainEvent
        {
            InvoiceId = invoice.Id,
            BuyerId = buyerId,
            OrderId = orderId,
            OccurredOnUtc = utcNow,
        });

        return Result.Ok(invoice);
    }

    /// <summary>
    /// Stamps the gap-free <see cref="InvoiceNumber"/> on a <see cref="InvoiceStatus.Draft"/>
    /// invoice without raising domain events or transitioning state. Used by the command
    /// handler to assign the number BEFORE PDF rendering \u2014 the renderer reads
    /// <see cref="InvoiceNumber"/> off the aggregate, so it must be present at render time
    /// even though <see cref="PdfBlobRef"/> only lands after upload (chicken-and-egg
    /// resolved by splitting the stamp + the issue transition). Once assigned the value
    /// is immutable per I-3.
    /// </summary>
    public void AssignInvoiceNumber(InvoiceNumber invoiceNumber)
    {
        ArgumentNullException.ThrowIfNull(invoiceNumber);

        if (Status != InvoiceStatus.Draft)
        {
            throw new DataIntegrityException(
                "Invoicing.AssignInvoiceNumberOutOfDraft",
                $"InvoiceNumber can only be assigned while Draft (current: {Status.Name}).");
        }

        if (InvoiceNumber is not null)
        {
            throw new DataIntegrityException(
                "Invoicing.InvoiceNumberAlreadyAssigned",
                "InvoiceNumber is immutable once assigned (I-3).");
        }

        InvoiceNumber = invoiceNumber;
    }

    /// <summary>
    /// Transitions <c>Draft \u2192 Issued</c>. Stamps the gap-free <see cref="InvoiceNumber"/>
    /// allocated by the transactional allocator and the <see cref="PdfBlobRef"/> of the
    /// uploaded PDF. Raises <see cref="InvoiceIssuedDomainEvent"/> and, when
    /// <see cref="DeliveryChannel"/> is not <see cref="DeliveryChannel.None"/>,
    /// <see cref="InvoiceDeliveryRequestedDomainEvent"/> for delivery attempt 1.
    /// </summary>
    /// <remarks>
    /// Convenience overload \u2014 composes <see cref="AssignInvoiceNumber"/> + the no-number
    /// <see cref="Issue(PdfBlobRef, DateTimeOffset)"/>. Use the split form in the command
    /// handler when the PDF must render with the number embedded.
    /// </remarks>
    public Result Issue(InvoiceNumber invoiceNumber, PdfBlobRef pdfBlobRef, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(invoiceNumber);
        ArgumentNullException.ThrowIfNull(pdfBlobRef);

        // If the aggregate has already left Draft (e.g., already Issued), the FSM rejects the
        // transition. Return Result.Fail without touching state — preserves I-3 (number
        // immutable) + I-4 (PDF immutable) by short-circuiting before any assignment.
        if (Status != InvoiceStatus.Draft)
        {
            return Status.CanTransitionTo(InvoiceStatus.Issued);
        }

        if (InvoiceNumber is null)
        {
            AssignInvoiceNumber(invoiceNumber);
        }
        else if (!InvoiceNumber.Equals(invoiceNumber))
        {
            throw new DataIntegrityException(
                "Invoicing.InvoiceNumberMismatchOnIssue",
                "InvoiceNumber passed to Issue does not match the previously-assigned number (I-3).");
        }

        return Issue(pdfBlobRef, utcNow);
    }

    /// <summary>
    /// Transitions <c>Draft \u2192 Issued</c> using the previously-assigned
    /// <see cref="InvoiceNumber"/> and the supplied PDF reference. Requires
    /// <see cref="AssignInvoiceNumber"/> to have been called.
    /// </summary>
    public Result Issue(PdfBlobRef pdfBlobRef, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(pdfBlobRef);

        if (InvoiceNumber is null)
        {
            throw new DataIntegrityException(
                "Invoicing.IssueWithoutInvoiceNumber",
                "Issue requires an InvoiceNumber \u2014 call AssignInvoiceNumber first.");
        }

        // I-4 explicit write-once guard, mirroring CreditNote.Issue. The FSM gate below
        // catches the common case (already-Issued status), but a rehydrated Draft row
        // carrying a stale pdf_blob_uri must not be silently overwritten.
        if (PdfBlobRef is not null)
        {
            throw new DataIntegrityException(
                "Invoicing.InvoiceAlreadyIssued",
                "Invoice has already been issued (PDF stamped) (I-4).");
        }

        var transition = Status.CanTransitionTo(InvoiceStatus.Issued);
        if (transition.IsFailed)
        {
            return transition;
        }

        PdfBlobRef = pdfBlobRef;
        IssueDate = utcNow;
        Status = InvoiceStatus.Issued;

        AddDomainEvent(new InvoiceIssuedDomainEvent
        {
            InvoiceId = Id,
            InvoiceNumber = InvoiceNumber,
            BuyerId = BuyerId,
            OrderId = OrderId,
            PaymentId = PaymentId,
            IssueDate = utcNow,
            BillingAddress = BillingAddress,
            Subtotal = Subtotal,
            Total = Total,
            VatLines = _vatLines.AsReadOnly(),
            PdfBlobRef = pdfBlobRef,
            DeliveryChannel = DeliveryChannel,
            OccurredOnUtc = utcNow,
        });

        if (DeliveryChannel != DeliveryChannel.None)
        {
            // Client-assigned NotificationId (ADR-0031), persisted on the aggregate in this same
            // save so the delivery confirmation correlates back by delivery_notification_id — a
            // typed field read, replacing the v1 invoice-delivered-{guid}-{attempt} string parse.
            DeliveryNotificationId = Guid.CreateVersion7();

            AddDomainEvent(new InvoiceDeliveryRequestedDomainEvent
            {
                InvoiceId = Id,
                BuyerId = BuyerId,
                NotificationId = DeliveryNotificationId.Value,
                Channel = DeliveryChannel,
                InvoiceNumber = InvoiceNumber,
                Total = Total,
                OccurredOnUtc = utcNow,
            });
        }

        return Result.Ok();
    }

    /// <summary>
    /// Transitions <c>Issued \u2192 Delivered</c>. Records the delivery instant and raises
    /// <see cref="InvoiceDeliveredDomainEvent"/>.
    /// </summary>
    public Result Deliver(DateTimeOffset deliveredAtUtc)
    {
        var transition = Status.CanTransitionTo(InvoiceStatus.Delivered);
        if (transition.IsFailed)
        {
            return transition;
        }

        Status = InvoiceStatus.Delivered;
        DeliveredAtUtc = deliveredAtUtc;

        AddDomainEvent(new InvoiceDeliveredDomainEvent
        {
            InvoiceId = Id,
            BuyerId = BuyerId,
            DeliveredAtUtc = deliveredAtUtc,
            Channel = DeliveryChannel,
            OccurredOnUtc = deliveredAtUtc,
        });

        return Result.Ok();
    }

    /// <summary>
    /// Transitions <c>Delivered \u2192 Archived</c>. No side effects beyond the state change;
    /// no external Avro event for archival.
    /// </summary>
    public Result Archive()
    {
        var transition = Status.CanTransitionTo(InvoiceStatus.Archived);
        if (transition.IsFailed)
        {
            return transition;
        }

        Status = InvoiceStatus.Archived;
        return Result.Ok();
    }

    /// <summary>
    /// Off-ramp transition to <see cref="InvoiceStatus.Cancelled"/>. Requires the reversing
    /// <paramref name="creditNoteId"/> per I-6. Raises <see cref="InvoiceCancelledDomainEvent"/>.
    /// </summary>
    public Result Cancel(Guid creditNoteId, CreditNoteReason reason, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(reason);

        Throw.If(creditNoteId == Guid.Empty, new DataIntegrityException(
            "Invoicing.InvalidCreditNoteIdOnCancel",
            "Cancellation requires a non-empty CreditNoteId (I-6)."));

        var transition = Status.CanTransitionTo(InvoiceStatus.Cancelled);
        if (transition.IsFailed)
        {
            return transition;
        }

        CancellationInfo = CancellationInfo.Create(utcNow, reason, creditNoteId);
        Status = InvoiceStatus.Cancelled;

        AddDomainEvent(new InvoiceCancelledDomainEvent
        {
            InvoiceId = Id,
            BuyerId = BuyerId,
            CancelledAtUtc = utcNow,
            Reason = reason,
            CreditNoteId = creditNoteId,
            OccurredOnUtc = utcNow,
        });

        return Result.Ok();
    }

    /// <summary>
    /// Captures the invoice state needed to issue a reversing
    /// <see cref="CreditNotes.CreditNote"/>. Invoice owns this construction so the credit
    /// note never holds a direct reference to the Invoice aggregate (DDD aggregate boundary;
    /// mirrors the <c>ProductSnapshot</c> / <c>BasketSnapshot</c> pattern in Basket↔Ordering).
    /// </summary>
    /// <remarks>
    /// Caller (the command handler) MUST gate on <see cref="InvoiceStatus"/> before
    /// invoking — snapshotting a cancelled or draft invoice is bug-class and surfaces as
    /// <see cref="DataIntegrityException"/> here. The user-actionable
    /// "credit-note refers to cancelled invoice" path stays at the handler boundary as a
    /// <c>Result.Fail</c>.
    /// </remarks>
    public InvoiceSnapshot ToReversalSnapshot(DateTimeOffset capturedAtUtc)
    {
        if (Status != InvoiceStatus.Issued && Status != InvoiceStatus.Delivered)
        {
            throw new DataIntegrityException(
                "Invoicing.SnapshotFromIneligibleInvoice",
                $"Cannot snapshot invoice in state '{Status.Name}' for reversal (must be Issued or Delivered).");
        }

        if (InvoiceNumber is null)
        {
            throw new DataIntegrityException(
                "Invoicing.SnapshotMissingInvoiceNumber",
                "Cannot snapshot invoice without an allocated InvoiceNumber.");
        }

        return InvoiceSnapshot.Create(
            invoiceId: Id,
            invoiceNumber: InvoiceNumber,
            buyerId: BuyerId,
            reversalLines: LinesForReversal(),
            total: Total,
            capturedAtUtc: capturedAtUtc);
    }

    private List<CreditNoteLine> LinesForReversal()
    {
        var flipped = new List<CreditNoteLine>(_lines.Count);
        foreach (var line in _lines)
        {
            flipped.Add(CreditNoteLine.FromInvoiceLine(line));
        }

        return flipped;
    }

    private static (Money Subtotal, Money Total) ComputeTotals(
        IReadOnlyList<InvoiceLine> lines,
        IReadOnlyList<VatLine> vatLines,
        CurrencyCode currency)
    {
        decimal subtotalAmount = 0m;
        foreach (var line in lines)
        {
            subtotalAmount += line.LineTotal.Amount;
        }

        decimal vatTotal = 0m;
        foreach (var vatLine in vatLines)
        {
            if (vatLine.Amount.Currency != currency)
            {
                throw new DataIntegrityException(
                    "Invoicing.MixedCurrency",
                    "VAT lines must share the invoice's currency.");
            }

            vatTotal += vatLine.Amount.Amount;
        }

        var totalAmount = subtotalAmount + vatTotal;

        // I-1: Subtotal, VatLines, and Total self-consistent. Money.Create no longer rejects
        // zero/negative \u2014 positivity is enforced upstream by InvoiceLine.UnitPrice > 0 and
        // VatRate >= 0, so subtotal/total are guaranteed non-negative here.
        var subtotal = Money.Create(subtotalAmount, currency).Value;
        var total = Money.Create(totalAmount, currency).Value;
        return (subtotal, total);
    }
}
