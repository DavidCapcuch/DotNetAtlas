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

    public static NotFoundError NotFound(Guid categoryId)
        => new NotFoundError(
            entityName: "Category",
            id: categoryId,
            errorCode: "Category.NotFound");

    public static NotFoundError ParentNotFound(Guid parentCategoryId)
        => new NotFoundError(
            entityName: "Category",
            id: parentCategoryId,
            errorCode: "Category.ParentNotFound");

    public static ValidationError ReparentCreatesCycle(Guid categoryId, Guid newParentCategoryId)
        => new ValidationError(
            propertyName: "ParentCategoryId",
            errorMessage:
                $"Reparenting category '{categoryId}' under '{newParentCategoryId}' would create a cycle "
                + "(the candidate parent is the category itself or one of its descendants).",
            errorCode: "Category.ReparentCreatesCycle");
}
