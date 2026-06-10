namespace Notifications.FunctionalTests.Common.TestClientInfrastructure;

/// <summary>
/// Authentication archetypes for the bell hub. The bell is per-user, not role-gated, so there is
/// a single authenticated persona plus the anonymous one (used to assert the hub rejects it).
/// </summary>
public enum ClientType
{
    NonAuth,
    RegularUser
}
