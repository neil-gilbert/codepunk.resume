using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using lucidRESUME.Core.Models.Evidence;
using lucidRESUME.Core.Models.Resume;
using lucidRESUME.GitHub.Models;

namespace lucidRESUME.GitHub;

/// <summary>
/// Audits public NuGet registry metadata and groups package IDs into source-product
/// families. Registry popularity is retained as an observation, never competence.
/// </summary>
public sealed partial class NuGetPackageAuditService(HttpClient http)
{
    public async Task<NuGetPackageAuditResult> AuditAsync(
        string publisher, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publisher);
        var endpoint = await DiscoverSearchEndpointAsync(cancellationToken);
        var packages = (await SearchAsync(endpoint, publisher, cancellationToken))
            .Where(package => MatchesPublisher(package, publisher)).ToList();
        var ownedPackageIds = await GetPublisherPackageIdsAsync(publisher, cancellationToken);
        var knownIds = packages.Select(package => package.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var packageId in ownedPackageIds.Where(packageId => !knownIds.Contains(packageId)))
        {
            var exact = await SearchAsync(endpoint, $"packageid:{packageId}", cancellationToken);
            var match = exact.FirstOrDefault(package => package.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase));
            if (match is not null) packages.Add(match);
        }

        var distinct = packages
            .GroupBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.Version, StringComparer.OrdinalIgnoreCase).First())
            .ToList();
        var families = distinct.GroupBy(FamilyKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildFamily(group.Key, group.OrderByDescending(item => item.TotalDownloads).ToList()))
            .OrderByDescending(family => family.TotalDownloads)
            .ThenByDescending(family => family.PackageCount)
            .ToList();
        var observedAt = DateTimeOffset.UtcNow;
        foreach (var family in families)
        {
            family.ProjectEvidence.EvidenceMetadata["provider"] = "nuget";
            family.ProjectEvidence.EvidenceMetadata["publisher"] = publisher;
            family.ProjectEvidence.EvidenceMetadata["observed_at"] = observedAt.ToString("O");
        }
        return new NuGetPackageAuditResult
        {
            Publisher = publisher,
            ObservedAt = observedAt,
            PackageCount = distinct.Count,
            TotalDownloads = distinct.Sum(package => package.TotalDownloads),
            Families = families
        };
    }

    internal static NuGetPackageFamily BuildFamily(string key, List<NuGetPackageRecord> packages)
    {
        var sourceRepository = packages.Select(package => NormalizeProjectUrl(package.ProjectUrl))
            .FirstOrDefault(value => value is not null);
        var familyName = FamilyName(key, sourceRepository);
        var terms = packages.SelectMany(package => package.Tags)
            .Concat(packages.SelectMany(package => PackageTerms(package.Id)))
            .Where(term => term.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(term => term, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var representative = packages.Take(6).Select(package => package.Id).ToList();
        var downloads = packages.Sum(package => package.TotalDownloads);
        var registryUri = $"https://www.nuget.org/packages/{Uri.EscapeDataString(packages[0].Id)}";
        var source = sourceRepository ?? registryUri;
        var description = $"Public NuGet package family containing {packages.Count} package ID{(packages.Count == 1 ? "" : "s")} " +
                          $"and {downloads:N0} cumulative registry downloads at audit time. " +
                          $"Representative packages: {string.Join(", ", representative)}.";
        return new NuGetPackageFamily
        {
            Id = EvidenceLedgerBuilder.Slug(key),
            Name = familyName,
            SourceRepository = sourceRepository,
            PackageCount = packages.Count,
            TotalDownloads = downloads,
            Packages = packages,
            SearchTerms = terms,
            ProjectEvidence = new Project
            {
                Id = EvidenceLedgerBuilder.StableGuid($"nuget:{key}"),
                Name = $"{familyName} package family",
                Description = description,
                Url = source,
                Technologies = terms,
                ImportSources = ["NuGet package registry audit"],
                EvidenceMetadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["provider"] = "nuget",
                    ["family_key"] = key,
                    ["package_count"] = packages.Count.ToString(),
                    ["total_downloads"] = downloads.ToString(),
                    ["package_ids"] = string.Join(",", packages.Select(package => package.Id)),
                    ["source_repository"] = sourceRepository ?? "unknown"
                }
            }
        };
    }

    internal static string FamilyKey(NuGetPackageRecord package)
    {
        var parts = package.Id.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var offset = parts.Length > 1 && parts[0].Equals("mostlylucid", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        if (offset < parts.Length && IsStableFamilyPrefix(parts[offset]))
            return $"nuget:{parts[offset].ToLowerInvariant()}";
        var project = NormalizeProjectUrl(package.ProjectUrl);
        if (project is not null) return project;
        if (parts.Length == 1) return parts[0];
        if (offset >= parts.Length) return package.Id;
        return $"nuget:{parts[offset].ToLowerInvariant()}";
    }

    private async Task<List<NuGetPackageRecord>> SearchAsync(
        string endpoint, string query, CancellationToken cancellationToken)
    {
        var packages = new List<NuGetPackageRecord>();
        const int pageSize = 100;
        for (var skip = 0; ; skip += pageSize)
        {
            var separator = endpoint.Contains('?') ? '&' : '?';
            var uri = $"{endpoint}{separator}q={Uri.EscapeDataString(query)}&skip={skip}&take={pageSize}&prerelease=true&semVerLevel=2.0.0";
            var page = await http.GetFromJsonAsync<NuGetSearchResponse>(uri, cancellationToken)
                       ?? new NuGetSearchResponse();
            packages.AddRange(page.Data);
            if (skip + pageSize >= page.TotalHits || page.Data.Count == 0) break;
        }
        return packages;
    }

    private async Task<HashSet<string>> GetPublisherPackageIdsAsync(
        string publisher, CancellationToken cancellationToken)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var page = 1; page <= 100; page++)
        {
            var uri = $"https://www.nuget.org/profiles/{Uri.EscapeDataString(publisher)}?page={page}";
            var html = await http.GetStringAsync(uri, cancellationToken);
            var before = ids.Count;
            foreach (Match match in PackageLink().Matches(html))
                ids.Add(Uri.UnescapeDataString(match.Groups[1].Value));
            if (ids.Count == before || !html.Contains($"page={page + 1}", StringComparison.OrdinalIgnoreCase)) break;
        }
        return ids;
    }

    private async Task<string> DiscoverSearchEndpointAsync(CancellationToken cancellationToken)
    {
        using var stream = await http.GetStreamAsync("https://api.nuget.org/v3/index.json", cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        foreach (var resource in document.RootElement.GetProperty("resources").EnumerateArray())
        {
            var type = resource.GetProperty("@type");
            var types = type.ValueKind == JsonValueKind.Array
                ? type.EnumerateArray().Select(value => value.GetString())
                : [type.GetString()];
            if (types.Any(value => value?.StartsWith("SearchQueryService", StringComparison.OrdinalIgnoreCase) == true))
                return resource.GetProperty("@id").GetString()
                       ?? throw new InvalidDataException("NuGet search endpoint has no URI.");
        }
        throw new InvalidDataException("NuGet service index has no SearchQueryService resource.");
    }

    private static bool MatchesPublisher(NuGetPackageRecord package, string publisher)
    {
        var normalizedPublisher = Token().Replace(publisher, "").ToLowerInvariant();
        if (normalizedPublisher.Length == 0) return false;
        if (Token().Replace(package.Id, "").StartsWith(normalizedPublisher, StringComparison.OrdinalIgnoreCase))
            return true;
        return package.Authors.Any(author =>
            Token().Replace(author, "").Equals(normalizedPublisher, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsStableFamilyPrefix(string value) => value.Equals("ephemeral", StringComparison.OrdinalIgnoreCase) ||
                                                               value.Equals("lucidrag", StringComparison.OrdinalIgnoreCase) ||
                                                               value.Equals("styloflow", StringComparison.OrdinalIgnoreCase) ||
                                                               value.Equals("styloextract", StringComparison.OrdinalIgnoreCase) ||
                                                               value.Equals("consoleimage", StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeProjectUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return null;
        if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length < 2 ? null : $"https://github.com/{segments[0]}/{segments[1].Replace(".git", "", StringComparison.OrdinalIgnoreCase)}";
    }

    private static string FamilyName(string key, string? projectUrl)
    {
        if (projectUrl is not null)
        {
            var segment = new Uri(projectUrl).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (!string.IsNullOrWhiteSpace(segment)) return Humanize(segment);
        }
        return Humanize(key.StartsWith("nuget:", StringComparison.OrdinalIgnoreCase) ? key[6..] : key);
    }

    private static IEnumerable<string> PackageTerms(string packageId) => packageId
        .Split(['.', '-', '_'], StringSplitOptions.RemoveEmptyEntries)
        .Where(term => !term.Equals("mostlylucid", StringComparison.OrdinalIgnoreCase));

    private static string Humanize(string value) => string.Join(' ', WordBoundary().Split(value)
        .SelectMany(part => part.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries))) switch
    {
        var result when result.Equals("lucidrag", StringComparison.OrdinalIgnoreCase) => "LucidRAG",
        var result when result.Equals("stylobot", StringComparison.OrdinalIgnoreCase) => "StyloBot",
        var result when result.Equals("styloextract", StringComparison.OrdinalIgnoreCase) => "StyloExtract",
        var result when result.Equals("styloflow", StringComparison.OrdinalIgnoreCase) => "StyloFlow",
        var result => result
    };

    [GeneratedRegex("[^a-zA-Z0-9]+")]
    private static partial Regex Token();

    [GeneratedRegex("(?<=[a-z0-9])(?=[A-Z])")]
    private static partial Regex WordBoundary();

    [GeneratedRegex("href=\"/packages/([^/\"?#]+)(?:[/\"?#])", RegexOptions.IgnoreCase)]
    private static partial Regex PackageLink();
}
