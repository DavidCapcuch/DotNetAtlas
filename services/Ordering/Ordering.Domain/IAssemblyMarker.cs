namespace Ordering.Domain;

/// <summary>
/// Marker interface used by architecture tests and DI assembly scanning to reference
/// the Ordering.Domain assembly without brittle <c>typeof(SomePublicType).Assembly</c> calls.
/// </summary>
public interface IAssemblyMarker;
