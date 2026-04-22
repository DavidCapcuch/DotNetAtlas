using Catalog.Application.Common.Data;
using Catalog.Domain.Categories;
using Catalog.Domain.Categories.Errors;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platform.CQRS;

namespace Catalog.Application.Categories.CreateCategory;

public sealed class CreateCategoryCommandHandler : ICommandHandler<CreateCategoryCommand, Guid>
{
    private readonly ICatalogDbContext _db;
    private readonly ILogger<CreateCategoryCommandHandler> _logger;

    public CreateCategoryCommandHandler(
        ICatalogDbContext db,
        ILogger<CreateCategoryCommandHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Result<Guid>> HandleAsync(CreateCategoryCommand command, CancellationToken ct)
    {
        Category? parent = null;
        if (command.ParentCategoryId is { } parentId)
        {
            parent = await _db.Categories
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == parentId, ct);
            if (parent is null)
            {
                return Result.Fail<Guid>(CategoryErrors.ParentNotFound(parentId));
            }
        }

        var categoryResult = Category.Create(command.Name, command.ParentCategoryId, parent?.Path);
        if (categoryResult.IsFailed)
        {
            return categoryResult.ToResult<Guid>();
        }

        var category = categoryResult.Value;
        _db.Categories.Add(category);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Created Category {CategoryId} ({Name}) under parent {ParentCategoryId}",
            category.Id, category.Name, command.ParentCategoryId);

        return Result.Ok(category.Id);
    }
}
