using System.Text.Json.Serialization;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.GitHub.Models;

public sealed class NuGetSearchResponse
{
    [JsonPropertyName("totalHits")]
    public int TotalHits { get; set; }

    [JsonPropertyName("data")]
    public List<NuGetPackageRecord> Data { get; set; } = [];
}

public sealed class NuGetPackageRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("authors")]
    public List<string> Authors { get; set; } = [];

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("projectUrl")]
    public string? ProjectUrl { get; set; }

    [JsonPropertyName("registration")]
    public string? Registration { get; set; }

    [JsonPropertyName("totalDownloads")]
    public long TotalDownloads { get; set; }

    [JsonPropertyName("verified")]
    public bool Verified { get; set; }
}

public sealed class NuGetPackageFamily
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string? SourceRepository { get; init; }
    public int PackageCount { get; init; }
    public long TotalDownloads { get; init; }
    public List<NuGetPackageRecord> Packages { get; init; } = [];
    public List<string> SearchTerms { get; init; } = [];
    public Project ProjectEvidence { get; init; } = new();
}

public sealed class NuGetPackageAuditResult
{
    public string Publisher { get; init; } = "";
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.UtcNow;
    public int PackageCount { get; init; }
    public long TotalDownloads { get; init; }
    public List<NuGetPackageFamily> Families { get; init; } = [];
    public List<Project> Projects => Families.Select(family => family.ProjectEvidence).ToList();
}
