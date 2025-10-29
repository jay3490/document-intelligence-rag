namespace DocumentIntelligence.Core.Documents;

/// <summary>The text formats the ingestion pipeline understands.</summary>
public enum DocumentFormat
{
    PlainText,
    Markdown,
}

public static class DocumentFormats
{
    private static readonly Dictionary<string, DocumentFormat> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".txt"] = DocumentFormat.PlainText,
        [".text"] = DocumentFormat.PlainText,
        [".md"] = DocumentFormat.Markdown,
        [".markdown"] = DocumentFormat.Markdown,
    };

    public static IReadOnlyCollection<string> SupportedExtensions => ByExtension.Keys;

    public static bool TryFromFileName(string fileName, out DocumentFormat format) =>
        ByExtension.TryGetValue(Path.GetExtension(fileName), out format);
}
