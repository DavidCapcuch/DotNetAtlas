using Platform.SharedKernel.Errors;

namespace Catalog.Domain.Categories.Errors;

public static class CategoryPathErrors
{
    public static ValidationError Malformed()
        => new ValidationError(
            propertyName: "Path",
            errorMessage: "Category path must be a leading-slash, slug-segment string (e.g., '/electronics/laptops').",
            errorCode: "CategoryPath.Malformed");

    public static ValidationError MaxDepthExceeded(int max)
        => new ValidationError(
            propertyName: "Path",
            errorMessage: $"Category path depth must not exceed {max} segments.",
            errorCode: "CategoryPath.MaxDepthExceeded");
}
