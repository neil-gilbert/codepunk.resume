using lucidRESUME.JobSpec.Extraction;

namespace lucidRESUME.JobSpec.Tests;

public sealed class JdFieldFuserTests
{
    [Fact]
    public void Fuse_DoesNotPromoteNerOnlyFragmentsToRequirements()
    {
        JdFieldCandidate[] candidates =
        [
            new("skill", "Turning", 0.91, "ner:onnx"),
            new("skill", "ASP.NET Core", 0.80, "llm")
        ];

        var fused = JdFieldFuser.Fuse(candidates);

        Assert.DoesNotContain(fused.Skills, skill => skill.Value == "Turning");
        Assert.Contains(fused.Skills, skill => skill.Value == "ASP.NET Core");
    }

    [Fact]
    public void Fuse_DoesNotPromoteSingleWordLlmSentenceFragments()
    {
        JdFieldCandidate[] candidates =
        [
            new("skill", "Turning", 0.80, "llm"),
            new("skill", "delivery governance", 0.80, "llm")
        ];

        var fused = JdFieldFuser.Fuse(candidates);

        Assert.DoesNotContain(fused.Skills, skill => skill.Value == "Turning");
        Assert.Contains(fused.Skills, skill => skill.Value == "delivery governance");
    }

    [Theory]
    [InlineData("Turning")]
    [InlineData("Prototypes")]
    [InlineData("Relevant")]
    public void Fuse_RejectsKnownNonSkillFragmentsEvenFromTaxonomy(string fragment)
    {
        var fused = JdFieldFuser.Fuse([
            new JdFieldCandidate("skill", fragment, 0.75, "taxonomy")
        ]);

        Assert.Empty(fused.Skills);
    }

    [Fact]
    public void Fuse_KeepsNerWhenAnotherExtractorCorroboratesIt()
    {
        JdFieldCandidate[] candidates =
        [
            new("skill", "Kubernetes", 0.70, "ner:onnx"),
            new("skill", "Kubernetes", 0.75, "taxonomy")
        ];

        var skill = Assert.Single(JdFieldFuser.Fuse(candidates).Skills);

        Assert.Equal("Kubernetes", skill.Value);
        Assert.Equal(2, skill.Sources.Count);
        Assert.True(skill.Confidence > 0.75);
    }
}
