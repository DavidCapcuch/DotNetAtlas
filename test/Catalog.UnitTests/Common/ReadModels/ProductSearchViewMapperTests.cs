using System.Text.Json.Nodes;
using Catalog.Application.Common.ReadModels;

namespace Catalog.UnitTests.Common.ReadModels;

public class ProductSearchViewMapperTests
{
    // A real catalog.product_search_view.images_json row.
    //
    // NEVER REGENERATE THIS FROM ProductImageDocument. Its whole value is that it cannot be updated
    // symmetrically with a rename: a renamed member, a changed type, or a new required member must
    // fail here rather than throw on every historical row the first time production reads one.
    // Nothing serializes into this constant — if a change to the document makes this test red, the
    // answer is a persistence migration, not an edit to this string.
    private const string HistoricalImagesJson =
        """[{"Url":"https://cdn.example.com/back.jpg","AltText":"Back view","DisplayOrder":2},{"Url":"https://cdn.example.com/front.jpg","AltText":"Front view","DisplayOrder":1}]""";

    [Fact]
    public void DeserializeImages_RowStoredBeforeAnyRename_ReadsEveryMember()
    {
        var images = ProductSearchViewMapper.DeserializeImages(HistoricalImagesJson);

        using (new AssertionScope())
        {
            images.Should().HaveCount(2);
            images[0].Url.Should().Be("https://cdn.example.com/back.jpg");
            images[0].AltText.Should().Be("Back view");
            images[0].DisplayOrder.Should().Be(2);
            images[1].Url.Should().Be("https://cdn.example.com/front.jpg");
            images[1].AltText.Should().Be("Front view");
            images[1].DisplayOrder.Should().Be(1);
        }
    }

    // Pins the writer against the reader: the two use separate JsonSerializer calls, so giving one a
    // naming policy the other lacks would emit {"url":...} that the case-sensitive, all-required
    // reader then throws on. Asserts the key SET, not the whole string — key ORDER is not
    // load-bearing (the column is jsonb, and Postgres reorders keys on write regardless).
    [Fact]
    public void SerializeImages_Documents_WritesTheKeysTheReaderExpects()
    {
        var images = new[]
        {
            new ProductImageDocument
            {
                Url = "https://cdn.example.com/back.jpg", AltText = "Back view", DisplayOrder = 2,
            },
        };

        var written = ProductSearchViewMapper.SerializeImages(images);

        var keys = JsonNode.Parse(written)!.AsArray()[0]!.AsObject().Select(p => p.Key);
        keys.Should().BeEquivalentTo("Url", "AltText", "DisplayOrder");
    }

    [Fact]
    public void ToDimensionsDto_AllColumnsPopulated_ReturnsDimensionsInColumnOrder()
    {
        // Distinct values per axis so a transposed mapping (length ↔ width) fails.
        var dimensions = ProductSearchViewMapper.ToDimensionsDto(10.5m, 4.25m, 2m, "cm");

        using (new AssertionScope())
        {
            dimensions.Should().NotBeNull();
            dimensions!.Length.Should().Be(10.5m);
            dimensions.Width.Should().Be(4.25m);
            dimensions.Height.Should().Be(2m);
            dimensions.Unit.Should().Be("cm");
        }
    }

    [Fact]
    public void ToDimensionsDto_NoDimensionColumns_ReturnsNullForDigitalProduct()
    {
        var dimensions = ProductSearchViewMapper.ToDimensionsDto(null, null, null, null);

        dimensions.Should().BeNull();
    }

    // The four columns are written atomically from one optional VO, so a partial row cannot occur
    // by construction. It reads as "dimensions unknown" rather than throwing on a GET.
    [Theory]
    [InlineData(null, 4.25d, 2d, "cm")]
    [InlineData(10.5d, null, 2d, "cm")]
    [InlineData(10.5d, 4.25d, null, "cm")]
    [InlineData(10.5d, 4.25d, 2d, null)]
    public void ToDimensionsDto_PartiallyPopulatedRow_ReturnsNull(
        double? length, double? width, double? height, string? unit)
    {
        var dimensions = ProductSearchViewMapper.ToDimensionsDto(
            (decimal?)length, (decimal?)width, (decimal?)height, unit);

        dimensions.Should().BeNull();
    }
}
