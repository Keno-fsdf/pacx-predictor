using System.Text;

namespace Pacx.Predictor;

public sealed record Suggestion(string Text, string? Tooltip);

/// <summary>
/// Turns the typed command line into full-line suggestions. Pure logic, no PowerShell
/// dependency, and the same rules as the tab completer shipped with pacx.
/// </summary>
public static class SuggestionEngine
{
    private static readonly string[] PacxNames = { "pacx", "pacx.exe" };

    public static IReadOnlyList<Suggestion> Suggest(string input, CommandTree tree, int max = 5)
    {
        if (string.IsNullOrEmpty(input)) return Array.Empty<Suggestion>();

        var tokens = Tokenize(input, out var trailingSpace);
        if (tokens.Count == 0 || !PacxNames.Contains(tokens[0], StringComparer.OrdinalIgnoreCase))
        {
            return Array.Empty<Suggestion>();
        }

        // The last token is the word being completed unless the cursor sits after a space.
        var args = tokens.Skip(1).ToList();
        var word = string.Empty;
        if (!trailingSpace && args.Count > 0)
        {
            word = args[^1];
            args.RemoveAt(args.Count - 1);
        }
        var prefix = input[..(input.Length - word.Length)];
        if (!prefix.EndsWith(' ')) prefix += " ";

        var verbs = new List<string>();
        var usedOptions = new List<string>();
        foreach (var t in args)
        {
            if (t.StartsWith('-')) usedOptions.Add(t);
            else if (usedOptions.Count == 0) verbs.Add(t);
        }

        var command = FindCommand(tree, verbs);
        return command is null
            ? SuggestVerbs(tree, verbs, word, prefix, max)
            : SuggestOptions(command, args, usedOptions, word, prefix, max);
    }

    private static IReadOnlyList<Suggestion> SuggestOptions(
        CommandNode command, List<string> args, List<string> usedOptions, string word, string prefix, int max)
    {
        var results = new List<Suggestion>();

        // Directly after an option: its fixed values, or nothing if it takes free text.
        if (args.Count > 0 && args[^1].StartsWith('-'))
        {
            var previous = command.Options.FirstOrDefault(o => o.MatchesToken(args[^1]));
            if (previous?.Values is { Count: > 0 } values)
            {
                foreach (var v in values)
                {
                    if (results.Count >= max) break;
                    if (StartsWith(v, word)) results.Add(new Suggestion(prefix + v, previous.Help));
                }
                return results;
            }
            if (previous is not null && word.Length == 0) return results;
        }

        foreach (var o in command.Options.OrderByDescending(o => o.Required))
        {
            if (results.Count >= max) break;
            if (usedOptions.Any(o.MatchesToken) || !StartsWith(o.LongToken, word)) continue;
            results.Add(new Suggestion(prefix + o.LongToken, o.Help));
        }
        return results;
    }

    private static IReadOnlyList<Suggestion> SuggestVerbs(
        CommandTree tree, List<string> verbs, string word, string prefix, int max)
    {
        // Next verb after the typed ones; help text from the command if it completes the
        // chain, otherwise from the namespace.
        var candidates = new List<KeyValuePair<string, string?>>();
        foreach (var c in tree.Commands)
        {
            foreach (var chain in c.VerbChains())
            {
                if (chain.Count <= verbs.Count || !PrefixMatches(chain, verbs)) continue;
                var next = chain[verbs.Count];
                if (next.StartsWith('!') || candidates.Any(x => Same(x.Key, next))) continue;
                candidates.Add(new(next, chain.Count == verbs.Count + 1 ? c.Help : null));
            }
        }
        foreach (var ns in tree.Namespaces)
        {
            if (ns.Verbs.Count != verbs.Count + 1 || !PrefixMatches(ns.Verbs, verbs)) continue;
            var i = candidates.FindIndex(x => Same(x.Key, ns.Verbs[^1]));
            if (i >= 0 && !string.IsNullOrEmpty(ns.Help)) candidates[i] = new(candidates[i].Key, ns.Help);
        }

        var results = new List<Suggestion>();
        foreach (var (verb, help) in candidates)
        {
            if (results.Count >= max) break;
            if (StartsWith(verb, word)) results.Add(new Suggestion(prefix + verb, help));
        }
        return results;
    }

    private static CommandNode? FindCommand(CommandTree tree, List<string> verbs)
    {
        if (verbs.Count == 0) return null;
        return tree.Commands.FirstOrDefault(c =>
            c.VerbChains().Any(chain => chain.Count == verbs.Count && PrefixMatches(chain, verbs)));
    }

    private static bool PrefixMatches(IReadOnlyList<string> chain, List<string> typed)
    {
        for (var i = 0; i < typed.Count; i++)
        {
            if (!Same(chain[i], typed[i])) return false;
        }
        return true;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool StartsWith(string candidate, string word) =>
        word.Length == 0 || candidate.StartsWith(word, StringComparison.OrdinalIgnoreCase);

    /// <summary>Splits on whitespace, keeping quoted strings together.</summary>
    internal static List<string> Tokenize(string input, out bool trailingSpace)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var quote = '\0';
        foreach (var ch in input)
        {
            if (quote != '\0')
            {
                current.Append(ch);
                if (ch == quote) quote = '\0';
            }
            else if (ch is '"' or '\'')
            {
                quote = ch;
                current.Append(ch);
            }
            else if (char.IsWhiteSpace(ch))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(ch);
            }
        }
        trailingSpace = current.Length == 0 && input.Length > 0 && char.IsWhiteSpace(input[^1]);
        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }
}
