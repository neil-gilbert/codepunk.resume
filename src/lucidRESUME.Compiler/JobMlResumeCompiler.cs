using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using lucidRESUME.Core.Interfaces;
using lucidRESUME.JobML;

namespace lucidRESUME.Compiler;

public sealed class JobMlResumeCompiler(
    IJobSpecParser jobParser,
    ResumeCompositionOrchestrator composition,
    IEmbeddingService? embeddings = null) : IJobMlCompiler
{
    private static readonly Regex TokenPattern = new(@"[\p{L}\p{N}][\p{L}\p{N}+#.-]{1,}", RegexOptions.Compiled);

    public async Task<CompilationResult> CompileAsync(
        JobMlSnapshot completeResume,
        string jobDescription,
        CompilationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completeResume);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobDescription);
        options ??= new CompilationOptions();

        var job = await jobParser.ParseFromTextAsync(jobDescription, cancellationToken);
        var requirements = BuildRequirements(job.RequiredSkills, job.PreferredSkills, job.Responsibilities);
        if (requirements.Count == 0)
            requirements = ExtractFallbackRequirements(jobDescription);

        var reconciled = JobMlProcessor.Reconcile(completeResume.File)
            .ToDictionary(x => x.Claim.Id, StringComparer.OrdinalIgnoreCase);
        var concepts = BuildConceptTerms(completeResume.File.Data.Concepts);
        var entities = completeResume.File.Data.Entities.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var index = MarkdownEvidenceIndex.Create(completeResume.File.Markdown);
        var subjectAffinities = await BuildSubjectAffinitiesAsync(
            job.Title ?? jobDescription, completeResume.File.Data.RoleCentroids, entities, cancellationToken);

        var accepted = completeResume.File.Data.Claims
            .Where(IsAccepted)
            .Where(c => reconciled.TryGetValue(c.Id, out var resolution) && resolution.Evidence.Count > 0 &&
                        resolution.Evidence.All(e => e.State is EvidenceState.Valid or EvidenceState.External))
            .ToList();
        var subjectsWithNarrative = accepted
            .Where(claim => claim.Type is "achievement" or "project" or "summary")
            .Select(claim => claim.Subject)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = accepted
            .Where(IsResumeNarrative)
            .Where(claim => claim.Type != "experience" || !subjectsWithNarrative.Contains(claim.Subject))
            .ToList();

        var matches = new List<ClaimMatch>();
        foreach (var requirement in requirements)
            foreach (var claim in candidates)
            {
                var subjectName = entities.GetValueOrDefault(claim.Subject)?.Name ?? claim.Subject;
                var (score, reason, direct) = await ScoreAsync(requirement, claim, subjectName, concepts, cancellationToken);
                if (subjectAffinities.TryGetValue(claim.Subject, out var roleAffinity))
                {
                    score = score * .75 + roleAffinity * .25;
                    if (roleAffinity >= .9 && claim.Type == "achievement")
                        score = Math.Max(score, options.RelatedThreshold + .08);
                    reason += $"; role affinity {roleAffinity:F2}";
                }
                if (score >= options.RelatedThreshold)
                    matches.Add(new ClaimMatch(requirement.Id, claim.Id,
                        direct ? MatchKind.Direct : MatchKind.Related, score, reason));
            }

        var careerAnchorSubjects = entities.Values
            .Where(IsCareerAnchor)
            .Select(entity => entity.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = SelectClaims(candidates, accepted, matches, reconciled, entities, index, options,
            careerAnchorSubjects);
        var sections = selected.GroupBy(x => x.Claim.Subject, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Any(item => item.Claim.Type == "summary"))
            .ThenByDescending(group => ExperienceSortDate(group.Key, accepted).End)
            .ThenByDescending(group => ExperienceSortDate(group.Key, accepted).Start)
            .ThenByDescending(group => group.Max(item => item.Score))
            .Select((group, number) => BuildEvidencePacket(group, number, requirements, accepted))
            .ToList();
        var gaps = requirements.Where(r => matches.All(m => m.RequirementId != r.Id || m.Kind == MatchKind.None))
            .Select(r => r.Text).ToList();
        var manifest = new ProjectionManifest(
            completeResume.Revision,
            Hash(jobDescription),
            DateTimeOffset.UtcNow,
            requirements,
            sections,
            matches,
            gaps,
            embeddings is null ? "lexical" : "configured");

        var composed = await composition.ComposeAsync(manifest, jobDescription, options, cancellationToken);
        var human = RenderHumanMarkdown(completeResume.File.Markdown, composed.Blocks, sections, job.Title);
        var projected = BuildProjection(completeResume.File, human, composed.Blocks, selected,
            options.FullJobMlUri);
        var full = JobMlArtifactComposer.Compose(projected);
        var published = CJobMlProjector.Project(projected).Markdown;
        return new CompilationResult(Guid.NewGuid().ToString("N"), manifest, human, published, full,
            projected, composed.Used, composed.Provider, composed.Warnings);
    }

    private static EvidencePacket BuildEvidencePacket(
        IGrouping<string, SelectedClaim> group,
        int number,
        IReadOnlyList<CompilerRequirement> requirements,
        IReadOnlyList<JobMlClaim> acceptedClaims)
    {
        var rankedClaims = group.OrderByDescending(x => x.Score).ToList();
        var initialRequirementIds = rankedClaims.SelectMany(x => x.Matches)
            .Select(x => x.RequirementId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var allFocus = requirements
            .Where(requirement => initialRequirementIds.Contains(requirement.Id, StringComparer.OrdinalIgnoreCase))
            .OrderBy(requirement => requirement.Kind)
            .Select(requirement => requirement.Text)
            .ToList();
        var focus = allFocus.Take(6).ToList();
        var isSummary = rankedClaims.Any(item => item.Claim.Type == "summary");
        var isProject = rankedClaims.Any(item => item.Claim.Type == "project");
        // A senior summary needs enough room for identity, the vacancy-specific
        // differentiator and the relevant implementation/leadership context. At
        // 72 words the sentence-preserving compactor commonly retained identity
        // and stack but dropped the differentiator (for example daily agent use).
        var targetWords = isSummary ? 80 : isProject ? 60 : 44 + Math.Max(0, rankedClaims.Count - 1) * 16;
        targetWords = Math.Min(targetWords, isSummary ? 80 : 76);
        var claims = FitHumanProse(rankedClaims, targetWords, allFocus);
        var requirementIds = claims.SelectMany(x => x.Matches)
            .Select(x => x.RequirementId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var sourceWords = claims.Sum(item => WordCount(item.Prose));
        var maximumWords = Math.Clamp(Math.Max(24, sourceWords), 24, isSummary ? 80 : 76);
        var intent = isSummary
            ? "Write one plain, specific professional summary for this vacancy. Keep the candidate's vocabulary and omit generic aspiration or self-praise."
            : "Write one compact role passage, not a catalogue of duties. Lead with the outcome most relevant to this vacancy, retain the substance of each selected claim, combine overlapping detail, and omit unrelated context.";
        if (focus.Count > 0)
            intent += $" The target emphasis is: {string.Join("; ", focus)}.";

        var heading = isSummary ? "Professional Summary" : CompactHeading(claims[0].SubjectName);
        var dateRange = isSummary ? null : ExperienceDateRange(group.Key, acceptedClaims);
        if (!string.IsNullOrWhiteSpace(dateRange)) heading += $" | {dateRange}";

        return new EvidencePacket(
            $"section-{number + 1}-{Slug(group.Key)}",
            heading,
            intent,
            maximumWords,
            claims,
            requirementIds,
            isSummary ? "summary" : isProject ? "project" : "experience");
    }

    private static List<SelectedClaim> FitHumanProse(
        IReadOnlyList<SelectedClaim> claims,
        int wordBudget,
        IReadOnlyList<string> focus)
    {
        var fitted = new List<SelectedClaim>();
        var remaining = wordBudget;
        for (var index = 0; index < claims.Count && remaining > 0; index++)
        {
            var futureMinimum = Math.Min(claims.Count - index - 1, 2) * 12;
            var allowance = Math.Max(12, remaining - futureMinimum);
            var prose = CompactHumanProse(claims[index].Prose, allowance, focus);
            var words = WordCount(prose);
            if (words == 0 || words > remaining)
            {
                // Prefer fewer complete, evidenced claims to chopped prose or an
                // oversized deterministic fallback.
                continue;
            }
            fitted.Add(claims[index] with { Prose = prose });
            remaining -= words;
        }
        return fitted.Count > 0 ? fitted : [claims[0]];
    }

    private static string CompactHumanProse(string prose, int maximumWords, IReadOnlyList<string> focus)
    {
        var normalized = MarkdownEvidenceIndex.NormalizeText(prose);
        if (WordCount(normalized) <= maximumWords) return normalized;
        var sentences = Regex.Split(normalized, @"(?<=[.!?])\s+")
            .Select((text, index) => new { Text = text.Trim(), Index = index })
            .Where(item => item.Text.Length > 0)
            .ToList();
        if (sentences.Count <= 1) return normalized;

        var focusTokens = Tokens(string.Join(' ', focus));
        var selected = new List<(string Text, int Index)>();
        var remaining = maximumWords;
        foreach (var sentence in sentences
                     .OrderByDescending(item => Tokens(item.Text).Intersect(focusTokens).Count())
                     .ThenBy(item => item.Index))
        {
            var words = WordCount(sentence.Text);
            if (words > remaining) continue;
            selected.Add((sentence.Text, sentence.Index));
            remaining -= words;
        }
        return selected.Count == 0
            ? normalized
            : string.Join(' ', selected.OrderBy(item => item.Index).Select(item => item.Text));
    }

    private async Task<(double Score, string Reason, bool Direct)> ScoreAsync(
        CompilerRequirement requirement, JobMlClaim claim, string subjectName,
        IReadOnlyDictionary<string, HashSet<string>> conceptTerms, CancellationToken cancellationToken)
    {
        var requirementTokens = Tokens(requirement.Text);
        var claimTerms = claim.Concepts.All.SelectMany(id => conceptTerms.GetValueOrDefault(id, [id]))
            .Append(subjectName).Append(claim.Statement).ToList();
        var claimTokens = Tokens(string.Join(' ', claimTerms));
        var intersection = requirementTokens.Intersect(claimTokens).Count();
        var lexical = intersection == 0 ? 0 : intersection / (double)Math.Max(1, requirementTokens.Count);
        var direct = claim.Concepts.All.Any(id => conceptTerms.GetValueOrDefault(id, [id])
            .Any(term => requirement.Text.Contains(term, StringComparison.OrdinalIgnoreCase)));
        if (direct) return (Math.Max(.92, lexical), "concept name or alias appears in the requirement", true);
        if (lexical >= .35) return (Math.Min(.88, .55 + lexical * .3), "shared terms", false);
        if (embeddings is null) return (lexical, "no semantic match", false);
        var a = await embeddings.EmbedAsync(requirement.Text, cancellationToken);
        var b = await embeddings.EmbedAsync(claim.Statement + " " + string.Join(' ', claimTerms), cancellationToken);
        var semantic = embeddings.CosineSimilarity(a, b);
        return (semantic, "embedding similarity", false);
    }

    private async Task<IReadOnlyDictionary<string, double>> BuildSubjectAffinitiesAsync(
        string targetText, IReadOnlyList<JobMlRoleCentroid> centroids,
        IReadOnlyDictionary<string, JobMlEntity> entities, CancellationToken cancellationToken)
    {
        if (embeddings is null || centroids.Count == 0) return new Dictionary<string, double>();
        var target = centroids
            .Where(centroid => targetText.Contains(centroid.Name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(centroid => centroid.Name.Length)
            .FirstOrDefault();
        if (target is null)
        {
            var targetVector = await embeddings.EmbedAsync(targetText, cancellationToken);
            target = centroids.Where(centroid => centroid.Vector.Count == targetVector.Length)
                .OrderByDescending(centroid => embeddings.CosineSimilarity(targetVector, [.. centroid.Vector]))
                .FirstOrDefault();
        }
        if (target is null || target.Vector.Count == 0) return new Dictionary<string, double>();

        var targetCentroid = target.Vector.ToArray();
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in entities.Values.Where(entity => entity.Type == "experience"))
        {
            var roleVector = await embeddings.EmbedAsync(entity.Name, cancellationToken);
            if (roleVector.Length == targetCentroid.Length)
            {
                var semantic = Math.Clamp(embeddings.CosineSimilarity(roleVector, targetCentroid), 0, 1);
                result[entity.Id] = Math.Max(semantic, TitleAffinity(target.Name, entity.Name));
            }
        }
        return result;
    }

    private static double TitleAffinity(string targetRole, string candidateRole)
    {
        if (candidateRole.Contains(targetRole, StringComparison.OrdinalIgnoreCase)) return 1;
        var executiveTarget = targetRole.Contains("VP", StringComparison.OrdinalIgnoreCase) ||
                              targetRole.Contains("Head", StringComparison.OrdinalIgnoreCase) ||
                              targetRole.Contains("CTO", StringComparison.OrdinalIgnoreCase);
        if (!executiveTarget) return 0;
        string[] executiveTitles = ["VP", "Head of Engineering", "CTO", "Director of Engineering", "Engineering Director"];
        if (executiveTitles.Any(title => candidateRole.Contains(title, StringComparison.OrdinalIgnoreCase))) return .92;
        if (candidateRole.Contains("Head", StringComparison.OrdinalIgnoreCase) ||
            candidateRole.Contains("Director", StringComparison.OrdinalIgnoreCase) ||
            candidateRole.Contains("CTO", StringComparison.OrdinalIgnoreCase) ||
            candidateRole.Contains("VP", StringComparison.OrdinalIgnoreCase)) return .92;
        if (candidateRole.Contains("Lead", StringComparison.OrdinalIgnoreCase) ||
            candidateRole.Contains("Manager", StringComparison.OrdinalIgnoreCase)) return .72;
        return 0;
    }

    private static List<SelectedClaim> SelectClaims(
        IReadOnlyList<JobMlClaim> candidates, IReadOnlyList<JobMlClaim> acceptedClaims,
        IReadOnlyList<ClaimMatch> matches,
        IReadOnlyDictionary<string, ClaimEvidenceResolution> reconciled,
        IReadOnlyDictionary<string, JobMlEntity> entities, MarkdownEvidenceIndex index,
        CompilationOptions options, IReadOnlySet<string>? careerAnchorSubjects = null)
    {
        var result = new List<SelectedClaim>();
        var coveredRequirements = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Reserve one concise, human-authored passage for each author-selected career
        // anchor before relevance ranking consumes the section budget.
        foreach (var subject in careerAnchorSubjects ?? new HashSet<string>())
        {
            var anchored = candidates
                .Where(claim => claim.Subject.Equals(subject, StringComparison.OrdinalIgnoreCase))
                .Select(claim => new
                {
                    Claim = claim,
                    Matches = matches.Where(match =>
                        match.ClaimId.Equals(claim.Id, StringComparison.OrdinalIgnoreCase)).ToList(),
                    Prose = ResolveProse(claim, reconciled, index)
                })
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Prose))
                .OrderByDescending(candidate => CareerAnchorClaimSignal(candidate.Claim.Statement))
                .ThenByDescending(candidate => candidate.Matches.Select(match => match.Score).DefaultIfEmpty(0).Max())
                .ThenByDescending(candidate => candidate.Claim.Type == "achievement")
                .FirstOrDefault();
            if (anchored is null) continue;
            result.Add(new SelectedClaim(anchored.Claim,
                entities.GetValueOrDefault(anchored.Claim.Subject)?.Name ?? anchored.Claim.Subject,
                anchored.Prose!,
                anchored.Claim.Evidence.Select((e, i) => e.Id ?? $"{anchored.Claim.Id}-e{i + 1}").ToList(),
                anchored.Matches.Select(match => match.Score).DefaultIfEmpty(.5).Max(), anchored.Matches));
            foreach (var requirement in anchored.Matches.Select(match => match.RequirementId))
                coveredRequirements.Add(requirement);
        }

        // Every targeted resume needs the author's reviewed summary. It frames the
        // selected evidence but never gains facts from the vacancy.
        var summary = candidates
            .Where(claim => claim.Type == "summary")
            .Select(claim => new
            {
                Claim = claim,
                Matches = matches.Where(match =>
                    match.ClaimId.Equals(claim.Id, StringComparison.OrdinalIgnoreCase)).ToList(),
                Prose = ResolveProse(claim, reconciled, index)
            })
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Prose))
            .OrderByDescending(candidate => candidate.Matches.Select(match => match.Score).DefaultIfEmpty(.55).Max())
            .FirstOrDefault();
        if (summary is not null && result.All(item => item.Claim.Id != summary.Claim.Id))
            result.Add(new SelectedClaim(summary.Claim,
                entities.GetValueOrDefault(summary.Claim.Subject)?.Name ?? summary.Claim.Subject,
                summary.Prose!,
                summary.Claim.Evidence.Select((e, i) => e.Id ?? $"{summary.Claim.Id}-e{i + 1}").ToList(),
                summary.Matches.Select(match => match.Score).DefaultIfEmpty(.55).Max(), summary.Matches));

        // Reserve relevant employment evidence before project matches consume the
        // section budget. Leadership resumes still need to read as career histories.
        var reservedRoles = candidates
            .Where(claim => entities.GetValueOrDefault(claim.Subject)?.Type == "experience")
            .Where(claim => careerAnchorSubjects?.Contains(claim.Subject) != true)
            .Select(claim => new
            {
                Claim = claim,
                Matches = matches.Where(match =>
                    match.ClaimId.Equals(claim.Id, StringComparison.OrdinalIgnoreCase)).ToList(),
                Prose = ResolveProse(claim, reconciled, index),
                Recency = ExperienceRecency(claim.Subject, acceptedClaims)
            })
            .Where(candidate => candidate.Matches.Count > 0 && !string.IsNullOrWhiteSpace(candidate.Prose))
            .GroupBy(candidate => candidate.Claim.Subject, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(candidate =>
                    candidate.Matches.Max(match => match.Score) * .55 + candidate.Recency * .45)
                .ThenByDescending(candidate => LeadershipClaimSignal(candidate.Claim.Statement))
                .First())
            .OrderByDescending(candidate =>
                candidate.Matches.Max(match => match.Score) * .55 + candidate.Recency * .45)
            .Take(options.MinimumExperienceSections);
        foreach (var role in reservedRoles)
        {
            if (result.Any(item => item.Claim.Id.Equals(role.Claim.Id, StringComparison.OrdinalIgnoreCase))) continue;
            result.Add(new SelectedClaim(role.Claim,
                entities.GetValueOrDefault(role.Claim.Subject)?.Name ?? role.Claim.Subject,
                role.Prose!,
                role.Claim.Evidence.Select((e, i) => e.Id ?? $"{role.Claim.Id}-e{i + 1}").ToList(),
                role.Matches.Max(match => match.Score), role.Matches));
            foreach (var requirement in role.Matches.Select(match => match.RequirementId))
                coveredRequirements.Add(requirement);
        }

        var pending = candidates
            .Select(c => (Claim: c, Matches: matches.Where(m => m.ClaimId.Equals(c.Id, StringComparison.OrdinalIgnoreCase)).ToList()))
            .Where(x => x.Matches.Count > 0)
            // Anchors already have their one defining passage. Keep them compact and
            // leave the ordinary claim and section budgets to role-specific evidence.
            .Where(x => careerAnchorSubjects?.Contains(x.Claim.Subject) != true)
            .Where(x => result.All(selected => !selected.Claim.Id.Equals(x.Claim.Id, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        while (pending.Count > 0 && NonAnchorClaimCount(result, careerAnchorSubjects) < options.MaximumClaims)
        {
            var claim = pending.OrderByDescending(x =>
            {
                var uncoveredBonus = x.Matches.Any(m => !coveredRequirements.Contains(m.RequirementId)) ? .12 : 0;
                var subjectPenalty = result.Count(r => r.Claim.Subject.Equals(x.Claim.Subject, StringComparison.OrdinalIgnoreCase)) * options.DiversityPenalty;
                var conceptOverlap = result.Count == 0 ? 0 : result.Max(r => Jaccard(r.Claim.Concepts.All, x.Claim.Concepts.All)) * options.DiversityPenalty;
                return x.Matches.Max(m => m.Score) + uncoveredBonus - subjectPenalty - conceptOverlap;
            }).First();
            pending.Remove(claim);
            var isNewSubject = result.All(existing =>
                !existing.Claim.Subject.Equals(claim.Claim.Subject, StringComparison.OrdinalIgnoreCase));
            if (isNewSubject && result
                    .Where(existing => careerAnchorSubjects?.Contains(existing.Claim.Subject) != true)
                    .Select(existing => existing.Claim.Subject)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() >= options.MaximumSections)
                continue;
            if (result.Count(x => x.Claim.Subject.Equals(claim.Claim.Subject, StringComparison.OrdinalIgnoreCase)) >=
                options.MaximumClaimsPerSubject) continue;
            var prose = ResolveProse(claim.Claim, reconciled, index);
            // A role-specific resume is never bootstrapped from an LLM or the advert.
            if (string.IsNullOrWhiteSpace(prose)) continue;
            // Imports commonly contain lightly rewritten copies of the same bullet.
            // Keep their separate evidence in the career record, but do not print both
            // in one role section of the projection.
            if (result.Any(existing =>
                    existing.Claim.Subject.Equals(claim.Claim.Subject, StringComparison.OrdinalIgnoreCase) &&
                    TextOverlap(existing.Prose, prose) >= .48))
                continue;
            result.Add(new SelectedClaim(claim.Claim,
                entities.GetValueOrDefault(claim.Claim.Subject)?.Name ?? claim.Claim.Subject,
                prose,
                claim.Claim.Evidence.Select((e, i) => e.Id ?? $"{claim.Claim.Id}-e{i + 1}").ToList(),
                claim.Matches.Max(x => x.Score), claim.Matches));
            foreach (var requirement in claim.Matches.Select(x => x.RequirementId)) coveredRequirements.Add(requirement);
        }
        return result;
    }

    private static int NonAnchorClaimCount(
        IEnumerable<SelectedClaim> selected,
        IReadOnlySet<string>? careerAnchorSubjects) => selected.Count(item =>
        careerAnchorSubjects?.Contains(item.Claim.Subject) != true);

    private static string? ResolveProse(JobMlClaim claim,
        IReadOnlyDictionary<string, ClaimEvidenceResolution> reconciled, MarkdownEvidenceIndex index) =>
        reconciled.GetValueOrDefault(claim.Id)?.Evidence
            .Where(x => x.State == EvidenceState.Valid && IsProse(x.Evidence))
            .Select(x => x.CurrentText ??
                         (x.Evidence.Ref is not null && index.TryGet(x.Evidence.Ref, out var passage)
                             ? passage.Text
                             : null))
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

    private static int CareerAnchorClaimSignal(string statement)
    {
        var score = 0;
        if (statement.Contains("first release", StringComparison.OrdinalIgnoreCase) ||
            statement.Contains("public production release", StringComparison.OrdinalIgnoreCase)) score += 10;
        if (statement.Contains("led", StringComparison.OrdinalIgnoreCase) ||
            statement.Contains("owned", StringComparison.OrdinalIgnoreCase)) score += 3;
        if (statement.Contains("architect", StringComparison.OrdinalIgnoreCase) ||
            statement.Contains("built", StringComparison.OrdinalIgnoreCase)) score += 2;
        return score;
    }

    private static int LeadershipClaimSignal(string statement)
    {
        var score = CareerAnchorClaimSignal(statement);
        if (statement.Contains("recruited", StringComparison.OrdinalIgnoreCase) ||
            statement.Contains("hired", StringComparison.OrdinalIgnoreCase)) score += 6;
        if (statement.Contains("engineering standards", StringComparison.OrdinalIgnoreCase)) score += 4;
        if (statement.Contains("team", StringComparison.OrdinalIgnoreCase)) score += 2;
        if (statement.Contains("mentored", StringComparison.OrdinalIgnoreCase)) score += 2;
        return score;
    }

    private static string? ExperienceDateRange(string subject, IReadOnlyList<JobMlClaim> claims)
    {
        var temporal = claims.FirstOrDefault(claim =>
            claim.Subject.Equals(subject, StringComparison.OrdinalIgnoreCase) && claim.Type == "experience");
        if (temporal is null) return null;
        var match = Regex.Match(temporal.Statement,
            @"\|\s*(?<start>\d{4}-\d{2}-\d{2})\s*\|\s*(?<end>\d{4}-\d{2}-\d{2}|Present)\s*$",
            RegexOptions.IgnoreCase);
        if (!match.Success || !DateOnly.TryParseExact(match.Groups["start"].Value, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)) return null;
        var endText = match.Groups["end"].Value;
        var end = endText.Equals("Present", StringComparison.OrdinalIgnoreCase)
            ? "Present"
            : DateOnly.TryParseExact(endText, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var endDate)
                ? endDate.ToString("MMM yyyy", CultureInfo.GetCultureInfo("en-GB"))
                : null;
        return end is null
            ? null
            : $"{start.ToString("MMM yyyy", CultureInfo.GetCultureInfo("en-GB"))} - {end}";
    }

    private static bool IsCurrentExperience(string subject, IReadOnlyList<JobMlClaim> claims) =>
        claims.Any(claim => claim.Subject.Equals(subject, StringComparison.OrdinalIgnoreCase) &&
                            claim.Type == "experience" &&
                            Regex.IsMatch(claim.Statement, @"\|\s*present\s*$", RegexOptions.IgnoreCase));

    private static (DateOnly Start, DateOnly End) ExperienceSortDate(
        string subject, IReadOnlyList<JobMlClaim> claims)
    {
        var temporal = claims.FirstOrDefault(claim =>
            claim.Subject.Equals(subject, StringComparison.OrdinalIgnoreCase) && claim.Type == "experience");
        if (temporal is null) return (DateOnly.MinValue, DateOnly.MinValue);
        var match = Regex.Match(temporal.Statement,
            @"\|\s*(?<start>\d{4}-\d{2}-\d{2})\s*\|\s*(?<end>\d{4}-\d{2}-\d{2}|present)\s*$",
            RegexOptions.IgnoreCase);
        if (!match.Success || !DateOnly.TryParseExact(match.Groups["start"].Value, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
            return (DateOnly.MinValue, DateOnly.MinValue);
        if (match.Groups["end"].Value.Equals("present", StringComparison.OrdinalIgnoreCase))
            return (start, DateOnly.MaxValue);
        return DateOnly.TryParseExact(match.Groups["end"].Value, "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)
            ? (start, end)
            : (DateOnly.MinValue, DateOnly.MinValue);
    }

    private static double ExperienceRecency(string subject, IReadOnlyList<JobMlClaim> claims)
    {
        var temporal = claims.FirstOrDefault(claim =>
            claim.Subject.Equals(subject, StringComparison.OrdinalIgnoreCase) && claim.Type == "experience");
        if (temporal is null) return .4;
        var match = Regex.Match(temporal.Statement,
            @"\|\s*\d{4}-\d{2}-\d{2}\s*\|\s*(?<end>\d{4}-\d{2}-\d{2}|present)\s*$",
            RegexOptions.IgnoreCase);
        if (!match.Success) return .4;
        if (match.Groups["end"].Value.Equals("present", StringComparison.OrdinalIgnoreCase)) return 1;
        if (!DateOnly.TryParseExact(match.Groups["end"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var end)) return .4;
        return end.Year switch
        {
            >= 2025 => 1,
            >= 2023 => .85,
            >= 2021 => .7,
            >= 2018 => .55,
            _ => .4
        };
    }

    private static bool IsCareerAnchor(JobMlEntity entity) =>
        string.Equals(entity.Projection?.Include, "always", StringComparison.OrdinalIgnoreCase);

    private static JobMlFile BuildProjection(JobMlFile source, string human,
        IReadOnlyList<CompositionBlock> blocks, IReadOnlyList<SelectedClaim> selected, string? fullJobMlUri)
    {
        var effectiveFullJobMl = AbsoluteHttpUri(fullJobMlUri ?? source.Data.Document.EffectiveFullJobMl);
        var sourceEntities = source.Data.Entities.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var selectedById = selected.ToDictionary(x => x.Claim.Id, StringComparer.OrdinalIgnoreCase);
        var claims = new List<JobMlClaim>();
        foreach (var block in blocks)
            foreach (var claimId in block.ClaimIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!selectedById.TryGetValue(claimId, out var selectedClaim)) continue;
                var original = selectedClaim.Claim;
                var passageText = MarkdownEvidenceIndex.NormalizeText(block.Text);
                var evidence = new List<JobMlEvidence>
            {
                new()
                {
                    Id = $"projection-{block.SectionId}", Type = "prose", Ref = $"#{block.SectionId}:p1",
                    Fingerprint = new JobMlFingerprint { Text = MarkdownEvidenceIndex.Fingerprint(passageText) },
                    Selector = new JobMlTextSelector { Exact = passageText }
                }
            };
                evidence.AddRange(original.Evidence.Where(e => !IsProse(e)).Select(item =>
                    ProjectSupportingEvidence(item, original.Subject,
                        sourceEntities.GetValueOrDefault(original.Subject)?.Name ?? original.Subject,
                        effectiveFullJobMl)));
                claims.Add(new JobMlClaim
                {
                    Id = original.Id,
                    Subject = original.Subject,
                    Type = original.Type,
                    Statement = original.Statement,
                    Concepts = new JobMlClaimConcepts
                    {
                        Skills = [.. original.Concepts.Skills],
                        Capabilities = [.. original.Concepts.Capabilities],
                        Domains = [.. original.Concepts.Domains]
                    },
                    Evidence = evidence,
                    Origin = original.Origin,
                    Review = "accepted"
                });
            }
        claims = claims.DistinctBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToList();
        var entityIds = claims.Select(x => x.Subject).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var conceptIds = claims.SelectMany(x => x.Concepts.All).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sourceIds = claims.SelectMany(claim => claim.Evidence)
            .Select(evidence => evidence.SourceId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var root = new JobMlRoot
        {
            Header = new JobMlHeader
            {
                Version = source.Data.Header.Version,
                Profile = "resume",
                Purpose = "Machine-readable projection of claims selected for this role-specific resume.",
                Semantics = [.. source.Data.Header.Semantics]
            },
            Document = new JobMlDocumentMetadata
            {
                Id = source.Data.Document.Id + "-projection",
                Language = source.Data.Document.Language,
                FullJobMl = effectiveFullJobMl?.ToString()
            },
            Entities = source.Data.Entities.Where(x => entityIds.Contains(x.Id)).Select(x => new JobMlEntity
            {
                Id = x.Id,
                Name = CompactHeading(x.Name),
                Type = x.Type,
                Source = $"#{blocks.First(b => b.ClaimIds.Any(id => selectedById.GetValueOrDefault(id)?.Claim.Subject.Equals(x.Id, StringComparison.OrdinalIgnoreCase) == true)).SectionId}",
                Projection = x.Projection is null ? null : new JobMlProjectionPreference
                {
                    Include = x.Projection.Include,
                    Reason = x.Projection.Reason
                }
            }).ToList(),
            Claims = claims,
            Concepts = source.Data.Concepts.Where(x => conceptIds.Contains(x.Id)).Select(x => new JobMlConcept
            {
                Id = x.Id,
                Type = x.Type,
                Name = x.Name,
                Aliases = [.. x.Aliases]
            }).ToList(),
            Sources = source.Data.Sources.Where(x => sourceIds.Contains(x.Id)).ToList()
        };
        return new JobMlFile(human, root);
    }

    private static string RenderHumanMarkdown(string completeMarkdown,
        IReadOnlyList<CompositionBlock> blocks, IReadOnlyList<EvidencePacket> packets, string? targetTitle)
    {
        var firstSection = Regex.Match(completeMarkdown, @"(?m)^##\s+");
        var identity = (firstSection.Success ? completeMarkdown[..firstSection.Index] : completeMarkdown).Trim();
        if (string.IsNullOrWhiteSpace(identity)) identity = "# Résumé";
        var packetById = packets.ToDictionary(packet => packet.SectionId, StringComparer.OrdinalIgnoreCase);
        var orderedKinds = new[] { "summary", "project", "experience" };
        var renderedGroups = new List<string>();
        foreach (var kind in orderedKinds)
        {
            var matching = blocks.Where(block =>
                    packetById.GetValueOrDefault(block.SectionId)?.Kind.Equals(kind,
                        StringComparison.OrdinalIgnoreCase) == true)
                .ToList();
            if (matching.Count == 0) continue;

            if (kind == "summary")
            {
                renderedGroups.AddRange(matching.Select(block => RenderResumeBlock(block,
                    packetById[block.SectionId], 2)));
                continue;
            }

            var groupHeading = kind == "project" ? "Selected AI Engineering" : "Experience";
            renderedGroups.Add($"## {groupHeading}\n\n" + string.Join("\n\n", matching.Select(block =>
                RenderResumeBlock(block, packetById[block.SectionId], 3))));
        }

        var title = string.IsNullOrWhiteSpace(targetTitle)
            ? null
            : $"**{Regex.Replace(targetTitle.Trim(), @"\s+", " ")}**";
        return string.Join("\n\n", new[] { identity, title }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Concat(renderedGroups));
    }

    private static string RenderResumeBlock(CompositionBlock block, EvidencePacket packet, int headingLevel)
    {
        // A composition block is the evidence passage for every claim it contains.
        // Keep it as one Markdown paragraph so :p1 and its fingerprint describe the
        // same text even when the source block combined several claim passages.
        return $"{new string('#', headingLevel)} {packet.Heading} {{#{block.SectionId}}}\n\n" +
               MarkdownEvidenceIndex.NormalizeText(block.Text);
    }

    private static IReadOnlyDictionary<string, HashSet<string>> BuildConceptTerms(IEnumerable<JobMlConcept> concepts) =>
        concepts.ToDictionary(x => x.Id,
            x => new[] { x.Id, x.Name }.Concat(x.Aliases).Where(v => !string.IsNullOrWhiteSpace(v)).ToHashSet(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

    private static List<CompilerRequirement> BuildRequirements(
        IReadOnlyList<string> required, IReadOnlyList<string> preferred, IReadOnlyList<string> responsibilities)
    {
        var all = required.Select(x => (x, RequirementKind.Required))
            .Concat(preferred.Select(x => (x, RequirementKind.Preferred)))
            .Concat(responsibilities.Select(x => (x, RequirementKind.Responsibility)))
            .Where(x => !string.IsNullOrWhiteSpace(x.x)).DistinctBy(x => x.x, StringComparer.OrdinalIgnoreCase);
        return all.Select((x, i) => new CompilerRequirement($"req-{i + 1}", x.x.Trim(), x.Item2, x.x.Trim())).ToList();
    }

    private static List<CompilerRequirement> ExtractFallbackRequirements(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length is >= 12 and <= 240)
            .Take(40).Select((x, i) => new CompilerRequirement($"req-{i + 1}", x, RequirementKind.Responsibility, x)).ToList();

    private static string CompactHeading(string value)
    {
        var heading = Regex.Replace(value, @"\s+", " ").Trim();
        var colon = heading.IndexOf(':');
        if (colon is > 0 and <= 80)
            heading = heading[..colon].Trim();
        if (heading.Length <= 110) return heading;
        var boundary = heading.LastIndexOf(' ', 106);
        return heading[..(boundary > 30 ? boundary : 106)].TrimEnd() + "…";
    }

    private static bool IsAccepted(JobMlClaim claim) =>
        string.Equals(claim.Review, "accepted", StringComparison.OrdinalIgnoreCase);
    private static bool IsResumeNarrative(JobMlClaim claim) => claim.Type is null or
        "summary" or "achievement" or "project" or "education" or "experience";
    private static bool IsProse(JobMlEvidence evidence) =>
        evidence.Type.Equals("prose", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(evidence.Uri);
    private static JobMlEvidence CloneEvidence(JobMlEvidence e) => new()
    {
        Id = e.Id,
        Type = e.Type,
        Ref = e.Ref,
        Uri = e.Uri,
        SourceId = e.SourceId,
        Issuer = e.Issuer,
        Qualification = e.Qualification,
        Title = e.Title,
        Authors = [.. e.Authors],
        Publisher = e.Publisher,
        Published = e.Published,
        Accessed = e.Accessed,
        Fingerprint = e.Fingerprint,
        Selector = e.Selector,
        State = e.State
    };
    private static JobMlEvidence ProjectSupportingEvidence(JobMlEvidence evidence, string subjectId,
        string subjectName, Uri? fullJobMl)
    {
        var projected = CloneEvidence(evidence);
        if (!IsTranscriptEvidence(projected)) return projected;

        projected.Type = "career_transcript";
        projected.Title = $"Complete transcript: {subjectName}";
        projected.Uri = TranscriptSectionUri(fullJobMl, subjectId)?.ToString();
        return projected;
    }

    private static bool IsTranscriptEvidence(JobMlEvidence evidence) =>
        string.Equals(evidence.Type, "source_ledger", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(evidence.Type, "career_transcript", StringComparison.OrdinalIgnoreCase);

    private static Uri? AbsoluteHttpUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https"
            ? uri
            : null;

    private static Uri? TranscriptSectionUri(Uri? fullJobMl, string subjectId)
    {
        if (fullJobMl is null) return null;
        var builder = new UriBuilder(fullJobMl) { Fragment = subjectId };
        return builder.Uri;
    }
    private static HashSet<string> Tokens(string value) => TokenPattern.Matches(value.ToLowerInvariant()).Select(x => x.Value).ToHashSet();
    private static int WordCount(string value) => Regex.Matches(value, @"\b[\p{L}\p{N}][\p{L}\p{N}'’-]*\b").Count;
    private static double Jaccard(IEnumerable<string> left, IEnumerable<string> right)
    {
        var a = left.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var b = right.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var union = a.Union(b, StringComparer.OrdinalIgnoreCase).Count();
        return union == 0 ? 0 : a.Intersect(b, StringComparer.OrdinalIgnoreCase).Count() / (double)union;
    }
    private static double TextOverlap(string left, string right)
    {
        var a = Tokens(left);
        var b = Tokens(right);
        var smaller = Math.Min(a.Count, b.Count);
        return smaller == 0 ? 0 : a.Intersect(b, StringComparer.OrdinalIgnoreCase).Count() / (double)smaller;
    }
    private static string Slug(string value) => Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];
}
