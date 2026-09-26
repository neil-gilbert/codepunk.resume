using System.Text.RegularExpressions;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.JobML;

namespace lucidRESUME.Export;

internal static partial class ExportArtifact
{
    public static ResumeTemplate Template(ResumeDocument resume) =>
        ResumeTemplateCatalog.Get(resume.OutputTemplateId);

    public static string? JobMlYaml(ResumeDocument resume)
    {
        if (string.IsNullOrWhiteSpace(resume.JobMlSource)) return null;
        var match = JobMlFence().Match(resume.JobMlSource);
        return match.Success ? match.Groups["yaml"].Value.Trim() : null;
    }

    public static CJobMlProjection? CompactJobMl(ResumeDocument resume)
    {
        if (!resume.IncludeCompactJobMl || string.IsNullOrWhiteSpace(resume.JobMlSource)) return null;
        var parser = new JobMlParser();
        return parser.TryParse(resume.JobMlSource, out var file, out _)
            ? CJobMlProjector.Project(file!)
            : null;
    }

    public static IReadOnlyList<int> CitationNumbers(string text, CJobMlProjection? projection)
    {
        if (projection is null || string.IsNullOrWhiteSpace(text)) return [];
        var normalized = NormalizeRenderedText(text);
        return projection.Anchors
            .Where(anchor => string.Equals(NormalizeRenderedText(anchor.ProseText), normalized,
                StringComparison.Ordinal))
            .SelectMany(anchor => anchor.ReferenceNumbers)
            .Distinct()
            .Order()
            .ToList();
    }

    private static string NormalizeRenderedText(string text)
    {
        var normalized = MarkdownEvidenceIndex.NormalizeText(text);
        // Markdown evidence passages retain their list prefix. DOCX and PDF renderers
        // supply the structured achievement without that presentation character.
        // Removing only a leading list marker preserves an exact, deterministic match;
        // this is not fuzzy evidence reconciliation at render time.
        return Regex.Replace(normalized, @"^(?:[-+*]|\d+[.)])\s+", "");
    }

    public const string MachineArticleUrl = JobMlArtifactComposer.ArticleUrl;

    [GeneratedRegex(@"(?ms)^\s*```jobml\s*\n(?<yaml>.*?)^\s*```\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex JobMlFence();
}
