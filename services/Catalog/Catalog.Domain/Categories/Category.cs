using Catalog.Domain.Categories.Errors;
using Catalog.Domain.Categories.Events;
using Catalog.Domain.Categories.ValueObjects;
using FluentResults;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Errors;
using Platform.SharedKernel.Exceptions;

namespace Catalog.Domain.Categories;

/// <summary>
/// Aggregate root representing a node in the Catalog taxonomy tree.
/// Owns its identity, display <see cref="Name"/>, optional <see cref="ParentCategoryId"/>,
/// and a materialized <see cref="Path"/>. Categories are referenced by <see cref="Product"/>
/// via <c>CategoryId</c> only (ID-by-reference between aggregates).
/// </summary>
/// <remarks>
/// Descendant path rewriting on rename / reparent is a domain-service operation that
/// touches multiple aggregates within the same transaction — it is intentionally
/// outside this aggregate's boundary.
///
/// This aggregate can raise the following domain events:
/// <list type="bullet">
/// <item><see cref="CategoryCreatedDomainEvent"/>: When a new category is created.</item>
/// <item><see cref="CategoryReparentedDomainEvent"/>: When <see cref="Rename"/> or
/// <see cref="Reparent"/> succeeds.</item>
/// </list>
/// </remarks>
public sealed class Category : AggregateRoot<Guid>, IAuditableEntity
{
    public const int MaxNameLength = 100;

    public string Name { get; private set; } = string.Empty;
    public Guid? ParentCategoryId { get; private set; }
    public CategoryPath Path { get; private set; } = null!;

    public DateTimeOffset CreatedUtc { get; private set; }
    public DateTimeOffset LastModifiedUtc { get; private set; }

    private Category()
    {
    }

    /// <summary>
    /// Creates a new category. If <paramref name="parentCategoryId"/> is non-null,
    /// the caller must supply the parent's <see cref="Path"/> (loaded via repository).
    /// The resulting path is built as <c>parentPath + "/" + slug(name)</c> or
    /// <c>"/" + slug(name)</c> for a root category.
    /// </summary>
    /// <remarks>
    /// Raises <see cref="CategoryCreatedDomainEvent"/> with <c>OccurredOnUtc = utcNow</c>
    /// on success.
    /// </remarks>
    public static Result<Category> Create(
        string name,
        Guid? parentCategoryId,
        CategoryPath? parentPath,
        DateTimeOffset utcNow)
    {
        var nameValidation = ValidateName(name);
        if (nameValidation.IsFailed)
        {
            return Result.Fail(nameValidation.Errors);
        }

        var slug = CategoryPath.Slugify(name);
        if (slug is null)
        {
            return Result.Fail(CategoryErrors.NameRequired());
        }

        Throw.If(parentCategoryId.HasValue && parentPath is null, new DataIntegrityException(
            "Category.MissingParentPath",
            "A parent category was specified but its path was not supplied."));

        Result<CategoryPath> newPath = parentCategoryId.HasValue
            ? parentPath!.Append(slug)
            : CategoryPath.Create($"/{slug}");

        if (newPath.IsFailed)
        {
            return MapPathErrorToAggregateError(newPath);
        }

        var category = new Category
        {
            Id = Guid.CreateVersion7(),
            Name = name.Trim(),
            ParentCategoryId = parentCategoryId,
            Path = newPath.Value
        };

        category.AddDomainEvent(new CategoryCreatedDomainEvent
        {
            CategoryId = category.Id,
            Name = category.Name,
            ParentCategoryId = parentCategoryId,
            Path = category.Path,
            OccurredOnUtc = utcNow
        });

        return category;
    }

    /// <summary>
    /// Renames the category. Rewrites the final segment of <see cref="Path"/> to the
    /// slug of <paramref name="newName"/>. Descendants' paths are updated by a
    /// domain-service operation, not by this aggregate.
    /// </summary>
    /// <remarks>
    /// Raises <see cref="CategoryReparentedDomainEvent"/> with
    /// <c>OldParentId == NewParentId</c> and <c>OccurredOnUtc = utcNow</c> on success.
    /// </remarks>
    public Result Rename(string newName, DateTimeOffset utcNow)
    {
        var nameValidation = ValidateName(newName);
        if (nameValidation.IsFailed)
        {
            return nameValidation;
        }

        var slug = CategoryPath.Slugify(newName);
        if (slug is null)
        {
            return Result.Fail(CategoryErrors.NameRequired());
        }

        var parentPrefix = ExtractParentPrefix(Path);
        var rebuiltPath = CategoryPath.Create($"{parentPrefix}/{slug}");
        if (rebuiltPath.IsFailed)
        {
            return rebuiltPath.ToResult();
        }

        var oldPath = Path;
        Name = newName.Trim();
        Path = rebuiltPath.Value;

        AddDomainEvent(new CategoryReparentedDomainEvent
        {
            CategoryId = Id,
            OldParentId = ParentCategoryId,
            NewParentId = ParentCategoryId,
            OldPath = oldPath,
            NewPath = Path,
            OccurredOnUtc = utcNow
        });

        return Result.Ok();
    }

    /// <summary>
    /// Reparents this category under a new parent (null for root).
    /// Cycle detection against descendants is the caller's responsibility (via a
    /// <c>CategoryAncestryService</c>); this aggregate only rejects the self-parent case.
    /// </summary>
    /// <remarks>
    /// Raises <see cref="CategoryReparentedDomainEvent"/> with <c>OccurredOnUtc = utcNow</c>
    /// on success.
    /// </remarks>
    public Result Reparent(Guid? newParentCategoryId, CategoryPath? newParentPath, DateTimeOffset utcNow)
    {
        if (newParentCategoryId.HasValue && newParentCategoryId.Value == Id)
        {
            return Result.Fail(CategoryErrors.CannotParentToSelf());
        }

        Throw.If(newParentCategoryId.HasValue && newParentPath is null, new DataIntegrityException(
            "Category.MissingParentPath",
            "A new parent category was specified but its path was not supplied."));

        var slug = ExtractFinalSlug(Path);
        Result<CategoryPath> rebuiltPath = newParentCategoryId.HasValue
            ? newParentPath!.Append(slug)
            : CategoryPath.Create($"/{slug}");

        if (rebuiltPath.IsFailed)
        {
            return MapPathErrorToAggregateError(rebuiltPath);
        }

        var oldParentId = ParentCategoryId;
        var oldPath = Path;
        ParentCategoryId = newParentCategoryId;
        Path = rebuiltPath.Value;

        AddDomainEvent(new CategoryReparentedDomainEvent
        {
            CategoryId = Id,
            OldParentId = oldParentId,
            NewParentId = newParentCategoryId,
            OldPath = oldPath,
            NewPath = Path,
            OccurredOnUtc = utcNow
        });

        return Result.Ok();
    }

    private static Result MapPathErrorToAggregateError<T>(Result<T> pathResult)
    {
        // Error-taxonomy.md § 1 surfaces user-facing depth failures as
        // "Category.MaxDepthExceeded", not "CategoryPath.MaxDepthExceeded".
        // Translate here so consumers see the enumerated aggregate-level code.
        var depthError = pathResult.Errors.OfType<DomainError>()
            .FirstOrDefault(e => e.ErrorCode == "CategoryPath.MaxDepthExceeded");
        return depthError is null
            ? pathResult.ToResult()
            : Result.Fail(CategoryErrors.MaxDepthExceeded(CategoryPath.MaxDepth));
    }

    private static Result ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Fail(CategoryErrors.NameRequired());
        }

        if (name.Trim().Length > MaxNameLength)
        {
            return Result.Fail(CategoryErrors.NameTooLong(MaxNameLength));
        }

        return Result.Ok();
    }

    private static string ExtractParentPrefix(CategoryPath path)
    {
        var lastSlash = path.Value.LastIndexOf('/');
        return lastSlash <= 0 ? string.Empty : path.Value[..lastSlash];
    }

    private static string ExtractFinalSlug(CategoryPath path)
    {
        var lastSlash = path.Value.LastIndexOf('/');
        return path.Value[(lastSlash + 1)..];
    }
}
