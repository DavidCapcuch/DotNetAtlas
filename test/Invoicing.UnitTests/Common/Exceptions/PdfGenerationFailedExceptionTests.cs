using Invoicing.Application.Common.Exceptions;
using Platform.SharedKernel.Exceptions;

namespace Invoicing.UnitTests.Common.Exceptions;

public class PdfGenerationFailedExceptionTests
{
    [Fact]
    public void Constructor_PreservesDetailAndInnerException()
    {
        var inner = new InvalidOperationException("layout overflow at page 3");

        var ex = new PdfGenerationFailedException("layout overflow at page 3", inner);

        ex.Detail.Should().Be("layout overflow at page 3");
        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void Constructor_SetsCanonicalErrorCode()
    {
        var ex = new PdfGenerationFailedException("anything", new Exception());

        ex.ErrorCode.Should().Be("Invoicing.PdfGenerationFailed");
    }

    [Fact]
    public void Constructor_EmbedsDetailInMessage()
    {
        var ex = new PdfGenerationFailedException("strict-space MaxWidth violation", new Exception());

        ex.Message.Should().Contain("strict-space MaxWidth violation");
    }

    [Fact]
    public void InheritsDataIntegrityException_SoExistingDltBranchCatchesIt()
    {
        var ex = new PdfGenerationFailedException("d", new Exception());

        // Locks the inheritance chain that the consumer middleware's catch (CriticalException)
        // branch relies on for DLT routing.
        ex.Should().BeAssignableTo<DataIntegrityException>();
        ex.Should().BeAssignableTo<CriticalException>();
    }
}
