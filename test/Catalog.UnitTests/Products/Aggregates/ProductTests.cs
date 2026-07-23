using Catalog.Domain.Products;
using Catalog.Domain.Products.Events;
using Catalog.Domain.Products.ValueObjects;
using FluentResults.Extensions.FluentAssertions;
using Platform.SharedKernel.Errors;
using Platform.SharedKernel.ValueObjects;

namespace Catalog.UnitTests.Products.Aggregates;

public class ProductTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 4, 22, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_WhenValid_ReturnsActiveProductAndRaisesProductCreated()
    {
        // Arrange
        var categoryId = Guid.CreateVersion7();
        var sku = Sku.Create("TEST-001").Value;
        var name = ProductName.Create("Test Product").Value;
        var description = ProductDescription.Create("desc").Value;
        var brand = BrandName.Create("TestBrand").Value;
        var price = Money.Create(10m, CurrencyCode.Usd).Value;

        // Act
        var result = Product.Create(
            sku,
            name,
            description,
            categoryId,
            brand,
            price,
            dimensions: null,
            images: [],
            UtcNow);

        // Assert — CAT-TST #201: pin the full factory-side-effect surface so
        // mutation testing can't leave constructor-body assignments unchecked. Every assignment
        // in the Product.Create body must be observable here. Status is set to Active on create.
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            var product = result.Value;
            product.Id.Should().NotBe(Guid.Empty);
            product.Sku.Should().BeSameAs(sku);
            product.Name.Should().BeSameAs(name);
            product.Description.Should().BeSameAs(description);
            product.CategoryId.Should().Be(categoryId);
            product.Brand.Should().BeSameAs(brand);
            product.Price.Should().BeSameAs(price);
            product.Dimensions.Should().BeNull();
            product.Images.Should().BeEmpty();
            product.Status.Should().Be(ProductStatus.Active);

            // Exactly one domain event raised, with every payload field threaded through.
            var domainEvents = product.PopDomainEvents();
            domainEvents.Should().ContainSingle();
            var created = domainEvents.Single().Should()
                .BeOfType<ProductCreatedDomainEvent>().Subject;
            created.ProductId.Should().Be(product.Id);
            created.Sku.Should().BeSameAs(sku);
            created.Name.Should().BeSameAs(name);
            created.CategoryId.Should().Be(categoryId);
            created.Price.Should().BeSameAs(price);
            created.OccurredOnUtc.Should().Be(UtcNow);
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
        var product = CreateActiveProduct(images: [second, first]);

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
        var product = CreateActiveProduct();
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
        var product = CreateActiveProduct();
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
    public void UpdatePrice_WhenCurrencyDiffers_ReturnsCannotChangePriceCurrencyAndKeepsPrice()
    {
        // Arrange — a Product's price is single-currency for its lifetime (ADR-0002); repricing
        // into a different currency is not a valid amount change and must be rejected.
        var product = CreateActiveProduct();
        _ = product.PopDomainEvents();
        var originalPrice = product.Price;
        var differentCurrency = Money.Create(20m, CurrencyCode.Eur).Value;

        // Act
        var result = product.UpdatePrice(differentCurrency, UtcNow);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "Product.CannotChangePriceCurrency");
            product.Price.Should().Be(originalPrice);
            product.PopDomainEvents().Should().BeEmpty();
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
            evt.OccurredOnUtc.Should().Be(UtcNow);
        }
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
            evt.OccurredOnUtc.Should().Be(UtcNow);
        }
    }

    [Fact]
    public void Discontinue_FromDiscontinued_ReturnsCannotDiscontinueInStatus()
    {
        // Arrange
        var product = CreateDiscontinuedProduct();

        // Act
        var result = product.Discontinue("reason", UtcNow);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle()
                .Which.Should().BeAssignableTo<ConflictError>()
                .Which.ErrorCode.Should().Be("Product.CannotDiscontinueInStatus");
        }
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
            var reactivated = product.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<ProductReactivatedDomainEvent>().Subject;
            reactivated.OccurredOnUtc.Should().Be(UtcNow);
        }
    }

    [Fact]
    public void Reactivate_WithAdminFlagFromActive_ReturnsCannotReactivateInStatus()
    {
        // Arrange
        var product = CreateActiveProduct();

        // Act
        var result = product.Reactivate(adminReactivation: true, UtcNow);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle()
                .Which.Should().BeAssignableTo<ConflictError>()
                .Which.ErrorCode.Should().Be("Product.CannotReactivateInStatus");
        }
    }

    private static Product CreateActiveProduct(IReadOnlyCollection<ImageReference>? images = null)
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

    private static Product CreateDiscontinuedProduct()
    {
        var product = CreateActiveProduct();
        product.Discontinue("End of life", UtcNow);
        _ = product.PopDomainEvents();
        return product;
    }
}
