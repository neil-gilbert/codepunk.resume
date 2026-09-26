namespace lucidRESUME.Parsing;

/// <summary>
/// Reads canonical Markdown without converting or normalising its prose. Section and
/// evidence parsing is performed later by the ingestion pipeline so anchors remain stable.
/// </summary>
public sealed class MarkdownDirectParser : IDocumentParser
{
    public IReadOnlyList<string> SupportedExtensions { get; } = [".md", ".markdown"];

    public async Task<ParsedDocument?> ParseAsync(string filePath, CancellationToken ct = default)
    {
        var markdown = await File.ReadAllTextAsync(filePath, ct);
        if (string.IsNullOrWhiteSpace(markdown))
            return null;

        return new ParsedDocument
        {
            Markdown = markdown,
            PlainText = markdown,
            Confidence = 1.0,
            PageCount = 1
        };
    }
}
