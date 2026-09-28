using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace lucidRESUME.Web;

public sealed class LucidResumeWebOptions
{
    public const string SectionName = "LucidResumeWeb";

    public string PublicationDirectory { get; set; } = Path.Combine("App_Data", "resume-publications");

    /// <summary>
    /// When true, successful compilations are stored and receive an opaque public URL.
    /// This is publication, not access control or visitor analytics.
    /// </summary>
    public bool PublishCompiledResumes { get; set; } = true;
}

public sealed class FileSystemResumePublicationStore(IOptions<LucidResumeWebOptions> options)
    : IResumePublicationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _directory = Path.GetFullPath(options.Value.PublicationDirectory);

    public string CreatePublicId() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(24));

    public async Task PublishAsync(ResumePublication publication,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publication);
        if (!IsPublicId(publication.PublicId))
            throw new ArgumentException("The publication ID is invalid.", nameof(publication));

        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, publication.PublicId + ".json");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var json = JsonSerializer.Serialize(publication, JsonOptions);
        await File.WriteAllTextAsync(temporary, json, new UTF8Encoding(false), cancellationToken);
        File.Move(temporary, path, false);
    }

    public async Task<ResumePublication?> GetAsync(string publicId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publicId);
        if (!IsPublicId(publicId)) return null;
        var path = Path.Combine(_directory, publicId + ".json");
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<ResumePublication>(json, JsonOptions);
    }

    private static bool IsPublicId(string value) =>
        value.Length == 32 && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}
