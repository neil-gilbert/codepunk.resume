using lucidRESUME.GitHub;
using lucidRESUME.GitHub.Models;

namespace lucidRESUME.GitHub.Tests;

public sealed class NuGetPackageAuditServiceTests
{
    [Fact]
    public void Packages_with_the_same_source_repository_form_one_searchable_family()
    {
        var packages = new List<NuGetPackageRecord>
        {
            Package("Mostlylucid.LucidRAG.Core", 1_200, "https://github.com/scottgal/lucidrag/tree/main/src"),
            Package("Mostlylucid.LucidRAG.DocSummarizer", 800, "https://github.com/scottgal/lucidrag")
        };

        var keys = packages.Select(NuGetPackageAuditService.FamilyKey).Distinct().ToList();
        var family = NuGetPackageAuditService.BuildFamily(Assert.Single(keys), packages);

        Assert.Equal("LucidRAG", family.Name);
        Assert.Equal(2, family.PackageCount);
        Assert.Equal(2_000, family.TotalDownloads);
        Assert.Equal("https://github.com/scottgal/lucidrag", family.SourceRepository);
        Assert.Contains("LucidRAG", family.ProjectEvidence.Name);
        Assert.Contains("2 package IDs", family.ProjectEvidence.Description);
        Assert.Contains("NuGet package registry audit", family.ProjectEvidence.ImportSources);
        Assert.Equal(family.ProjectEvidence.Id,
            NuGetPackageAuditService.BuildFamily(Assert.Single(keys), packages).ProjectEvidence.Id);
    }

    [Fact]
    public void Packages_without_project_urls_group_by_product_prefix_not_global_publisher()
    {
        var ephemeral = Package("Mostlylucid.Ephemeral.Atoms.Retry", 100, null);
        var notify = Package("Mostlylucid.Notify", 50, null);

        Assert.Equal("nuget:ephemeral", NuGetPackageAuditService.FamilyKey(ephemeral));
        Assert.Equal("nuget:notify", NuGetPackageAuditService.FamilyKey(notify));
    }

    [Fact]
    public void Stable_family_prefix_joins_packages_even_when_project_url_is_missing()
    {
        var withSource = Package("Mostlylucid.LucidRAG.Core", 100, "https://github.com/scottgal/lucidrag");
        var withoutSource = Package("Mostlylucid.LucidRAG.LLM", 50, null);

        Assert.Equal("nuget:lucidrag", NuGetPackageAuditService.FamilyKey(withSource));
        Assert.Equal(NuGetPackageAuditService.FamilyKey(withSource),
            NuGetPackageAuditService.FamilyKey(withoutSource));
    }

    [Fact]
    public void Registry_downloads_are_described_as_observations_not_skill_claims()
    {
        var package = Package("Mostlylucid.ConsoleImage", 12_345, null);

        var family = NuGetPackageAuditService.BuildFamily(
            NuGetPackageAuditService.FamilyKey(package), [package]);

        Assert.Contains("registry downloads at audit time", family.ProjectEvidence.Description);
        Assert.DoesNotContain("expert", family.ProjectEvidence.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("proficient", family.ProjectEvidence.Description, StringComparison.OrdinalIgnoreCase);
    }

    private static NuGetPackageRecord Package(string id, long downloads, string? projectUrl) => new()
    {
        Id = id,
        Version = "1.0.0",
        TotalDownloads = downloads,
        ProjectUrl = projectUrl,
        Authors = ["Scott Galloway"],
        Tags = ["dotnet", "retrieval"]
    };
}
