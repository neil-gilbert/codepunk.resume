using System.Text.Json.Serialization;

namespace lucidRESUME.GitHub.Models;

public sealed class GitHubRepo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";

    [JsonPropertyName("default_branch")]
    public string DefaultBranch { get; set; } = "main";

    [JsonPropertyName("fork")]
    public bool Fork { get; set; }

    [JsonPropertyName("archived")]
    public bool Archived { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("pushed_at")]
    public DateTimeOffset PushedAt { get; set; }

    [JsonPropertyName("stargazers_count")]
    public int StargazersCount { get; set; }

    [JsonPropertyName("forks_count")]
    public int ForksCount { get; set; }

    [JsonPropertyName("open_issues_count")]
    public int OpenIssuesCount { get; set; }

    [JsonPropertyName("size")]
    public int Size { get; set; }

    [JsonPropertyName("topics")]
    public string[] Topics { get; set; } = [];

    [JsonPropertyName("license")]
    public GitHubLicense? License { get; set; }
}

public sealed class GitHubLicense
{
    [JsonPropertyName("spdx_id")]
    public string? SpdxId { get; set; }
}

public sealed class GitHubTree
{
    [JsonPropertyName("sha")]
    public string Sha { get; set; } = "";

    [JsonPropertyName("truncated")]
    public bool Truncated { get; set; }

    [JsonPropertyName("tree")]
    public List<GitHubTreeEntry> Entries { get; set; } = [];
}

public sealed class GitHubTreeEntry
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("size")]
    public long? Size { get; set; }
}
