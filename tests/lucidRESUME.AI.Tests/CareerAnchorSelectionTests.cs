using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.AI.Tests;

public sealed class CareerAnchorSelectionTests
{
    [Theory]
    [InlineData("Head of Software", 6)]
    [InlineData("Contract Head of Development", 6)]
    [InlineData("Head of Engineering", 6)]
    [InlineData("Lead Developer", 4)]
    [InlineData("Development Lead", 2)]
    public void Engineering_lead_targets_understand_the_leadership_title_hierarchy(
        string candidateTitle, double expectedSignal)
    {
        Assert.Equal(expectedSignal, SemanticCompressor.RoleTitleSignal(candidateTitle, "Lead Developer"));
    }

    [Fact]
    public void Author_anchors_reserve_slots_before_relevance_ranking()
    {
        var microsoft = Role("Microsoft Corp", 2007, isAnchor: true);
        var dell = Role("Dell Ltd", 2011);
        var recent = Enumerable.Range(0, 7)
            .Select(index => Role($"Recent {index}", 2026 - index))
            .ToList();
        var roles = recent.Append(microsoft).Append(dell).ToList();
        var scores = recent.Select((role, index) => (role.Id, Score: 100d - index))
            .Concat([(microsoft.Id, 0d), (dell.Id, 0d)])
            .ToDictionary(item => item.Id, item => item.Item2);

        var selected = SemanticCompressor.SelectDetailedRoleIds(
            roles, scores, new HashSet<string>([SemanticCompressor.NormalizeCompany("Dell")]), 4);

        Assert.Equal(6, selected.Count);
        Assert.Contains(microsoft.Id, selected);
        Assert.Contains(dell.Id, selected);
        Assert.Equal(4, recent.Count(role => selected.Contains(role.Id)));
    }

    private static WorkExperience Role(string company, int year, bool isAnchor = false) => new()
    {
        Company = company,
        Title = "Software Engineer",
        StartDate = new DateOnly(year, 1, 1),
        EndDate = new DateOnly(year, 12, 1),
        IsCareerAnchor = isAnchor,
        Achievements = ["Built and shipped production software."]
    };
}
