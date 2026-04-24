using System.ComponentModel.DataAnnotations;

namespace Invoicing.Infrastructure.Pdf;

/// <summary>
/// Configuration for the Invoicing PDF generator. Supplies the issuer-side text drawn on
/// the document header (legal entity name) and footer (legal/tax disclosure string). Bound
/// from the <c>PdfGeneration</c> configuration section.
/// </summary>
/// <remarks>
/// These are intentionally seller-side constants per <c>invoicing.md § 10</c>: the legal
/// footer and issuer name are operator configuration, not aggregate state. Keeping them out
/// of the aggregate avoids polluting fiscal records with environment-specific display data.
/// </remarks>
public sealed class PdfGenerationOptions
{
    public const string SectionName = "PdfGeneration";

    /// <summary>Issuer legal entity name rendered in the PDF header and <c>DocumentMetadata.Author</c>.</summary>
    [Required]
    [MinLength(1)]
    public string LegalEntityName { get; set; } = string.Empty;

    /// <summary>Legal footer text (e.g., VAT ID + registered office) rendered centered at the bottom of every page.</summary>
    [Required]
    [MinLength(1)]
    public string LegalFooter { get; set; } = string.Empty;
}
