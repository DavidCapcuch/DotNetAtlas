namespace Payments.FunctionalTests.Common.TestClientInfrastructure;

/// <summary>
/// Stable user ids used by <see cref="FakeTokenCreator"/>. Hard-coded so the
/// same caller is recognised across functional tests in the same fixture
/// lifetime.
/// </summary>
internal static class TestUsers
{
    public static readonly Guid UserId = new("01999998-0002-7000-8000-000000000001");
    public static readonly Guid AdminId = new("01999998-0002-7000-8000-000000000002");
}
