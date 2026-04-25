using Catalog.Domain.Products;
using Catalog.Domain.Products.Events;
using Catalog.Domain.Products.ValueObjects;
using FluentResults.Extensions.FluentAssertions;
using Platform.SharedKernel.Errors;
using Platform.SharedKernel.Exceptions;
using Platform.SharedKernel.ValueObjects;

namespace Catalog.UnitTests.Products.Aggregates;

public class ProductTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 4, 22, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_WhenValid_ReturnsDraftProductAndRaisesProductCreated()
    {
        // Arrange
        var categoryId = Guid.CreateVersion7();

        // Act
        var result = Product.Create(
            Sku.Create("TEST-001").Value,
            ProductName.Create("Test Product").Value,
            ProductDescription.Create("desc").Value,
            categoryId,
            BrandName.Create("TestBrand").Value,
            Money.Create(10m, CurrencyCode.Usd).Value,
            dimensions: null,
            images: [],
            UtcNow);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            var product = result.Value;
            product.Status.Should().Be(ProductStatus.Draft);
            product.Id.Should().NotBe(Guid.Empty);
            product.CategoryId.Should().Be(categoryId);
            product.Images.Should().BeEmpty();
            var created = product.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<ProductCreatedDomainEvent>()
                .Subject;
            created.ProductId.Should().Be(product.Id);
            created.CategoryId.Should().Be(categoryId);
        }
    }

    [Fact]
    public void Create_WhenCategoryIdEmpty_ReturnsFailureWithCategoryIdRequired()
    {
        // Act
        var result = Product.Create(
            Sku.Create("TEST-001").Value,
            ProductName.Create("Test").Value,
            ProductDescription.Create("").Value,
            Guid.Empty,
            BrandName.Create("Brand").Value,
            Money.Create(1m, CurrencyCode.Usd).Value,
            dimensions: null,
            images: [],
            UtcNow);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "Product.CategoryIdRequired");
        }
    }

    [Fact]
    public void Create_OrdersImagesByDisplayOrder()
    {
        // Arrange
        var second = ImageReference.Create("https://e.com/1.png", "alt1", 1).Value;
        var first = ImageReference.Create("https://e.com/0.png", "alt0", 0).Value;

        // Act
        var product = CreateDraftProduct(images: [second, first]);

        // Assert
        product.Images.Select(i => i.DisplayOrder).Should().Equal(0, 1);
    }

    [Fact]
    public void UpdatePrice_WhenStatusDiscontinued_ReturnsCannotRepriceDiscontinued()
    {
        // Arrange
        var product = CreateDiscontinuedProduct();
        var newPrice = Money.Create(99m, CurrencyCode.Usd).Value;

        // Act
        var result = product.UpdatePrice(newPrice, UtcNow);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "Product.CannotRepriceDiscontinued");
            product.PopDomainEvents().Should().BeEmpty();
        }
    }

    [Fact]
    public void UpdatePrice_WhenIdentical_IsNoOpNoEvent()
    {
        // Arrange
        var product = CreateDraftProduct();
        _ = product.PopDomainEvents();
        var samePrice = Money.Create(10m, CurrencyCode.Usd).Value;

        // Act
        var result = product.UpdatePrice(samePrice, UtcNow);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            product.PopDomainEvents().Should().BeEmpty();
        }
    }

    [Fact]
    public void UpdatePrice_WhenDifferent_RaisesProductPriceChangedAndUpdatesPrice()
    {
        // Arrange
        var product = CreateDraftProduct();
        _ = product.PopDomainEvents();
        var oldPrice = product.Price;
        var newPrice = Money.Create(25m, CurrencyCode.Usd).Value;

        // Act
        var result = product.UpdatePrice(newPrice, UtcNow);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            product.Price.Should().Be(newPrice);
            var evt = product.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<ProductPriceChangedDomainEvent>()
                .Subject;
            evt.ProductId.Should().Be(product.Id);
            evt.OldPrice.Should().Be(oldPrice);
            evt.NewPrice.Should().Be(newPrice);
            evt.OccurredOnUtc.Should().Be(UtcNow);
        }
    }

    [Fact]
    public void Describe_WhenStatusDiscontinued_ReturnsCannotModifyDiscontinued()
    {
        // Arrange
        var product = CreateDiscontinuedProduct();
        var newDescription = ProductDescription.Create("updated").Value;

        // Act
        var result = product.Describe(newDescription, UtcNow);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "Product.CannotModifyDiscontinued");
            product.PopDomainEvents().Should().BeEmpty();
        }
    }

    [Fact]
    public void Describe_WhenStatusActive_RaisesProductDescribed()
    {
        // Arrange
        var product = CreateActiveProduct();
        _ = product.PopDomainEvents();
        var newDescription = ProductDescription.Create("updated").Value;

        // Act
        var result = product.Describe(newDescription, UtcNow);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            product.Description.Should().Be(newDescription);
            var evt = product.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<ProductDescribedDomainEvent>()
                .Subject;
            evt.ProductId.Should().Be(product.Id);
            evt.NewDescription.Should().Be(newDescription);
        }
    }

    [Fact]
    public void Activate_FromDraft_RaisesProductActivated()
    {
        // Arrange
        var product = CreateDraftProduct();
        _ = product.PopDomainEvents();

        // Act
        var result = product.Activate(UtcNow);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            product.Status.Should().Be(ProductStatus.Active);
            product.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<ProductActivatedDomainEvent>();
        }
    }

    [Fact]
    public void Activate_FromActive_ThrowsDataIntegrityException()
    {
        // Arrange
        var product = CreateActiveProduct();

        // Act
        var act = () => product.Activate(UtcNow);

        // Assert
        act.Should().Throw<DataIntegrityException>()
            .WithMessage("*Cannot activate product in status 'Active'*");
    }

    [Fact]
    public void Activate_FromDiscontinued_ThrowsDataIntegrityException()
    {
        // Arrange
        var product = CreateDiscontinuedProduct();

        // Act
        var act = () => product.Activate(UtcNow);

        // Assert
        act.Should().Throw<DataIntegrityException>()
            .WithMessage("*Cannot activate product in status 'Discontinued'*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Discontinue_WhenReasonEmpty_ReturnsReasonRequired(string? reason)
    {
        // Arrange
        var product = CreateActiveProduct();
        _ = product.PopDomainEvents();

        // Act
        var result = product.Discontinue(reason!, UtcNow);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "Product.ReasonRequired");
            product.PopDomainEvents().Should().BeEmpty();
        }
    }

    [Fact]
    public void Discontinue_WhenActive_RaisesProductDiscontinued()
    {
        // Arrange
        var product = CreateActiveProduct();
        _ = product.PopDomainEvents();

        // Act
        var result = product.Discontinue("End of life", UtcNow);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            product.Status.Should().Be(ProductStatus.Discontinued);
            var evt = product.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<ProductDiscontinuedDomainEvent>()
                .Subject;
            evt.Reason.Should().Be("End of life");
        }
    }

    [Fact]
    public void Discontinue_FromDraft_ThrowsDataIntegrityException()
    {
        // Arrange
        var product = CreateDraftProduct();

        // Act
        var act = () => product.Discontinue("reason", UtcNow);

        // Assert
        act.Should().Throw<DataIntegrityException>()
            .WithMessage("*Cannot discontinue product in status 'Draft'*");
    }

    [Fact]
    public void Discontinue_FromDiscontinued_ThrowsDataIntegrityException()
    {
        // Arrange
        var product = CreateDiscontinuedProduct();

        // Act
        var act = () => product.Discontinue("reason", UtcNow);

        // Assert
        act.Should().Throw<DataIntegrityException>()
            .WithMessage("*Cannot discontinue product in status 'Discontinued'*");
    }

    [Fact]
    public void Reactivate_WithoutAdminFlagFromDiscontinued_ReturnsReactivationRequiresAdminFlag()
    {
        // Arrange
        var product = CreateDiscontinuedProduct();
        _ = product.PopDomainEvents();

        // Act
        var result = product.Reactivate(adminReactivation: false, UtcNow);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "Product.ReactivationRequiresAdminFlag");
            product.Status.Should().Be(ProductStatus.Discontinued);
            product.PopDomainEvents().Should().BeEmpty();
        }
    }

    [Fact]
    public void Reactivate_WithoutAdminFlagFromActive_ReturnsReactivationRequiresAdminFlag()
    {
        // Arrange — flag check happens BEFORE status check (spec: catalog.md:71).
        var product = CreateActiveProduct();

        // Act
        var result = product.Reactivate(adminReactivation: false, UtcNow);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "Product.ReactivationRequiresAdminFlag");
            product.Status.Should().Be(ProductStatus.Active);
        }
    }

    [Fact]
    public void Reactivate_WithoutAdminFlagFromDraft_ReturnsReactivationRequiresAdminFlag()
    {
        // Arrange — flag check happens BEFORE status check (spec: catalog.md:71).
        var product = CreateDraftProduct();

        // Act
        var result = product.Reactivate(adminReactivation: false, UtcNow);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "Product.ReactivationRequiresAdminFlag");
            product.Status.Should().Be(ProductStatus.Draft);
        }
    }

    [Fact]
    public void Reactivate_WithAdminFlagFromDiscontinued_RaisesProductReactivated()
    {
        // Arrange
        var product = CreateDiscontinuedProduct();
        _ = product.PopDomainEvents();

        // Act
        var result = product.Reactivate(adminReactivation: true, UtcNow);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            product.Status.Should().Be(ProductStatus.Active);
            product.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<ProductReactivatedDomainEvent>();
        }
    }

    [Fact]
    public void Reactivate_WithAdminFlagFromActive_ThrowsDataIntegrityException()
    {
        // Arrange
        var product = CreateActiveProduct();

        // Act
        var act = () => product.Reactivate(adminReactivation: true, UtcNow);

        // Assert
        act.Should().Throw<DataIntegrityException>()
            .WithMessage("*Cannot reactivate product in status 'Active'*");
    }

    private static Product CreateDraftProduct(IReadOnlyCollection<ImageReference>? images = null)
    {
        var result = Product.Create(
            Sku.Create("TEST-001").Value,
            ProductName.Create("Test Product").Value,
            ProductDescription.Create("").Value,
            Guid.CreateVersion7(),
            BrandName.Create("TestBrand").Value,
            Money.Create(10m, CurrencyCode.Usd).Value,
            dimensions: null,
            images: images ?? [],
            UtcNow);
        return result.Value;
    }

    private static Product CreateActiveProduct()
    {
        var product = CreateDraftProduct();
        product.Activate(UtcNow);
        _ = product.PopDomainEvents();
        return product;
    }

    private static Product CreateDiscontinuedProduct()
    {
        var product = CreateDraftProduct();
        product.Activate(UtcNow);
        product.Discontinue("End of life", UtcNow);
        _ = product.PopDomainEvents();
        return product;
    }
}
