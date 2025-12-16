namespace DocumentIntelligence.Core.Ingestion;

/// <summary>A condition consumers wait for before they start taking ingestion messages.</summary>
public interface IIngestionReadiness
{
    Task WaitUntilReadyAsync(CancellationToken cancellationToken);
}
