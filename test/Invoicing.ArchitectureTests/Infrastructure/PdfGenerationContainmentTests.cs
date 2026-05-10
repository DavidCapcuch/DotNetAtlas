using Invoicing.ArchitectureTests.Rules;
using NetArchTest.Rules;

namespace Invoicing.ArchitectureTests.Infrastructure;

/// <summary>
/// ADR-0019: QuestPDF is the v1 invoice PDF library, but it lives behind the
/// <c>IPdfGenerator</c> seam in <c>Invoicing.Application.Pdf</c>. Domain and
/// Application must stay vendor-neutral. Additionally, every type in the
/// <c>Invoicing.Infrastructure.Pdf</c> namespace must avoid static "now"
/// getters so PDF output stays byte-deterministic across runs (the runtime
/// hash test in <c>QuestPdfInvoiceGeneratorTests</c> would catch a regression
/// empirically; this fact catches it statically and points the contributor at
/// the determinism contract).
/// </summary>
public sealed class PdfGenerationContainmentTests : BaseTest
{
    [Fact]
    public void Domain_ShouldNotHaveDependencyOn_QuestPdf()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOnAny("QuestPDF")
            .GetResult();
        result.FailingTypes.Should().BeEmpty(
            "Domain must not reference the PDF library — invoice templating belongs in Infrastructure");
    }

    [Fact]
    public void Application_ShouldNotHaveDependencyOn_QuestPdf()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOnAny("QuestPDF")
            .GetResult();
        result.FailingTypes.Should().BeEmpty(
            "Application uses IPdfGenerator (Invoicing.Application.Pdf) — QuestPDF belongs to " +
            "QuestPdfInvoiceGenerator in Invoicing.Infrastructure.Pdf only");
    }

    [Fact]
    public void PdfNamespace_ShouldNotCall_StaticUtcNow()
    {
        // Regex selector covers `Invoicing.Infrastructure.Pdf` and any future
        // sub-namespace (e.g. `...Pdf.Templates`). The exact-match
        // `ResideInNamespace("Invoicing.Infrastructure.Pdf")` would silently
        // miss types added under a child namespace.
        var result = Types.InAssembly(InfrastructureAssembly)
            .That()
            .ResideInNamespaceMatching(@"^Invoicing\.Infrastructure\.Pdf(\..*)?$")
            .Should()
            .MeetCustomRule(new DoesNotCallStaticUtcNowRule())
            .GetResult();
        result.FailingTypes.Should().BeEmpty(
            "Per ADR-0019, PDF templates must derive every timestamp from aggregate state " +
            "(e.g. invoice.IssueDate.UtcDateTime) — static DateTime.Now / UtcNow breaks byte-determinism");
    }
}
