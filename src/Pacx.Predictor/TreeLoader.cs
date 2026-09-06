using System.Diagnostics;

namespace Pacx.Predictor;

/// <summary>
/// Provides the command tree for the session: from the disk cache when it is valid,
/// otherwise from the installed pacx (and then refreshes the cache).
/// </summary>
public static class TreeLoader
{
    private static readonly object Gate = new();
    private static Task<CommandTree?>? _loading;

    /// <summary>The cleaned JSON the current tree was parsed from.</summary>
    public static string? RawJson { get; private set; }

    /// <summary>The script printed by <c>pacx completion powershell</c>, if available.</summary>
    public static string? CompleterScript { get; private set; }

    /// <summary>The loaded tree, or null while loading or after a failed load.</summary>
    public static CommandTree? Current =>
        _loading is { IsCompletedSuccessfully: true } t ? t.Result : null;

    /// <summary>Starts loading unless already started. Cheap enough to call on every keystroke.</summary>
    public static Task<CommandTree?> EnsureLoading()
    {
        lock (Gate)
        {
            return _loading ??= Task.Run(Load);
        }
    }

    private static CommandTree? Load()
    {
        var cache = new TreeCache();
        var key = TreeCache.ComputeKey();

        if (cache.TryLoad(key) is { } cached && CommandTree.Parse(cached.TreeJson, out var cachedJson) is { } cachedTree)
        {
            RawJson = cachedJson;
            CompleterScript = cached.CompleterScript;
            return cachedTree;
        }

        var tree = LoadFromPacx();
        if (tree is not null && RawJson is not null)
        {
            cache.Save(key, new PacxOutput(RawJson, CompleterScript));
        }
        return tree;
    }

    private static CommandTree? LoadFromPacx()
    {
        // pacx locks its history file while running. If another pacx instance is active at
        // that moment the call fails, so one short retry is worth it. Not so when pacx
        // cannot be started at all.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var output = RunPacx("completion export --nologo", out var started);
            if (!started) return null;

            var tree = CommandTree.Parse(output, out var json);
            if (tree is not null)
            {
                RawJson = json;
                var script = RunPacx("completion powershell --nologo", out _);
                CompleterScript = script?.Contains("Register-ArgumentCompleter") == true ? script : null;
                return tree;
            }
            Thread.Sleep(750);
        }
        return null;
    }

    private static string? RunPacx(string arguments, out bool started)
    {
        started = false;
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "pacx",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null) return null;
            started = true;

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(15_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return null;
            }
            Task.WaitAll(stdout, stderr);
            return process.ExitCode == 0 ? stdout.Result : null;
        }
        catch
        {
            // pacx missing, not on PATH, or too old for the completion commands.
            return null;
        }
    }
}
