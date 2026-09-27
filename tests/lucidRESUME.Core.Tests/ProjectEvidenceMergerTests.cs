using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.Core.Tests;

public sealed class ProjectEvidenceMergerTests
{
    [Fact]
    public void Matching_audit_refresh_preserves_human_prose_and_attaches_observations()
    {
        var resume = ResumeDocument.Create("career.md", "text/markdown", 100);
        resume.Projects.Add(new Project
        {
            Name = "LucidRAG",
            Description = "My reviewed, detailed account of why I built the product.",
            Url = "https://github.com/scottgal/lucidrag",
            Technologies = ["C#"],
            ImportSources = ["career.md"]
        });
        var audit = AuditProject("LucidRAG", "https://github.com/scottgal/lucidrag", revision: "abc");
        audit.EvidenceMetadata["eligible_for_personal_evidence"] = "false";

        ProjectEvidenceMerger.MergeAuditObservations(resume, [audit]);

        var merged = Assert.Single(resume.Projects);
        Assert.Equal("My reviewed, detailed account of why I built the product.", merged.Description);
        Assert.Contains("C#", merged.Technologies);
        Assert.Contains("RAG", merged.Technologies);
        Assert.Equal("abc", merged.EvidenceMetadata["revision"]);
        Assert.Equal("true", merged.EvidenceMetadata["eligible_for_personal_evidence"]);
        Assert.Equal("true", merged.EvidenceMetadata["human_prose_preserved"]);
    }

    [Fact]
    public void Existing_audit_record_is_replaced_by_the_latest_observation()
    {
        var resume = ResumeDocument.Create("career.md", "text/markdown", 100);
        resume.Projects.Add(AuditProject("LucidRAG", "https://github.com/scottgal/lucidrag", revision: "old"));
        var latest = AuditProject("LucidRAG", "https://github.com/scottgal/lucidrag", revision: "new");

        ProjectEvidenceMerger.MergeAuditObservations(resume, [latest]);

        var merged = Assert.Single(resume.Projects);
        Assert.Same(latest, merged);
        Assert.Equal("new", merged.EvidenceMetadata["revision"]);
    }

    private static Project AuditProject(string name, string url, string revision) => new()
    {
        Name = name,
        Description = "Machine-observed repository summary.",
        Url = url,
        Technologies = ["RAG"],
        ImportSources = ["GitHub repository audit"],
        EvidenceMetadata = new Dictionary<string, string>
        {
            ["provider"] = "github",
            ["revision"] = revision,
            ["eligible_for_personal_evidence"] = "true"
        }
    };
}
