using System.Text.RegularExpressions;

namespace Pacx.Predictor;

/// <summary>One "pac ... help" page, reduced to what the tree needs.</summary>
internal sealed record PacHelpPage(string? Description, IReadOnlyList<PacUsageItem> Usage, IReadOnlyList<PacHelpEntry> Entries)
{
    /// <summary>A page lists either sub commands (a group) or options (a command).</summary>
    public bool IsCommand => Usage.Count == 0 || Usage.Any(u => u.Name.StartsWith("--"));
}

/// <summary>An item of the "Usage:" line; brackets in the help output mean optional.</summary>
internal sealed record PacUsageItem(string Name, bool Optional);

/// <summary>An indented "  name   help" line: a sub command or an option.</summary>
internal sealed record PacHelpEntry(string Name, string Help, string? Alias, List<string>? Values);

/// <summary>
/// Parses the text of "pac ... help". pac has no machine readable command listing, but its
/// help is regular enough: a "Usage:" line naming the children, then one indented line per
/// child with its description, aliases at the end in parentheses and fixed values on a
/// "Values:" line below the option.
/// </summary>
internal static partial class PacHelpParser
{
    [GeneratedRegex(@"^\s{1,8}(\S+)\s{2,}(\S.*)$")]
    private static partial Regex EntryLine();

    [GeneratedRegex(@"^\s+Values:\s*(.*)$")]
    private static partial Regex ValuesLine();

    [GeneratedRegex(@"\s*\(alias:\s*(-[^)]+)\)\s*$")]
    private static partial Regex AliasSuffix();

    public static PacHelpPage Parse(string? text)
    {
        var lines = (text ?? string.Empty).Replace("\r", string.Empty).Split('\n');

        string? description = null;
        var usage = new List<PacUsageItem>();
        var entries = new List<PacHelpEntry>();
        var inHelpSection = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("Help:"))
            {
                inHelpSection = true;
                continue;
            }
            if (line.StartsWith("Commands:") || line.StartsWith("Usage:"))
            {
                inHelpSection = false;
            }
            if (inHelpSection)
            {
                if (description is null && line.Trim().Length > 0) description = line.Trim();
                continue;
            }

            if (line.StartsWith("Usage:"))
            {
                usage.AddRange(ParseUsage(line));
                continue;
            }

            var values = ValuesLine().Match(line);
            if (values.Success)
            {
                if (entries.Count > 0)
                {
                    var last = entries[^1];
                    entries[^1] = last with { Values = SplitValues(values.Groups[1].Value) };
                }
                continue;
            }

            var entry = EntryLine().Match(line);
            if (entry.Success)
            {
                var name = entry.Groups[1].Value;
                var help = entry.Groups[2].Value.Trim();
                string? alias = null;
                var aliasMatch = AliasSuffix().Match(help);
                if (aliasMatch.Success)
                {
                    alias = aliasMatch.Groups[1].Value;
                    help = help[..aliasMatch.Index].TrimEnd();
                }
                entries.Add(new PacHelpEntry(name, help, alias, null));
                continue;
            }

            // Any other text after the first entry is a wrapped description. pac wraps long
            // lines at the console width and the continuation may start in column one.
            if (entries.Count > 0 && line.Trim().Length > 0)
            {
                var last = entries[^1];
                entries[^1] = last with { Help = (last.Help + " " + line.Trim()).Trim() };
            }
        }

        return new PacHelpPage(description, usage, entries);
    }

    /// <summary>"Usage: pac admin list [--environment] --name" -> the bracketed and dashed items.</summary>
    private static IEnumerable<PacUsageItem> ParseUsage(string line)
    {
        foreach (var token in line["Usage:".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.StartsWith('[') && token.EndsWith(']'))
            {
                yield return new PacUsageItem(token[1..^1], Optional: true);
            }
            else if (token.StartsWith("--"))
            {
                yield return new PacUsageItem(token, Optional: false);
            }
            // Anything else is the command chain itself ("pac admin list").
        }
    }

    private static List<string> SplitValues(string text) =>
        text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
