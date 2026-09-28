using Markdig;

namespace lucidRESUME.Web;

public sealed class MarkdigResumeMarkdownRenderer : IResumeMarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    public string ToHtml(string markdown) => Markdown.ToHtml(markdown, Pipeline);
}
