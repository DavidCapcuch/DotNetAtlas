namespace SagaOrchestrators.UnitTests.Checkout;

/// <summary>
/// xUnit test collection used to serialize all test classes that drive the static
/// <c>CheckoutSagaMetrics</c> <see cref="System.Diagnostics.Metrics.Meter"/> (named
/// <c>SagaOrchestrators</c>). The meter is process-global, so any test class that
/// (a) drives a saga harness producing <c>saga.checkout.*</c> measurements AND
/// (b) attaches a <see cref="System.Diagnostics.Metrics.MeterListener"/> to assert
/// emission would observe cross-class measurements when xUnit runs the classes in
/// parallel by default.
/// </summary>
/// <remarks>
/// Apply <c>[Collection(nameof(CheckoutMeterSerialCollection))]</c> to every test
/// class that asserts on the meter (currently <c>CheckoutSagaMetricsEmissionTests</c>)
/// AND to every test class whose saga harness produces measurements that those
/// assertions would conflict with (currently <c>CheckoutSagaOrchestratorTests</c>,
/// which exercises the same compensation transitions for state-level assertions).
/// </remarks>
[CollectionDefinition(nameof(CheckoutMeterSerialCollection))]
public sealed class CheckoutMeterSerialCollection;
