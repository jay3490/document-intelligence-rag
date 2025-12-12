using System.ComponentModel.DataAnnotations;

namespace DocumentIntelligence.Infrastructure.Persistence;

public enum DatabaseProvider
{
    /// <summary>A local SQLite file (or in-memory database); the schema is created directly.</summary>
    Sqlite,

    /// <summary>PostgreSQL; the schema is managed by EF Core migrations.</summary>
    Postgres,
}

public sealed class PersistenceOptions
{
    public const string SectionName = "Persistence";

    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Sqlite;

    [Required]
    public string ConnectionString { get; set; } = "Data Source=data/ingestion.db";

    /// <summary>
    /// Apply pending migrations (PostgreSQL) or create the schema (SQLite) at start-up. Enable
    /// it on one service only (the API) so that two processes never migrate at once.
    /// </summary>
    public bool InitializeOnStartup { get; set; } = true;
}
