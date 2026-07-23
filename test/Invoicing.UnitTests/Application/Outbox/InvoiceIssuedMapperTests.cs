using Invoicing.Application.Outbox;
using Invoicing.Domain.Common.ValueObjects;
using Invoicing.Domain.Invoices.Events;
using Invoicing.Domain.Invoices.ValueObjects;
using Platform.SharedKernel.ValueObjects;

namespace Invoicing.UnitTests.Application.Outbox;

/// <summary>
/// Field-level mapping guard for <see cref="InvoiceIssuedMapper"/> — every non-money field, the
/// nested billing-address record, and the VAT-line collection's shape (order, cardinality, the
/// non-null empty case). The monetary <em>values and scales</em> are owned by
/// <see cref="OutboxMoneyMappingTests"/>; this file deliberately does not re-assert them, so a
/// scale/precision regression fails there and a field-routing regression fails here.
/// </summary>
public sealed class InvoiceIssuedMapperTests
{
    private static readonly DateTimeOffset IssueDate = new(2026, 4, 24, 9, 15, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OccurredOn = new(2026, 4, 24, 9, 15, 3, TimeSpan.Zero);

    [Fact]
    public void ToInvoiceIssuedEvent_MapsScalarAndReferenceFields()
    {
        // Arrange — every id/instant is distinct so a field cross-wire (e.g. IssueDate↔OccurredOnUtc,
        // OrderId↔PaymentId) flips an assertion; the three Pdf fields carry unrelated values so a
        // blob-field transposition is caught.
        var invoiceId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var paymentId = Guid.CreateVersion7();
        var pdf = PdfBlobRef.Create("2026/04/INV-2026-000142.pdf", new string('b', 64), sizeBytes: 4096).Value;

        var domainEvent = NewEvent(
            invoiceId: invoiceId,
            buyerId: buyerId,
            orderId: orderId,
            paymentId: paymentId,
            invoiceNumber: InvoiceNumber.Create(2026, 142).Value,
            issueDate: IssueDate,
            occurredOnUtc: OccurredOn,
            deliveryChannel: DeliveryChannel.TaxAuthorityWebhook,
            pdfBlobRef: pdf);

        // Act
        var avro = domainEvent.ToInvoiceIssuedEvent();

        // Assert
        using (new AssertionScope())
        {
            avro.InvoiceId.Should().Be(invoiceId);
            avro.InvoiceNumber.Should().Be("INV-2026-000142");
            avro.BuyerId.Should().Be(buyerId);
            avro.OrderId.Should().Be(orderId);
            avro.PaymentId.Should().Be(paymentId);
            avro.IssueDate.Should().Be(IssueDate.UtcDateTime);
            avro.DeliveryChannel.Should().Be(DeliveryChannel.TaxAuthorityWebhook.Name);
            avro.PdfBlobName.Should().Be("2026/04/INV-2026-000142.pdf");
            avro.PdfContentHash.Should().Be(new string('b', 64));
            avro.PdfSizeBytes.Should().Be(4096);
            avro.OccurredOnUtc.Should().Be(OccurredOn.UtcDateTime);
        }
    }

    [Fact]
    public void ToInvoiceIssuedEvent_MapsBillingAddressWithAllLinesPopulated()
    {
        // Arrange — all six lines distinct and non-null: proves the optional lines (Street2/State)
        // are actually wired (not hard-nulled) and catches a Street1↔Street2 swap.
        var address = Address.Create("221B Baker Street", "Flat 2", "London", "Greater London", "NW1 6XE", "GB").Value;
        var domainEvent = NewEvent(billingAddress: address);

        // Act
        var avro = domainEvent.ToInvoiceIssuedEvent();

        // Assert
        using (new AssertionScope())
        {
            avro.BillingAddress.Street1.Should().Be("221B Baker Street");
            avro.BillingAddress.Street2.Should().Be("Flat 2");
            avro.BillingAddress.City.Should().Be("London");
            avro.BillingAddress.State.Should().Be("Greater London");
            avro.BillingAddress.PostalCode.Should().Be("NW1 6XE");
            avro.BillingAddress.CountryCode.Should().Be("GB");
        }
    }

    [Fact]
    public void ToInvoiceIssuedEvent_PreservesNullBillingAddressOptionalLines()
    {
        // Arrange — Street2/State are null on a schema that allows null; the mapper must pass them
        // through as null rather than coalescing to "" or dropping the field.
        var address = Address.Create("742 Evergreen Terrace", null, "Springfield", null, "49007", "US").Value;
        var domainEvent = NewEvent(billingAddress: address);

        // Act
        var avro = domainEvent.ToInvoiceIssuedEvent();

        // Assert
        using (new AssertionScope())
        {
            avro.BillingAddress.Street1.Should().Be("742 Evergreen Terrace");
            avro.BillingAddress.Street2.Should().BeNull();
            avro.BillingAddress.City.Should().Be("Springfield");
            avro.BillingAddress.State.Should().BeNull();
            avro.BillingAddress.PostalCode.Should().Be("49007");
            avro.BillingAddress.CountryCode.Should().Be("US");
        }
    }

    [Fact]
    public void ToInvoiceIssuedEvent_MapsVatLinesPreservingOrderAndCardinality()
    {
        // Arrange — two lines at different rates, with values deliberately distinct from the single
        // line OutboxMoneyMappingTests pins (21% / 200 / 42) so no assertion here re-kills a money
        // mutant that slice owns. Per-line Base ≠ Amount catches a BaseAmount↔Amount transposition;
        // the distinct per-line rates plus the count catch a dropped, duplicated, or reordered line.
        // Scale correctness stays with OutboxMoneyMappingTests, so only values are checked here.
        var vatLines = new List<VatLine>
        {
            VatLine.Create(VatRate.Create(9.00m).Value, Eur(150.00m), Eur(13.50m)),
            VatLine.Create(VatRate.Create(12.50m).Value, Eur(80.00m), Eur(10.00m)),
        };
        var domainEvent = NewEvent(vatLines: vatLines);

        // Act
        var avro = domainEvent.ToInvoiceIssuedEvent();

        // Assert
        using (new AssertionScope())
        {
            avro.VatLines.Should().HaveCount(2);

            ((decimal)avro.VatLines[0].Rate).Should().Be(9.00m);
            ((decimal)avro.VatLines[0].BaseAmount).Should().Be(150.00m);
            ((decimal)avro.VatLines[0].Amount).Should().Be(13.50m);

            ((decimal)avro.VatLines[1].Rate).Should().Be(12.50m);
            ((decimal)avro.VatLines[1].BaseAmount).Should().Be(80.00m);
            ((decimal)avro.VatLines[1].Amount).Should().Be(10.00m);
        }
    }

    [Fact]
    public void ToInvoiceIssuedEvent_WithNoVatLines_EmitsNonNullEmptyList()
    {
        // Arrange — an empty VAT-line collection (the mapper filters nothing; it maps whatever it is
        // given). The schema field is non-null, so the mapper's .Select().ToList() must surface an
        // empty list, never null.
        var domainEvent = NewEvent(vatLines: []);

        // Act
        var avro = domainEvent.ToInvoiceIssuedEvent();

        // Assert
        avro.VatLines.Should().NotBeNull().And.BeEmpty();
    }

    private static Money Eur(decimal amount) => Money.Create(amount, CurrencyCode.Eur).Value;

    /// <summary>
    /// Builds a fully-valid <see cref="InvoiceIssuedDomainEvent"/>, letting each test override only
    /// the axis it asserts. Money fields are fixed here (their mapping is covered elsewhere).
    /// </summary>
    private static InvoiceIssuedDomainEvent NewEvent(
        Guid? invoiceId = null,
        Guid? buyerId = null,
        Guid? orderId = null,
        Guid? paymentId = null,
        InvoiceNumber? invoiceNumber = null,
        DateTimeOffset? issueDate = null,
        DateTimeOffset? occurredOnUtc = null,
        Address? billingAddress = null,
        DeliveryChannel? deliveryChannel = null,
        PdfBlobRef? pdfBlobRef = null,
        IReadOnlyList<VatLine>? vatLines = null) =>
        new()
        {
            InvoiceId = invoiceId ?? Guid.CreateVersion7(),
            InvoiceNumber = invoiceNumber ?? InvoiceNumber.Create(2026, 142).Value,
            BuyerId = buyerId ?? Guid.CreateVersion7(),
            OrderId = orderId ?? Guid.CreateVersion7(),
            PaymentId = paymentId ?? Guid.CreateVersion7(),
            IssueDate = issueDate ?? IssueDate,
            BillingAddress = billingAddress ?? Address.Create("Main Street 1", null, "Prague", null, "11000", "CZ").Value,
            Subtotal = Eur(200.00m),
            Total = Eur(242.00m),
            VatLines = vatLines ?? [VatLine.Create(VatRate.Create(21.00m).Value, Eur(200.00m), Eur(42.00m))],
            PdfBlobRef = pdfBlobRef ?? PdfBlobRef.Create("2026/04/INV-2026-000142.pdf", new string('a', 64), sizeBytes: 1024).Value,
            DeliveryChannel = deliveryChannel ?? DeliveryChannel.Email,
            OccurredOnUtc = occurredOnUtc ?? OccurredOn,
        };
}
