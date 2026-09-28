using lucidRESUME.Core.Interfaces;
using lucidRESUME.Core.Models.Jobs;
using lucidRESUME.Core.Models.Profile;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.JobSearch;

namespace lucidRESUME.Matching.Tests;

public sealed class JobSearchQualityTests
{
    [Fact]
    public void Deduplicate_IgnoresCompanyCaseAndSeniorityQualifier()
    {
        var older = Job("Senior Engineering Lead", "Example Ltd", "old", DateTimeOffset.Parse("2026-01-01"));
        var newer = Job("engineering lead", "EXAMPLE LTD", "new", DateTimeOffset.Parse("2026-02-01"));

        var result = JobDeduplicator.Deduplicate([older, newer]);

        Assert.Collection(result, job => Assert.Equal("new", job.Source.ExternalId));
    }

    [Fact]
    public void GenerateQueries_DeduplicatesWithoutChangingHumanCasing()
    {
        var profile = new UserProfile();
        profile.Preferences.TargetRoles.AddRange(["VP Engineering", "vp engineering"]);
        var resume = ResumeDocument.Create("resume.md", "text/markdown", 0);

        var result = RoleSuggestionService.GenerateQueries(resume, profile);

        Assert.Collection(result, query => Assert.Equal("VP Engineering", query.Keywords));
    }

    [Fact]
    public async Task SearchAll_IsolatesFailedProvider()
    {
        var expected = Job("Engineer", "Example", "ok", DateTimeOffset.UtcNow);
        var service = new JobSearchService([
            new FakeAdapter((_, _) => throw new HttpRequestException("offline")),
            new FakeAdapter((_, _) => Task.FromResult<IReadOnlyList<JobDescription>>([expected]))
        ]);

        var result = await service.SearchAllAsync(new JobSearchQuery("engineer"));

        Assert.Collection(result, job => Assert.Same(expected, job));
    }

    [Fact]
    public async Task SearchAll_PropagatesCancellation()
    {
        var service = new JobSearchService([
            new FakeAdapter((_, token) => Task.FromCanceled<IReadOnlyList<JobDescription>>(token))
        ]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.SearchAllAsync(new JobSearchQuery("engineer"), cancellation.Token));
    }

    private static JobDescription Job(string title, string company, string id, DateTimeOffset fetchedAt)
    {
        var job = JobDescription.Create("", new JobSource
        {
            ExternalId = id,
            Url = $"https://example.test/{id}",
            FetchedAt = fetchedAt
        });
        job.Title = title;
        job.Company = company;
        return job;
    }

    private sealed class FakeAdapter(
        Func<JobSearchQuery, CancellationToken, Task<IReadOnlyList<JobDescription>>> search) : IJobSearchAdapter
    {
        public string AdapterName => "fake";
        public bool IsConfigured => true;

        public Task<IReadOnlyList<JobDescription>> SearchAsync(
            JobSearchQuery query, CancellationToken ct = default) => search(query, ct);
    }
}
