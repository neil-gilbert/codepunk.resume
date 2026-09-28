namespace lucidRESUME.Core.Models.Resume;

/// <summary>
/// Normalised education levels based on ISCED 2011.
/// Cross-cultural equivalence — a German "Diplom", UK "BSc (Hons)", Indian "B.Tech"
/// all map to the same level.
/// </summary>
public enum EducationLevel
{
    Unknown = 0,
    SecondarySchool = 1,     // GCSE, O-levels, High School Diploma, Standard Grade, National 5
    PostSecondary = 2,       // A-levels, IB, Highers, Advanced Highers, Abitur, Baccalauréat
    Vocational = 3,          // HND, HNC, BTEC, SVQ
    Associate = 4,           // Associate's degree, Foundation degree
    Bachelors = 5,           // BSc, BA, BEng, B.Tech, Licence, Scottish MA (Hons)
    PostGradDiploma = 6,     // PgDip, PGCE, Graduate Diploma
    Masters = 7,             // MSc, MA, MEng, M.Tech, Diplom, DEA
    Doctoral = 8,            // PhD, DPhil, Dr., Doktorgrad
    PostDoctoral = 9,        // PostDoc, Habilitation
}

/// <summary>
/// Maps ISCED levels to EducationLevel enum.
/// The actual qualification patterns are in Resources/education/qualification-equivalence.txt
/// and loaded by EducationEquivalence in the Matching module.
/// This class only provides the ISCED→enum mapping.
/// </summary>
public static class EducationLevelClassifier
{
    public static EducationLevel FromIsced(int iscedLevel) => iscedLevel switch
    {
        3 => EducationLevel.SecondarySchool,
        4 => EducationLevel.PostSecondary,
        5 => EducationLevel.Vocational,
        6 => EducationLevel.Bachelors,
        7 => EducationLevel.Masters,
        8 => EducationLevel.Doctoral,
        _ => EducationLevel.Unknown,
    };

    /// <summary>
    /// Classify a degree string. Delegates to the resource-file-based
    /// EducationEquivalence system when available, falls back to basic heuristics.
    /// </summary>
    public static EducationLevel Classify(string? degree)
    {
        if (string.IsNullOrWhiteSpace(degree)) return EducationLevel.Unknown;
        var text = $" {degree} ";

        // Basic heuristics as fallback when the full EducationEquivalence isn't loaded
        // (e.g. in Core-only contexts without Matching module).
        // The real classification should use EducationEquivalence.Default.GetIscedLevel()
        if (text.Contains("phd", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("doctorate", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("dphil", StringComparison.OrdinalIgnoreCase))
            return EducationLevel.Doctoral;
        if (text.Contains("master", StringComparison.OrdinalIgnoreCase) || text.Contains("msc", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("mba", StringComparison.OrdinalIgnoreCase) || text.Contains("meng", StringComparison.OrdinalIgnoreCase))
            return EducationLevel.Masters;
        if (text.Contains("bachelor", StringComparison.OrdinalIgnoreCase) || text.Contains("bsc", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("beng", StringComparison.OrdinalIgnoreCase) || text.Contains("hons", StringComparison.OrdinalIgnoreCase))
            return EducationLevel.Bachelors;
        if (text.Contains("hnd", StringComparison.OrdinalIgnoreCase) || text.Contains("hnc", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("btec", StringComparison.OrdinalIgnoreCase))
            return EducationLevel.Vocational;
        if (text.Contains("a-level", StringComparison.OrdinalIgnoreCase) || text.Contains("higher", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("abitur", StringComparison.OrdinalIgnoreCase))
            return EducationLevel.PostSecondary;
        if (text.Contains("gcse", StringComparison.OrdinalIgnoreCase) || text.Contains("o-level", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("high school", StringComparison.OrdinalIgnoreCase))
            return EducationLevel.SecondarySchool;

        return EducationLevel.Unknown;
    }

    public static bool MeetsRequirement(EducationLevel candidate, EducationLevel required)
    {
        if (required == EducationLevel.Unknown) return true;
        return candidate >= required;
    }
}
