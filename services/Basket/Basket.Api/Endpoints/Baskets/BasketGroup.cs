using FastEndpoints;

namespace Basket.Api.Endpoints.Baskets;

internal sealed class BasketGroup : Group
{
    public BasketGroup()
    {
        Configure("/basket", ep =>
        {
            ep.Description(builder => builder
                .WithGroupName("Basket"));
            ep.Tags("Basket");
        });
    }
}
