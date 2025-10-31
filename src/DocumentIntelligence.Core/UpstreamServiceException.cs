namespace DocumentIntelligence.Core;

/// <summary>
/// Raised when a dependency outside this process (the vector database, an embedding or chat
/// endpoint) fails or returns something unusable.
/// </summary>
public sealed class UpstreamServiceException : Exception
{
    public UpstreamServiceException(string service, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Service = service;
    }

    /// <summary>A short name for the failing dependency, for example "qdrant" or "chat".</summary>
    public string Service { get; }
}
