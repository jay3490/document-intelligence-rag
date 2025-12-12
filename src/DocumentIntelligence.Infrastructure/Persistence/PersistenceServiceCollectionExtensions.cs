using DocumentIntelligence.Core.Ingestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace DocumentIntelligence.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the EF Core job store and a start-up initializer for the configured database.
    /// </summary>
    public static IServiceCollection AddIngestionJobPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PersistenceOptions>()
            .Bind(configuration.GetSection(PersistenceOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContextFactory<IngestionDbContext>((sp, builder) =>
            Configure(builder, sp.GetRequiredService<IOptions<PersistenceOptions>>().Value));
        services.AddSingleton<IIngestionJobStore, EfIngestionJobStore>();
        services.AddSingleton<DatabaseInitializer>();
        services.AddHostedService(sp => sp.GetRequiredService<DatabaseInitializer>());
        return services;
    }

    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder builder, PersistenceOptions options) =>
        options.Provider switch
        {
            DatabaseProvider.Postgres => builder.UseNpgsql(options.ConnectionString),
            DatabaseProvider.Sqlite => builder.UseSqlite(options.ConnectionString),
            _ => throw new InvalidOperationException($"Unsupported database provider '{options.Provider}'."),
        };
}

/// <summary>
/// Lets <c>dotnet ef migrations add</c> build the PostgreSQL model without starting a host.
/// Migrations are generated for PostgreSQL only; SQLite databases are created from the model.
/// </summary>
public sealed class DesignTimeIngestionDbContextFactory : IDesignTimeDbContextFactory<IngestionDbContext>
{
    public IngestionDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<IngestionDbContext>();
        builder.UseNpgsql("Host=localhost;Database=document_intelligence;Username=postgres;Password=postgres");
        return new IngestionDbContext(builder.Options);
    }
}
