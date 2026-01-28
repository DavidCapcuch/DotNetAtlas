namespace DotNetAtlas.KafkaFlow.DeadLetter;

/// <summary>
/// Header keys added to messages sent to Dead Letter Topics.
/// </summary>
public static class DltHeaders
{
    public const string OriginalTopic = "DLT-Original-Topic";

    public const string OriginalPartition = "DLT-Original-Partition";

    public const string OriginalOffset = "DLT-Original-Offset";

    public const string ExceptionType = "DLT-Exception-Type";

    public const string ExceptionMessage = "DLT-Exception-Message";

    public const string ExceptionStackTrace = "DLT-Exception-StackTrace";
}
