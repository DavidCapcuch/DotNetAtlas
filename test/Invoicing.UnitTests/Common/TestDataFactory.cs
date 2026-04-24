using Invoicing.Domain.Common.ValueObjects;
using Invoicing.Domain.Invoices;
using Invoicing.Domain.Invoices.ValueObjects;
using Platform.SharedKernel.ValueObjects;

namespace Invoicing.UnitTests.Common;

/// <summary>
/// Convenience factories building minimally-valid domain objects for tests. Each builder
/// exposes every relevant parameter via optional args so tests can mutate exactly one
/// input at a time without boilerplate.
/// </summary>
internal static class TestDataFactory
{
    public static readonly DateTimeOffset FixedUtcNow = new(2026, 4, 24, 12, 0, 0, TimeSpan.Zero);

    public static Address DefaultBillingAddress() =>
        Address.Create("Main Street 1", null, "Prague", null, "11000", "CZ").Value;

    public static Sku DefaultSku(string value = "WIDGET-001") => Sku.Create(value).Value;

    public static VatRate DefaultVatRate(decimal pct = 21m) => VatRate.Create(pct).Value;

    public static InvoiceLine BuildLine(
        int lineNumber = 1,
        int quantity = 2,
        decimal unitPrice = 100m,
        string currency = "EUR",
        decimal vatRate = 21m,
        string sku = "WIDGET-001",
        string description = "Widget")
    {
        var price = Money.Create(unitPrice, currency).Value;
        return InvoiceLine.Create(
            lineNumber,
            DefaultSku(sku),
            description,
            quantity,
            price,
            DefaultVatRate(vatRate)).Value;
    }

    public static VatLine BuildVatLine(
        decimal rate = 21m,
        decimal baseAmount = 200m,
        decimal taxAmount = 42m,
        string currency = "EUR")
    {
        var curr = Platform.SharedKernel.ValueObjects.CurrencyCode.FromName(currency);
        return new VatLine(
            DefaultVatRate(rate),
            new Money(baseAmount, curr),
            new Money(taxAmount, curr));
    }

    public static Invoice BuildDraftInvoice(
        DateTimeOffset? utcNow = null,
        IReadOnlyList<InvoiceLine>? lines = null,
        IReadOnlyList<VatLine>? vatLines = null,
        DeliveryChannel? deliveryChannel = null)
    {
        var linesActual = lines ?? [BuildLine()];
        var vatActual = vatLines ?? [BuildVatLine()];
        var channel = deliveryChannel ?? DeliveryChannel.Email;

        return Invoice.Create(
            buyerId: Guid.CreateVersion7(),
            orderId: Guid.CreateVersion7(),
            paymentId: Guid.CreateVersion7(),
            correlationId: Guid.CreateVersion7(),
            billingAddress: DefaultBillingAddress(),
            lines: linesActual,
            vatLines: vatActual,
            deliveryChannel: channel,
            utcNow: utcNow ?? FixedUtcNow).Value;
    }

    public static Invoice BuildIssuedInvoice(
        DateTimeOffset? utcNow = null,
        int year = 2026,
        long sequence = 142)
    {
        var invoice = BuildDraftInvoice(utcNow);
        var now = utcNow ?? FixedUtcNow;
        var number = InvoiceNumber.Create(year, sequence).Value;
        var pdf = PdfBlobRef.Create(
            new Uri("https://example.com/invoices/2026/04/INV-2026-000142.pdf?sv=sas"),
            new string('a', 64),
            sizeBytes: 1024).Value;
        invoice.Issue(number, pdf, now);
        return invoice;
    }
}
