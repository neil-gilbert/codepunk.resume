using lucidRESUME.JobML;

namespace lucidRESUME.JobML.Tests;

public sealed class CJobMlProjectionTests
{
    [Fact]
    public void Projection_IsCompactJatsLikeAndRoundTripsThroughOnePassParser()
    {
        var full = AcceptedFile();

        var compact = CJobMlProjector.Project(full);
        var parsed = CJobMlParser.Parse(compact.Markdown);

        Assert.Contains("platform. [[1]](#ref-1)", compact.Markdown);
        Assert.Contains("## References", compact.Markdown);
        Assert.Contains("cJobML 0.1: xref [n] in prose resolves to ref [n].", compact.Markdown);
        Assert.Contains("Full JobML: <https://example.com/jane.jobml>", compact.Markdown);
        Assert.Contains("[Article] <https://mostlylucid.net/reduced-rag>", compact.Markdown);
        Assert.DoesNotContain("fingerprint", compact.Markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("selector", compact.Markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("```jobml", compact.Markdown);

        Assert.Equal([1], parsed.Xrefs);
        var reference = Assert.Single(parsed.References);
        Assert.Equal("Reduced RAG", reference.Title);
        Assert.Equal("Article", reference.Type);
        Assert.Equal("https://mostlylucid.net/reduced-rag", reference.Uri!.ToString().TrimEnd('/'));
        Assert.Equal("https://example.com/jane.jobml", parsed.FullJobMl!.ToString().TrimEnd('/'));
    }

    [Fact]
    public void Projection_DeduplicatesEvidenceAndReusesNumber()
    {
        var full = AcceptedFile();
        var second = JobMlDraftGenerator.Generate("# Jane\n\n## Experience\n\nBuilt a second evidence-linked system.");
        var secondClaim = Assert.Single(second.Data.Claims);
        secondClaim.Review = "accepted";
        secondClaim.Id = "projects-claim-1";
        secondClaim.Evidence.Add(full.Data.Claims[0].Evidence[1]);
        full = full with
        {
            Markdown = full.Markdown + "\n\n## Projects {#projects}\n\nBuilt a second evidence-linked system."
        };
        secondClaim.Subject = "projects";
        secondClaim.Evidence[0].Ref = "#projects:p1";
        secondClaim.Evidence[0].Fingerprint = new JobMlFingerprint
        {
            Text = MarkdownEvidenceIndex.Fingerprint("Built a second evidence-linked system.")
        };
        secondClaim.Evidence[0].Selector = new JobMlTextSelector { Exact = "Built a second evidence-linked system." };
        full.Data.Entities.Add(new JobMlEntity { Id = "projects", Type = "project", Name = "Projects", Source = "#projects" });
        full.Data.Claims.Add(secondClaim);

        var compact = CJobMlProjector.Project(full);

        Assert.Single(compact.References);
        Assert.Equal(2, compact.Anchors.Count);
        Assert.All(compact.Anchors, anchor => Assert.Equal([1], anchor.ReferenceNumbers));
    }

    [Fact]
    public void Projection_DoesNotPublishUnreviewedDerivedClaims()
    {
        var full = AcceptedFile();
        full.Data.Claims[0].Review = "required";

        var compact = CJobMlProjector.Project(full);

        Assert.Empty(compact.References);
        Assert.DoesNotContain("## References", compact.Markdown);
    }

    [Fact]
    public void Projection_RejectsAcceptedClaimWhoseProseHasDrifted()
    {
        var full = AcceptedFile() with
        {
            Markdown = AcceptedFile().Markdown.Replace("Built an evidence-linked retrieval platform.",
                "Contributed to an evidence-linked retrieval platform.", StringComparison.Ordinal)
        };

        var error = Assert.Throws<JobMlProjectionException>(() => CJobMlProjector.Project(full));

        Assert.Contains("changed", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Projection_PublishesFullJobMlEndpointWithoutExternalReferences()
    {
        var full = AcceptedFile();
        full.Data.Claims.Single().Evidence.RemoveAll(evidence =>
            !string.Equals(evidence.Type, "prose", StringComparison.OrdinalIgnoreCase));

        var compact = CJobMlProjector.Project(full);

        Assert.Empty(compact.References);
        Assert.Contains("## References", compact.Markdown);
        Assert.Contains("Full JobML: <https://example.com/jane.jobml>", compact.Markdown);
        var parsed = CJobMlParser.Parse(compact.Markdown);
        Assert.Empty(parsed.References);
        Assert.Equal("https://example.com/jane.jobml", parsed.FullJobMl?.ToString());
    }

    [Fact]
    public void Projection_CitesImportedResumeSourceWithoutPublishingEditingMetadata()
    {
        var full = AcceptedFile();
        full.Data.Document.FullJobMl = null;
        var claim = full.Data.Claims.Single();
        claim.Evidence.RemoveAll(evidence =>
            !string.Equals(evidence.Type, "prose", StringComparison.OrdinalIgnoreCase));
        claim.Evidence.Add(new JobMlEvidence
        {
            Id = "evidence:source:achievement:1",
            Type = "source_ledger",
            Ref = "ledger://evidence:source:achievement:1",
            Title = "original-resume.docx",
            Fingerprint = new JobMlFingerprint { Text = "fnv1a64:1234" },
            Selector = new JobMlTextSelector { Exact = "Original supporting passage." }
        });

        var compact = CJobMlProjector.Project(full);
        var parsed = CJobMlParser.Parse(compact.Markdown);

        Assert.Contains("platform. [[1]](#ref-1)", compact.Markdown);
        Assert.Contains("“original-resume.docx.” [Resume Source]", compact.Markdown);
        Assert.DoesNotContain("Original supporting passage", compact.Markdown);
        Assert.DoesNotContain("fnv1a64", compact.Markdown);
        Assert.DoesNotContain("ledger://", compact.Markdown);
        Assert.Single(parsed.References);
    }

    [Fact]
    public void Projection_DoesNotMergeDifferentTranscriptSectionsOnSameEndpoint()
    {
        var full = AcceptedFile();
        var firstClaim = full.Data.Claims.Single();
        firstClaim.Evidence.RemoveAll(evidence =>
            !string.Equals(evidence.Type, "prose", StringComparison.OrdinalIgnoreCase));
        firstClaim.Evidence.Add(new JobMlEvidence
        {
            Id = "transcript-role-one",
            Type = "career_transcript",
            Uri = "https://example.com/jane.jobml#role-one",
            Title = "Complete transcript: Role One"
        });

        const string secondPassage = "Led another platform team.";
        full = full with { Markdown = full.Markdown + $"\n\n## Role Two {{#role-two}}\n\n{secondPassage}" };
        full.Data.Entities.Add(new JobMlEntity
            { Id = "role-two", Type = "experience", Name = "Role Two", Source = "#role-two" });
        full.Data.Claims.Add(new JobMlClaim
        {
            Id = "role-two-claim",
            Subject = "role-two",
            Statement = secondPassage,
            Review = "accepted",
            Evidence =
            [
                new JobMlEvidence
                {
                    Type = "prose", Ref = "#role-two:p1",
                    Fingerprint = new JobMlFingerprint { Text = MarkdownEvidenceIndex.Fingerprint(secondPassage) },
                    Selector = new JobMlTextSelector { Exact = secondPassage }
                },
                new JobMlEvidence
                {
                    Id = "transcript-role-two",
                    Type = "career_transcript",
                    Uri = "https://example.com/jane.jobml#role-two",
                    Title = "Complete transcript: Role Two"
                }
            ]
        });

        var compact = CJobMlProjector.Project(full);
        var parsed = CJobMlParser.Parse(compact.Markdown);

        Assert.Equal(2, compact.References.Count);
        Assert.Contains("#role-one", compact.References[0].Evidence.Uri);
        Assert.Contains("#role-two", compact.References[1].Evidence.Uri);
        Assert.Equal([1], compact.Anchors[0].ReferenceNumbers);
        Assert.Equal([2], compact.Anchors[1].ReferenceNumbers);
        Assert.Equal(["role-one", "role-two"], parsed.References
            .Select(reference => reference.Uri!.Fragment.TrimStart('#')).ToArray());
    }

    [Fact]
    public void Parser_RejectsUnresolvedXref()
    {
        var compact = CJobMlProjector.Project(AcceptedFile()).Markdown
            .Replace("[[1]](#ref-1)", "[[2]](#ref-2)", StringComparison.Ordinal);

        var error = Assert.Throws<CJobMlParseException>(() => CJobMlParser.Parse(compact));

        Assert.Contains("Unresolved", error.Message);
    }

    [Fact]
    public void Parser_RejectsReferenceWhichIsNotCited()
    {
        var compact = CJobMlProjector.Project(AcceptedFile()).Markdown
            .Replace("platform. [[1]](#ref-1)", "platform.", StringComparison.Ordinal);

        var error = Assert.Throws<CJobMlParseException>(() => CJobMlParser.Parse(compact));

        Assert.Contains("Uncited", error.Message);
    }

    [Fact]
    public void Parser_UsesFinalExactReferencesHeading()
    {
        var file = AcceptedFile() with
        {
            Markdown = AcceptedFile().Markdown.Replace("## Experience", "## References in prior work\n\nContext.\n\n## Experience",
                StringComparison.Ordinal)
        };

        var compact = CJobMlProjector.Project(file).Markdown;
        var parsed = CJobMlParser.Parse(compact);

        Assert.Single(parsed.References);
    }

    private static JobMlFile AcceptedFile()
    {
        var file = JobMlDraftGenerator.Generate(
            "# Jane\n\n## Experience\n\nBuilt an evidence-linked retrieval platform.");
        file.Data.Document.FullJobMl = "https://example.com/jane.jobml";
        var claim = Assert.Single(file.Data.Claims);
        claim.Review = "accepted";
        claim.Evidence.Add(new JobMlEvidence
        {
            Id = "reduced-rag",
            Type = "article",
            Uri = "https://mostlylucid.net/reduced-rag",
            Title = "Reduced RAG",
            Authors = ["S. Galloway"],
            Publisher = "MostlyLucid",
            Published = "2025-04-12"
        });
        return file;
    }
}
