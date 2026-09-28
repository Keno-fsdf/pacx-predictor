using System.Text.Json;

namespace Pacx.Predictor;

/// <summary>
/// The pac command tree comes from the disk cache, nothing else. Building it means a few
/// hundred pac processes, and that is nothing to run behind an interactive shell, however
/// throttled; it happens when the user asks for it with Update-PacPredictor and waits.
/// Without a cache there are simply no pac suggestions.
/// </summary>
public static class PacTreeLoader
{
    private static readonly object Gate = new();
    private static Task<CommandTree?>? _loading;

    public static CommandTree? Current =>
        _loading is { IsCompletedSuccessfully: true } t ? t.Result : null;

    /// <summary>Reads the cache once per session. Cheap enough to call on every keystroke.</summary>
    public static Task<CommandTree?> EnsureLoading()
    {
        lock (Gate)
        {
            return _loading ??= Task.Run(LoadFromCache);
        }
    }

    internal static string CacheDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pacx.Predictor", "pac");

    /// <summary>
    /// The key is the installed pac version, read from the package folder next to the shim
    /// ("Microsoft.PowerApps.CLI.2.12.2"). Deliberately no path in it: shells spell the same
    /// PATH entry differently, and the same pac must never look like a different one.
    /// </summary>
    internal static string ComputeKey(string? pathVariable = null)
    {
        var shim = TreeCache.FindOnPath("pac", pathVariable ?? Environment.GetEnvironmentVariable("PATH"));
        var root = shim is null ? null : Path.GetDirectoryName(shim);
        if (root is null || !Directory.Exists(root)) return "pac:not-found";

        var newest = Directory.GetDirectories(root, "Microsoft.PowerApps.CLI.*")
            .Select(Path.GetFileName)
            .Select(name => (name, version: ParseVersion(name!)))
            .Where(x => x.version is not null)
            .OrderByDescending(x => x.version)
            .Select(x => x.name)
            .FirstOrDefault();
        return newest is null ? "pac:unknown-version" : "pac|" + newest;
    }

    private static Version? ParseVersion(string folderName)
    {
        var dot = folderName.IndexOf(".CLI.", StringComparison.OrdinalIgnoreCase);
        return dot >= 0 && Version.TryParse(folderName[(dot + 5)..], out var v) ? v : null;
    }

    private static CommandTree? LoadFromCache()
    {
        var key = ComputeKey();
        var cache = new TreeCache(CacheDirectory, maxAge: TimeSpan.MaxValue);
        if (cache.TryLoad(key) is { } cached && CommandTree.Parse(cached.TreeJson) is { } tree)
        {
            Note(key, "cache");
            return PacTreeBuilder.AddKnownAliases(tree);
        }

        Note(key, "no cache for this pac, run Update-PacPredictor");
        return null;
    }

    /// <summary>
    /// Builds the tree from the installed pac, caches it and makes it the current tree of
    /// this session. <paramref name="pagesDone"/> is called from worker threads.
    /// </summary>
    public static CommandTree Rebuild(Action<int>? pagesDone = null)
    {
        var key = ComputeKey();
        if (key == "pac:not-found")
        {
            throw new InvalidOperationException("pac was not found on PATH.");
        }

        using var gate = BuildGate.TryAcquire(CacheDirectory)
            ?? throw new InvalidOperationException("Another shell is building the pac tree right now; try again in a minute.");

        var tree = PacTreeBuilder.Build(PacTreeBuilder.RunPacHelp, pagesDone);
        if (tree.Commands.Count == 0)
        {
            throw new InvalidOperationException("pac answered, but no commands could be read from its help output.");
        }

        // Cached as pac describes itself; the unlisted aliases are added on every load.
        new TreeCache(CacheDirectory, maxAge: TimeSpan.MaxValue)
            .Save(key, new PacxOutput(JsonSerializer.Serialize(tree), null));
        Note(key, $"built by Update-PacPredictor, {tree.Commands.Count} commands");

        var ready = PacTreeBuilder.AddKnownAliases(tree);
        lock (Gate)
        {
            _loading = Task.FromResult<CommandTree?>(ready);
        }
        return ready;
    }

    /// <summary>
    /// One line per load in the cache folder, so "why are there no suggestions?" can be
    /// answered by reading a file instead of guessing.
    /// </summary>
    private static void Note(string key, string what)
    {
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            File.AppendAllText(Path.Combine(CacheDirectory, "loads.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  pid {Environment.ProcessId,-6} {what,-52} key={key}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics only.
        }
    }
}

/// <summary>Cross-process lock via an exclusively opened file; released on dispose or process exit.</summary>
internal sealed class BuildGate : IDisposable
{
    private readonly FileStream _stream;
    private BuildGate(FileStream stream) => _stream = stream;

    public static BuildGate? TryAcquire(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var stream = new FileStream(Path.Combine(directory, "build.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            return new BuildGate(stream);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Dispose() => _stream.Dispose();
}
