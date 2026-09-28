using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Jobs;
using Microsoft.Extensions.Options;

namespace lucidRESUME.JobSearch.Adapters;

public sealed class FindworkOptions
{
    public string ApiKey { get; set; } = "";
}

public sealed class FindworkAdapter : IJobSearchAdapter
{
    private readonly HttpClient _http;
    private readonly FindworkOptions _options;

    public string AdapterName => "Findwork";
    public bool IsConfigured => !string.IsNullOrEmpty(_options.ApiKey);

    public FindworkAdapter(HttpClient http, IOptions<FindworkOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        _http = http;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<JobDescription>> SearchAsync(JobSearchQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!IsConfigured) return [];

        var url = $"https://findwork.dev/api/jobs/?search={Uri.EscapeDataString(query.Keywords)}&remote=true";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", _options.ApiKey);

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<FindworkResponse>(ct);
        return result?.Results.Select(ToJobDescription).ToList() ?? [];
    }

    private static JobDescription ToJobDescription(FindworkResult r)
    {
        var job = JobDescription.Create(r.Text ?? "", new JobSource
        {
            Type = JobSourceType.Findwork,
            Url = r.Url,
            ExternalId = r.Id.ToString(CultureInfo.InvariantCulture)
        });
        job.Title = r.Role;
        job.Company = r.CompanyName;
        job.IsRemote = r.Remote;
        job.RequiredSkills = r.Tags ?? [];
        return job;
    }

    private sealed record FindworkResponse(
        [property: JsonPropertyName("results")] List<FindworkResult> Results);

    private sealed record FindworkResult(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("role")] string? Role,
        [property: JsonPropertyName("company_name")] string? CompanyName,
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("date_posted")] string? DatePosted,
        [property: JsonPropertyName("employment_type")] string? EmploymentType,
        [property: JsonPropertyName("remote")] bool Remote,
        [property: JsonPropertyName("tags")] List<string>? Tags);
}
