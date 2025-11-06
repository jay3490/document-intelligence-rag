namespace DocumentIntelligence.Infrastructure.VectorStores;

public enum VectorStoreProvider
{
    /// <summary>Process-local storage; nothing survives a restart.</summary>
    InMemory,
}

public sealed class VectorStoreOptions
{
    public const string SectionName = "VectorStore";

    public VectorStoreProvider Provider { get; set; } = VectorStoreProvider.InMemory;
}
