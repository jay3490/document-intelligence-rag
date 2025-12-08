using DocumentIntelligence.Core.Answering;
using DocumentIntelligence.Core.Chat;
using DocumentIntelligence.Core.Chunking;
using DocumentIntelligence.Core.Embeddings;
using DocumentIntelligence.Core.Ingestion;
using DocumentIntelligence.Core.Search;
using DocumentIntelligence.Core.Storage;
using DocumentIntelligence.Infrastructure.Ai;
using DocumentIntelligence.Infrastructure.Offline;
using DocumentIntelligence.Infrastructure.Tokenization;
using DocumentIntelligence.Infrastructure.VectorStores;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers chunking, retrieval, answering and ingestion, plus the AI and vector store
    /// providers selected in configuration. All options are validated at start-up.
    /// </summary>
    public static IServiceCollection AddDocumentIntelligence(this IServiceCollection services, IConfiguration configuration)
    {
        AddValidatedOptions<ChunkingOptions>(services, configuration, ChunkingOptions.SectionName);
        AddValidatedOptions<RetrievalOptions>(services, configuration, RetrievalOptions.SectionName);
        AddValidatedOptions<IngestionOptions>(services, configuration, IngestionOptions.SectionName);
        AddValidatedOptions<AiOptions>(services, configuration, AiOptions.SectionName);
        AddValidatedOptions<VectorStoreOptions>(services, configuration, VectorStoreOptions.SectionName);

        services.AddSingleton<ITokenCounter, TiktokenTokenCounter>();
        services.AddSingleton<TextChunker>();
        services.AddSingleton<Bm25Index>();
        services.AddSingleton<HybridRetriever>();
        services.AddSingleton<QuestionAnsweringService>();
        services.AddSingleton<IReranker>(sp =>
        {
            var retrieval = sp.GetRequiredService<IOptions<RetrievalOptions>>().Value;
            var ai = sp.GetRequiredService<IOptions<AiOptions>>().Value;
            return retrieval.Reranker switch
            {
                RerankerKind.None => new NoOpReranker(),
                RerankerKind.Llm when ai.Provider == AiProvider.Offline => throw new InvalidOperationException(
                    $"{RetrievalOptions.SectionName}:{nameof(RetrievalOptions.Reranker)} = Llm needs a chat model; set {AiOptions.SectionName}:{nameof(AiOptions.Provider)} to OpenAICompatible."),
                RerankerKind.Llm => ActivatorUtilities.CreateInstance<LlmReranker>(sp),
                _ => throw new InvalidOperationException($"Unsupported reranker '{retrieval.Reranker}'."),
            };
        });
        services.AddSingleton<IngestionPipeline>();

        services.AddHttpClient(OpenAiHttpClient.Name, (sp, client) =>
            OpenAiHttpClient.Configure(client, sp.GetRequiredService<IOptions<AiOptions>>().Value));

        services.AddHttpClient(QdrantVectorStore.HttpClientName, (sp, client) =>
        {
            var qdrant = sp.GetRequiredService<IOptions<VectorStoreOptions>>().Value.Qdrant;
            client.BaseAddress = new Uri(qdrant.Url.TrimEnd('/') + "/");
            if (!string.IsNullOrWhiteSpace(qdrant.ApiKey))
            {
                client.DefaultRequestHeaders.Add("api-key", qdrant.ApiKey);
            }
        });

        // Providers are chosen when first resolved rather than while registering, so the
        // final configuration (including test and environment overrides) decides.
        services.AddSingleton<IEmbeddingClient>(sp =>
        {
            var ai = sp.GetRequiredService<IOptions<AiOptions>>().Value;
            return ai.Provider switch
            {
                AiProvider.Offline => new HashingEmbeddingClient(ai.EmbeddingDimensions),
                AiProvider.OpenAICompatible => ActivatorUtilities.CreateInstance<OpenAiCompatibleEmbeddingClient>(sp),
                _ => throw new InvalidOperationException($"Unsupported AI provider '{ai.Provider}'."),
            };
        });

        services.AddSingleton<IChatCompletionClient>(sp =>
        {
            var ai = sp.GetRequiredService<IOptions<AiOptions>>().Value;
            return ai.Provider switch
            {
                AiProvider.Offline => new ExtractiveChatClient(),
                AiProvider.OpenAICompatible => ActivatorUtilities.CreateInstance<OpenAiCompatibleChatClient>(sp),
                _ => throw new InvalidOperationException($"Unsupported AI provider '{ai.Provider}'."),
            };
        });

        services.AddSingleton<IVectorStore>(sp =>
        {
            var store = sp.GetRequiredService<IOptions<VectorStoreOptions>>().Value;
            var dimensions = sp.GetRequiredService<IOptions<AiOptions>>().Value.EmbeddingDimensions;
            return store.Provider switch
            {
                VectorStoreProvider.InMemory => new InMemoryVectorStore(dimensions),
                VectorStoreProvider.Qdrant => ActivatorUtilities.CreateInstance<QdrantVectorStore>(sp),
                _ => throw new InvalidOperationException($"Unsupported vector store '{store.Provider}'."),
            };
        });

        return services;
    }

    private static void AddValidatedOptions<TOptions>(IServiceCollection services, IConfiguration configuration, string section)
        where TOptions : class
    {
        services.AddOptions<TOptions>()
            .Bind(configuration.GetSection(section))
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }
}
