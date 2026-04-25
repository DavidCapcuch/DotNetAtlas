namespace Ordering.FunctionalTests.Common.TestClientInfrastructure;

/// <summary>
/// Stable buyer ids used by <see cref="FakeTokenCreator"/> + the test seed
/// helpers. Hard-coded so the same buyer is recognised across functional
/// tests in the same fixture lifetime.
/// </summary>
internal static class TestUsers
{
    public static readonly Guid BuyerId = new("01999998-0001-7000-8000-000000000001");
    public static readonly Guid OtherBuyerId = new("01999998-0001-7000-8000-000000000002");
    public static readonly Guid AdminId = new("01999998-0001-7000-8000-000000000003");
}
