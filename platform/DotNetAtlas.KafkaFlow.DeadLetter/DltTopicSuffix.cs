namespace DotNetAtlas.KafkaFlow.DeadLetter;

/// <summary>
/// Wrapper for the DLT topic suffix to enable DI resolution.
/// </summary>
internal sealed record DltTopicSuffix(string Value);
