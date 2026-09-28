using System.Diagnostics;

namespace Pacx.Predictor;

/// <summary>
/// Builds the pac command tree by walking "pac ... help" level by level. Each page costs a
/// process start (about 1.5 s), so pages of one level are fetched several at a time; pac
/// has no lock file, unlike pacx, so this is safe. A full tree is a few hundred pages and
/// takes under a minute once, after that the disk cache serves it.
/// </summary>
public static class PacTreeBuilder
{
    // More parallelism barely helps (8: 138 s, 16: 129 s for ~440 pages), pac spends its
    // time starting up. The user waits for this, so 8 is fine.
    public const int Parallelism = 8;

    /// <summary>
    /// Builds the tree from a function that returns the help text for a verb chain.
    /// <paramref name="pagesDone"/> gets the running count of pages fetched, from worker threads.
    /// </summary>
    public static CommandTree Build(Func<IReadOnlyList<string>, string?> help, Action<int>? pagesDone = null)
    {
        var done = 0;
        var tree = new CommandTree();
        var root = PacHelpParser.Parse(help(Array.Empty<string>()));

        // Descriptions of children come from the parent's listing; a group's own page repeats
        // it under "Help:", a command's page may not.
        var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pending = Children(Array.Empty<string>(), root, descriptions);

        while (pending.Count > 0)
        {
            var pages = new PacHelpPage?[pending.Count];
            Parallel.For(0, pending.Count, new ParallelOptions { MaxDegreeOfParallelism = Parallelism }, i =>
            {
                var text = help(pending[i]);
                pages[i] = text is null ? null : PacHelpParser.Parse(text);
                pagesDone?.Invoke(Interlocked.Increment(ref done));
            });

            var next = new List<List<string>>();
            for (var i = 0; i < pending.Count; i++)
            {
                var chain = pending[i];
                var page = pages[i];
                if (page is null) continue;

                descriptions.TryGetValue(Key(chain), out var listed);
                if (page.IsCommand)
                {
                    tree.Commands.Add(ToCommand(chain, page, page.Description ?? listed));
                }
                else
                {
                    tree.Namespaces.Add(new NamespaceNode { Verbs = chain, Help = page.Description ?? listed });
                    next.AddRange(Children(chain, page, descriptions));
                }
            }
            pending = next;
        }

        return tree;
    }

    /// <summary>
    /// pac is installed as a .cmd shim, which Process.Start cannot launch directly without
    /// the shell. Resolved once per session: the path on PATH and whether it needs cmd.exe.
    /// </summary>
    private static readonly Lazy<string?> PacPath = new(() =>
        TreeCache.FindOnPath("pac", Environment.GetEnvironmentVariable("PATH")));

    /// <summary>Runs the installed pac for one help page; null when pac cannot be started.</summary>
    public static string? RunPacHelp(IReadOnlyList<string> chain)
    {
        var path = PacPath.Value;
        if (path is null) return null;

        var arguments = chain.Count == 0 ? "help" : string.Join(' ', chain) + " help";
        var isScript = path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = isScript ? "cmd.exe" : path,
                Arguments = isScript ? $"/d /c \"\"{path}\" {arguments}\"" : arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null) return null;
            try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(20_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return null;
            }
            Task.WaitAll(stdout, stderr);
            return stdout.Result.Contains("Usage:") ? stdout.Result : null;
        }
        catch
        {
            return null;
        }
    }

    private static List<List<string>> Children(IReadOnlyList<string> chain, PacHelpPage page, Dictionary<string, string> descriptions)
    {
        var result = new List<List<string>>();
        foreach (var item in page.Usage)
        {
            // "help" is pac's own help command on every level and never worth suggesting,
            // and groups pac marks as deprecated are not worth the pages to fetch.
            if (item.Name.StartsWith("--") || item.Name.Equals("help", StringComparison.OrdinalIgnoreCase)) continue;

            var entry = page.Entries.FirstOrDefault(e => e.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase));
            if (entry?.Help.StartsWith("(deprecated)", StringComparison.OrdinalIgnoreCase) == true) continue;

            var child = new List<string>(chain) { item.Name };
            result.Add(child);
            if (entry is not null) descriptions[Key(child)] = entry.Help;
        }
        return result;
    }

    private static CommandNode ToCommand(IReadOnlyList<string> chain, PacHelpPage page, string? help)
    {
        var command = new CommandNode { Verbs = chain.ToList(), Help = help };
        foreach (var entry in page.Entries)
        {
            if (!entry.Name.StartsWith("--")) continue;
            if (entry.Help.StartsWith("(deprecated)", StringComparison.OrdinalIgnoreCase)) continue;

            var usage = page.Usage.FirstOrDefault(u => u.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase));
            command.Options.Add(new OptionNode
            {
                Long = entry.Name[2..],
                Short = entry.Alias?.TrimStart('-'),
                Help = entry.Help,
                Required = usage is { Optional: false },
                Values = entry.Values,
            });
        }
        return command;
    }

    private static string Key(IReadOnlyList<string> chain) => string.Join(' ', chain);

    /// <summary>
    /// Group names pac accepts but does not list in its help. "pac org ..." is the older
    /// spelling of "pac env ..." and still works, so it gets the same suggestions.
    /// Safe to call more than once.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> KnownAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["org"] = "env" };

    public static CommandTree AddKnownAliases(CommandTree tree)
    {
        foreach (var (alias, listed) in KnownAliases)
        {
            foreach (var command in tree.Commands)
            {
                if (command.Verbs.Count == 0 || !command.Verbs[0].Equals(listed, StringComparison.OrdinalIgnoreCase)) continue;
                var chain = command.Verbs.ToList();
                chain[0] = alias;
                if (!command.Aliases.Any(a => a.SequenceEqual(chain, StringComparer.OrdinalIgnoreCase)))
                {
                    command.Aliases.Add(chain);
                }
            }

            var group = tree.Namespaces.FirstOrDefault(n => n.Verbs.Count == 1 && n.Verbs[0].Equals(listed, StringComparison.OrdinalIgnoreCase));
            if (group is not null && !tree.Namespaces.Any(n => n.Verbs.Count == 1 && n.Verbs[0].Equals(alias, StringComparison.OrdinalIgnoreCase)))
            {
                tree.Namespaces.Add(new NamespaceNode { Verbs = new List<string> { alias }, Help = group.Help });
            }
        }
        return tree;
    }
}
