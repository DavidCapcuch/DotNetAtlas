using Platform.SharedKernel.Errors;

namespace Catalog.Domain.Categories.Errors;

public static class CategoryErrors
{
    public static ValidationError NameRequired()
        => new ValidationError(
            propertyName: "Name",
            errorMessage: "Category name must not be empty.",
            errorCode: "Category.NameRequired");

    public static ValidationError NameTooLong(int max)
        => new ValidationError(
            propertyName: "Name",
            errorMessage: $"Category name must not exceed {max} characters.",
            errorCode: "Category.NameTooLong");

    public static ValidationError MaxDepthExceeded(int max)
        => new ValidationError(
            propertyName: "Path",
            errorMessage: $"Category path depth must not exceed {max} segments.",
            errorCode: "Category.MaxDepthExceeded");

    public static ValidationError CannotParentToSelf()
        => new ValidationError(
            propertyName: "ParentCategoryId",
            errorMessage: "A category cannot be reparented under itself.",
            errorCode: "Category.CannotParentToSelf");
}
