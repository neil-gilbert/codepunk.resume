using lucidRESUME.Parsing;

namespace lucidRESUME.Core.Tests.Parsing;

public sealed class MarkdownDirectParserTests
{
    [Theory]
    [InlineData(".md")]
    [InlineData(".markdown")]
    public async Task ParseAsync_PreservesCanonicalMarkdown(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"resume-{Guid.NewGuid():N}{extension}");
        const string markdown = "# Avery Example\n\n## Experience\n\nHuman-authored prose.\n";

        try
        {
            await File.WriteAllTextAsync(path, markdown);

            var parsed = await new MarkdownDirectParser().ParseAsync(path);

            Assert.NotNull(parsed);
            Assert.Equal(markdown, parsed.Markdown);
            Assert.Equal(markdown, parsed.PlainText);
            Assert.Equal(1.0, parsed.Confidence);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
