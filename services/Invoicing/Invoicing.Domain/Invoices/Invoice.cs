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
/// configuration in M5); no explicit <c>Version</c> property \u2014 following Ordering precedent.
/// </remarks>
public sealed class Invoice : AggregateRoot<Guid>
{
    private readonly List<InvoiceLine> _lines = [];
    private readonly List<VatLine> _vatLines = [];

    public InvoiceNumber? InvoiceNumber { get; private set; }

    public Guid BuyerId { get; private set; }

    public Guid OrderId { get; private set; }

    public Guid PaymentId { get; private set; }

    public Guid CorrelationId { get; private set; }

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
        Guid correlationId,
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
        Throw.If(correlationId == Guid.Empty, new DataIntegrityException(
            "Invoicing.InvalidCorrelationId", "Invoice CorrelationId must not be empty."));

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

        var invoice = new Invoice
        {
            Id = Guid.CreateVersion7(),
            BuyerId = buyerId,
            OrderId = orderId,
            PaymentId = paymentId,
            CorrelationId = correlationId,
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
            CorrelationId = correlationId,
            OccurredOnUtc = utcNow,
        });

        return Result.Ok(invoice);
    }

    /// <summary>
    /// Stamps the gap-free <see cref="InvoiceNumber"/> on a <see cref="InvoiceStatus.Draft"/>
    /// invoice without raising domain events or transitioning state. Used by the M7
    /// command handler to assign the number BEFORE PDF rendering \u2014 the renderer reads
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
    /// <see cref="Issue(PdfBlobRef, DateTimeOffset)"/>. Use the split form in M7's command
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
            CorrelationId = CorrelationId,
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
            AddDomainEvent(new InvoiceDeliveryRequestedDomainEvent
            {
                InvoiceId = Id,
                BuyerId = BuyerId,
                Channel = DeliveryChannel,
                Attempt = 1,
                CorrelationId = CorrelationId,
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
            CorrelationId = CorrelationId,
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

        CancellationInfo = new CancellationInfo(utcNow, reason, creditNoteId);
        Status = InvoiceStatus.Cancelled;

        AddDomainEvent(new InvoiceCancelledDomainEvent
        {
            InvoiceId = Id,
            BuyerId = BuyerId,
            CancelledAtUtc = utcNow,
            Reason = reason,
            CreditNoteId = creditNoteId,
            CorrelationId = CorrelationId,
            OccurredOnUtc = utcNow,
        });

        return Result.Ok();
    }

    /// <summary>
    /// Returns an immutable <see cref="InvoiceLine"/> snapshot suitable for building a
    /// reversing credit note (sign-flipped via <see cref="InvoiceLine.WithFlippedSign"/>).
    /// </summary>
    internal IReadOnlyList<InvoiceLine> LinesForReversal()
    {
        var flipped = new List<InvoiceLine>(_lines.Count);
        foreach (var line in _lines)
        {
            flipped.Add(line.WithFlippedSign());
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

        // I-1: Subtotal, VatLines, and Total self-consistent. Use primary Money ctor to
        // bypass Money.Create's positivity check \u2014 totals may be 0 for edge cases.
        var subtotal = new Money(subtotalAmount, currency);
        var total = new Money(totalAmount, currency);
        return (subtotal, total);
    }
}
