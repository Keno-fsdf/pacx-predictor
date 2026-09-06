using System.Text.Json;

namespace Pacx.Predictor;

/// <summary>What one pacx version produces: the command tree JSON and the tab-completer script.</summary>
public sealed record PacxOutput(string TreeJson, string? CompleterScript);

/// <summary>
/// Caches pacx output on disk so a new shell does not have to run pacx at all. The cache
/// is keyed by the pacx executable (path, size, timestamp) and expires after a day, which
/// also covers changes pacx cannot signal through its binary, such as installed plugins.
/// </summary>
public sealed class TreeCache
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    private readonly string _dir;
    private string TreeFile => Path.Combine(_dir, "tree.json");
    private string CompleterFile => Path.Combine(_dir, "completer.ps1");
    private string MetaFile => Path.Combine(_dir, "meta.json");

    public TreeCache(string? directory = null)
    {
        _dir = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pacx.Predictor");
    }

    private sealed class Meta
    {
        public string Key { get; set; } = string.Empty;
        public DateTime SavedUtc { get; set; }
    }

    /// <summary>Returns the cached output if it belongs to <paramref name="key"/> and is fresh.</summary>
    public PacxOutput? TryLoad(string key, DateTime? nowUtc = null)
    {
        try
        {
            if (!File.Exists(MetaFile) || !File.Exists(TreeFile)) return null;

            var meta = JsonSerializer.Deserialize<Meta>(File.ReadAllText(MetaFile));
            if (meta is null || meta.Key != key) return null;
            if ((nowUtc ?? DateTime.UtcNow) - meta.SavedUtc > MaxAge) return null;

            var completer = File.Exists(CompleterFile) ? File.ReadAllText(CompleterFile) : null;
            return new PacxOutput(File.ReadAllText(TreeFile), completer);
        }
        catch
        {
            return null;
        }
    }

    public void Save(string key, PacxOutput output, DateTime? nowUtc = null)
    {
        try
        {
            Directory.CreateDirectory(_dir);
            WriteAtomic(TreeFile, output.TreeJson);
            if (output.CompleterScript is not null) WriteAtomic(CompleterFile, output.CompleterScript);
            var meta = new Meta { Key = key, SavedUtc = nowUtc ?? DateTime.UtcNow };
            WriteAtomic(MetaFile, JsonSerializer.Serialize(meta));
        }
        catch
        {
            // A missing cache only costs the next start a pacx run.
        }
    }

    private static void WriteAtomic(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>
    /// Identifies the installed pacx: location, size and timestamp of the executable found
    /// on PATH. A dotnet tool update rewrites the shim, so the key changes with the version.
    /// </summary>
    public static string ComputeKey(string? pathVariable = null)
    {
        var exe = FindOnPath("pacx", pathVariable ?? Environment.GetEnvironmentVariable("PATH"));
        if (exe is null) return "pacx:not-found";
        var info = new FileInfo(exe);
        return $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
    }

    private static string? FindOnPath(string name, string? pathVariable)
    {
        if (string.IsNullOrEmpty(pathVariable)) return null;
        var extensions = OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd", ".bat", "" } : new[] { "" };
        foreach (var dir in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(dir.Trim(), name + ext);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}
