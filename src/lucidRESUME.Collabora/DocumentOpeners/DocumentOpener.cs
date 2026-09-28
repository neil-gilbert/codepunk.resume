using System.Diagnostics;
using System.Runtime.InteropServices;

namespace lucidRESUME.Collabora.DocumentOpeners;

/// <summary>Represents a locally installed app that can open resume documents.</summary>
public class DocumentOpener
{
    private readonly string _executablePath;
    private readonly string? _macAppName; // For macOS `open -a AppName` style launching

    public string Name { get; }

    protected DocumentOpener(string name, string executablePath, string? macAppName = null)
    {
        Name = name;
        _executablePath = executablePath;
        _macAppName = macAppName;
    }

    public virtual void Open(string filePath)
    {
        if (!File.Exists(filePath)) return;

        // macOS: use `open -a AppName` for app bundles, or just `open` for system default
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && _macAppName is not null)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "open",
                UseShellExecute = false,
                CreateNoWindow = false
            };
            startInfo.ArgumentList.Add("-a");
            startInfo.ArgumentList.Add(_macAppName);
            startInfo.ArgumentList.Add(filePath);
            Process.Start(startInfo);
            return;
        }

        var editorStartInfo = new ProcessStartInfo
        {
            FileName = _executablePath,
            UseShellExecute = false,
            CreateNoWindow = false
        };
        editorStartInfo.ArgumentList.Add(filePath);
        Process.Start(editorStartInfo);
    }

    /// <summary>Try to create an opener if the executable exists.</summary>
    public static DocumentOpener? TryCreate(string name, params string[] candidatePaths)
    {
        ArgumentNullException.ThrowIfNull(candidatePaths);
        foreach (var path in candidatePaths)
        {
            if (File.Exists(path))
                return new DocumentOpener(name, path);
        }

        // Resolve PATH directly. Probing by launching the editor can leave a GUI
        // process behind and makes discovery itself observable to the user.
        var exeName = candidatePaths.LastOrDefault() ?? "";
        if (!exeName.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !exeName.Contains('/', StringComparison.Ordinal))
        {
            var path = Environment.GetEnvironmentVariable("PATH");
            foreach (var directory in path?.Split(Path.PathSeparator,
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [])
            {
                if (File.Exists(Path.Combine(directory, exeName)))
                    return new DocumentOpener(name, exeName);
            }
        }

        return null;
    }

    /// <summary>Create a macOS opener that uses `open -a AppName`.</summary>
    public static DocumentOpener? TryCreateMacApp(string name, string appName)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return null;

        // Check if the app bundle exists
        var appPath = $"/Applications/{appName}.app";
        if (Directory.Exists(appPath))
            return new DocumentOpener(name, "open", macAppName: appName);

        // Also check user-level Applications
        var userAppPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Applications", $"{appName}.app");
        if (Directory.Exists(userAppPath))
            return new DocumentOpener(name, "open", macAppName: appName);

        return null;
    }

    /// <summary>Create a macOS "System Default" opener using the `open` command.</summary>
    public static DocumentOpener? TryCreateMacDefault()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return null;
        return new DocumentOpener("System Default", "open");
    }

    // ── Platform-specific factory methods ────────────────────────────────────

    public static DocumentOpener? TryCreateLibreOffice() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? TryCreate("LibreOffice",
                @"C:\Program Files\LibreOffice\program\soffice.exe",
                @"C:\Program Files (x86)\LibreOffice\program\soffice.exe")
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? TryCreate("LibreOffice",
                "/Applications/LibreOffice.app/Contents/MacOS/soffice")
        : TryCreate("LibreOffice",
            "/usr/bin/soffice",
            "/usr/bin/libreoffice",
            "/usr/local/bin/soffice",
            "soffice");

    public static DocumentOpener? TryCreateMicrosoftWord() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? TryCreate("Microsoft Word",
                @"C:\Program Files\Microsoft Office\root\Office16\WINWORD.EXE",
                @"C:\Program Files (x86)\Microsoft Office\root\Office16\WINWORD.EXE",
                @"C:\Program Files\Microsoft Office\Office16\WINWORD.EXE",
                @"C:\Program Files (x86)\Microsoft Office\Office16\WINWORD.EXE",
                @"C:\Program Files\Microsoft Office\Office15\WINWORD.EXE",
                @"C:\Program Files\Microsoft Office\Office14\WINWORD.EXE")
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? TryCreate("Microsoft Word",
                "/Applications/Microsoft Word.app/Contents/MacOS/Microsoft Word")
        : null;

    public static DocumentOpener? TryCreateWpsOffice() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? TryCreate("WPS Office",
                @"C:\Program Files (x86)\Kingsoft\WPS Office\ksolaunch.exe",
                @"C:\Program Files\Kingsoft\WPS Office\ksolaunch.exe")
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? TryCreate("WPS Office",
                "/Applications/wpsoffice.app/Contents/MacOS/wpsoffice")
        : TryCreate("WPS Office",
            "/usr/bin/wps",
            "wps");

    public static DocumentOpener? TryCreateOnlyOffice() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? TryCreate("ONLYOFFICE",
                @"C:\Program Files\ONLYOFFICE\DesktopEditors\DesktopEditors.exe",
                @"C:\Program Files (x86)\ONLYOFFICE\DesktopEditors\DesktopEditors.exe")
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? TryCreate("ONLYOFFICE",
                "/Applications/ONLYOFFICE Desktop Editors.app/Contents/MacOS/ONLYOFFICE Desktop Editors")
        : TryCreate("ONLYOFFICE",
            "/usr/bin/onlyoffice-desktopeditors",
            "onlyoffice-desktopeditors");
}
