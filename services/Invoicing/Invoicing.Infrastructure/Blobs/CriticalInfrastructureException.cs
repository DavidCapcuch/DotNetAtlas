using Platform.SharedKernel.Exceptions;

namespace Invoicing.Infrastructure.Blobs;

/// <summary>
/// Infrastructure-scoped critical exception signalling an irrecoverable failure against
/// external storage (Azurite / Azure Blob). Derives from
/// <see cref="CriticalException"/> so the same <c>catch (CriticalException)</c> handler
/// in command-handler and Kafka-consumer pipelines treats it uniformly alongside
/// <see cref="DataIntegrityException"/>. Mapped to <c>InvoicingErrors.BlobUploadFailed</c>
/// at the command-handler boundary (M7); DLT-routed when it bubbles out of a consumer.
/// </summary>
public sealed class CriticalInfrastructureException : CriticalException
{
    public CriticalInfrastructureException(string errorCode, string message, Exception innerException)
        : base(errorCode, message, innerException)
    {
    }
}
