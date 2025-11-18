using DocumentIntelligence.Core.Search;
using DocumentIntelligence.Infrastructure;
using DocumentIntelligence.Infrastructure.Ai;
using DocumentIntelligence.Infrastructure.Offline;
using DocumentIntelligence.Infrastructure.VectorStores;
using DocumentIntelligence.Core.Chat;
using DocumentIntelligence.Core.Embeddings;
using DocumentIntelligence.Core.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.UnitTests.Infrastructure;

public sealed class OptionsValidationTests
{
    [Fact]
    public void Defaults_resolve_to_the_offline_providers()
    {
        using var services = Build([]);

        Assert.IsType<HashingEmbeddingClient>(services.GetRequiredService<IEmbeddingClient>());
        Assert.IsType<ExtractiveChatClient>(services.GetRequiredService<IChatCompletionClient>());
        Assert.IsType<InMemoryVectorStore>(services.GetRequiredService<IVectorStore>());
    }

    [Fact]
    public void Configuration_selects_the_http_providers()
    {
        using var services = Build(new()
        {
            ["Ai:Provider"] = "OpenAICompatible",
            ["Ai:BaseUrl"] = "http://localhost:11434/v1",
            ["VectorStore:Provider"] = "Qdrant",
        });

        Assert.IsType<OpenAiCompatibleEmbeddingClient>(services.GetRequiredService<IEmbeddingClient>());
        Assert.IsType<OpenAiCompatibleChatClient>(services.GetRequiredService<IChatCompletionClient>());
        Assert.IsType<QdrantVectorStore>(services.GetRequiredService<IVectorStore>());
    }

    [Theory]
    [InlineData("Ai:BaseUrl", "not a url", "Ai:Provider", "OpenAICompatible")]
    [InlineData("Ai:EmbeddingDimensions", "4", "Ai:Provider", "Offline")]
    [InlineData("VectorStore:Qdrant:Collection", "bad name!", "VectorStore:Provider", "Qdrant")]
    [InlineData("Chunking:OverlapTokens", "300", "Chunking:MaxTokens", "256")]
    [InlineData("Retrieval:CandidatesPerRetriever", "2", "Retrieval:DefaultTopK", "5")]
    public void Invalid_settings_are_rejected(string key, string value, string otherKey, string otherValue)
    {
        using var services = Build(new() { [key] = value, [otherKey] = otherValue });

        Assert.ThrowsAny<OptionsValidationException>(() =>
        {
            _ = services.GetRequiredService<IOptions<AiOptions>>().Value;
            _ = services.GetRequiredService<IOptions<VectorStoreOptions>>().Value;
            _ = services.GetRequiredService<IOptions<Core.Chunking.ChunkingOptions>>().Value;
            _ = services.GetRequiredService<IOptions<RetrievalOptions>>().Value;
        });
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddDocumentIntelligence(configuration)
            .BuildServiceProvider();
    }
}
