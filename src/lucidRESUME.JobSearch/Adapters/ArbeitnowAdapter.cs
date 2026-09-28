using System.Net.Http.Json;
using System.Text.Json.Serialization;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Jobs;

namespace lucidRESUME.JobSearch.Adapters;

/// <summary>Arbeitnow - free job board API, no API key required</summary>
public sealed class ArbeitnowAdapter : IJobSearchAdapter
{
    private readonly HttpClient _http;
    public string AdapterName => "Arbeitnow";
    public bool IsConfigured => true;

    public ArbeitnowAdapter(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<JobDescription>> SearchAsync(JobSearchQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        // Arbeitnow does not support keyword search on the public API - fetch page 1 and filter client-side
        var url = "https://www.arbeitnow.com/api/job-board-api?page=1";
        var response = await _http.GetFromJsonAsync<ArbeitnowResponse>(url, ct);
        if (response?.Data is null) return [];

        var keyword = query.Keywords;
        var results = response.Data
            .Where(j => string.IsNullOrWhiteSpace(keyword) ||
                        (j.Title?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true) ||
                        (j.Tags?.Any(t => t.Contains(keyword, StringComparison.OrdinalIgnoreCase)) == true))
            .Take(query.MaxResults)
            .Select(ToJobDescription)
            .ToList();

        return results;
    }

    private static JobDescription ToJobDescription(ArbeitnowJob j)
    {
        var job = JobDescription.Create(j.Description ?? "", new JobSource
        {
            Type = JobSourceType.Arbeitnow,
            Url = j.Url,
            ExternalId = j.Slug
        });
        job.Title = j.Title;
        job.Company = j.CompanyName;
        job.Location = j.Location;
        job.IsRemote = j.Remote;
        job.RequiredSkills = j.Tags ?? [];
        return job;
    }

    private sealed record ArbeitnowResponse(
        [property: JsonPropertyName("data")] List<ArbeitnowJob> Data);

    private sealed record ArbeitnowJob(
        [property: JsonPropertyName("slug")] string Slug,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("company_name")] string CompanyName,
        [property: JsonPropertyName("location")] string? Location,
        [property: JsonPropertyName("remote")] bool Remote,
        [property: JsonPropertyName("tags")] List<string>? Tags,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("created_at")] long CreatedAt);
}
