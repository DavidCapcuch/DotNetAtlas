using Avro;
using Invoicing.Application.Outbox;
using Invoicing.Domain.Common.ValueObjects;
using Invoicing.Domain.CreditNotes.Events;
using Invoicing.Domain.CreditNotes.ValueObjects;
using Invoicing.Domain.Invoices.Events;
using Invoicing.Domain.Invoices.ValueObjects;
using Platform.SharedKernel.ValueObjects;

namespace Invoicing.UnitTests.Application.Outbox;

/// <summary>
/// Guards the domain → Avro <em>money</em> mapping for the Invoicing outbox mappers. Every
/// monetary field is converted via <c>ToAvroDecimal(scale)</c>, and the .avsc pins two distinct
/// scales: 4 for money (<c>decimal(19,4)</c>) and 2 for VAT rate (<c>decimal(5,2)</c>). Two
/// failure modes: a wrong <em>scale</em> (or a money↔rate swap) is rejected loudly by the Avro
/// serializer at enqueue — but only against a live Schema Registry, so these tests localise it at
/// the fast unit tier instead; a lost sub-cent digit or a dropped credit-note sign keeps the
/// scale, serialises cleanly, and ships the wrong value — the genuinely silent corruption.
/// <see cref="AvroDecimal"/> equality compares <c>Scale</c> and the unscaled value, so both
/// classes of mutation flip an assertion here.
/// </summary>
/// <remarks>Scope is the money fields only — full field-level mapping is out of this slice.</remarks>
public sealed class OutboxMoneyMappingTests
{
    private const string Currency = "EUR";

    private static readonly DateTimeOffset Now = new(2026, 5, 22, 14, 30, 0, TimeSpan.Zero);

    private static Money Eur(decimal amount) => Money.Create(amount, Currency).Value;

    [Fact]
    public void ToInvoiceIssuedEvent_MapsMoneyAtScale4AndVatRateAtScale2()
    {
        // Arrange
        // Subtotal != Total and VAT Base != Amount so a field cross-wire is caught. Rate is the
        // sole scale-2 field among scale-4 money, so a money↔rate scale swap flips a scale assertion.
        var domainEvent = new InvoiceIssuedDomainEvent
        {
            InvoiceId = Guid.CreateVersion7(),
            InvoiceNumber = InvoiceNumber.Create(2026, 142).Value,
            BuyerId = Guid.CreateVersion7(),
            OrderId = Guid.CreateVersion7(),
            PaymentId = Guid.CreateVersion7(),
            IssueDate = Now,
            BillingAddress = Address.Create("Main Street 1", null, "Prague", null, "11000", "CZ").Value,
            Subtotal = Eur(200.00m),
            Total = Eur(242.00m),
            VatLines =
            [
                VatLine.Create(
                    VatRate.Create(21.00m).Value,
                    Eur(200.00m),
                    Eur(42.00m)),
            ],
            PdfBlobRef = PdfBlobRef.Create("2026/05/INV-2026-000142.pdf", new string('a', 64), sizeBytes: 1024).Value,
            DeliveryChannel = DeliveryChannel.Email,
            OccurredOnUtc = Now,
        };

        // Act
        var avro = domainEvent.ToInvoiceIssuedEvent();

        // Assert
        avro.VatLines.Should().ContainSingle();
        var vat = avro.VatLines[0];

        using (new AssertionScope())
        {
            avro.Currency.Should().Be(Currency);
            avro.Subtotal.Should().Be(new AvroDecimal(200.0000m));
            avro.Subtotal.Scale.Should().Be(4);
            avro.Total.Should().Be(new AvroDecimal(242.0000m));
            avro.Total.Scale.Should().Be(4);

            vat.Rate.Should().Be(new AvroDecimal(21.00m));
            vat.Rate.Scale.Should().Be(2);
            vat.BaseAmount.Should().Be(new AvroDecimal(200.0000m));
            vat.BaseAmount.Scale.Should().Be(4);
            vat.Amount.Should().Be(new AvroDecimal(42.0000m));
        }
    }

    [Fact]
    public void ToCreditNoteIssuedEvent_PreservesNegativeSignAndSubCentPrecisionAtScale4()
    {
        // Arrange
        // Credit-note Total is strictly negative (I-CN-2). The four-decimal value proves the
        // converter carries sub-cent precision: a scale-2 mutation would truncate 149.9999 to
        // 149.99, and a dropped sign would flip the unscaled value — both fail the .Be(...) check.
        var domainEvent = new CreditNoteIssuedDomainEvent
        {
            CreditNoteId = Guid.CreateVersion7(),
            CreditNoteNumber = CreditNoteNumber.Create(2026, 7).Value,
            OriginalInvoiceId = Guid.CreateVersion7(),
            OriginalInvoiceNumber = InvoiceNumber.Create(2026, 142).Value,
            BuyerId = Guid.CreateVersion7(),
            IssueDate = Now,
            Total = Eur(-149.9999m),
            Reason = CreditNoteReason.OrderCancelled,
            PdfBlobRef = PdfBlobRef.Create("2026/05/CN-2026-000007.pdf", new string('a', 64), sizeBytes: 1024).Value,
            OccurredOnUtc = Now,
        };

        // Act
        var avro = domainEvent.ToCreditNoteIssuedEvent();

        // Assert
        using (new AssertionScope())
        {
            avro.Currency.Should().Be(Currency);
            avro.Total.Should().Be(new AvroDecimal(-149.9999m));
            avro.Total.Scale.Should().Be(4);
        }
    }
}
