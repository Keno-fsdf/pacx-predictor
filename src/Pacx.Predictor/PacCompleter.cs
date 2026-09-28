using System.Management.Automation;
using System.Management.Automation.Language;

namespace Pacx.Predictor;

/// <summary>
/// TAB completion for pac lines, from the same cached tree as the grey suggestions. pac's own
/// "pac complete" only knows the first word, so the module registers this one on import.
/// Without a cache it returns nothing and PowerShell falls back to file names.
/// </summary>
public static class PacCompleter
{
    public sealed record Item(string Text, string Tooltip);

    /// <summary>Entry point of the registered argument completer.</summary>
    public static IEnumerable<CompletionResult> Complete(CommandAst commandAst, int cursorPosition)
    {
        var extent = commandAst.Extent;
        // The extent stops at the last token; a cursor beyond it means "after a space".
        var line = cursorPosition > extent.EndOffset
            ? extent.Text + " "
            : extent.Text[..Math.Clamp(cursorPosition - extent.StartOffset, 0, extent.Text.Length)];

        if (!PacTreeLoader.EnsureLoading().Wait(TimeSpan.FromSeconds(3)) || PacTreeLoader.Current is not { } tree)
        {
            return Array.Empty<CompletionResult>();
        }

        return Completions(line, tree).Select(i => new CompletionResult(
            i.Text, i.Text,
            i.Text.StartsWith('-') ? CompletionResultType.ParameterName : CompletionResultType.ParameterValue,
            i.Tooltip));
    }

    /// <summary>The words that can replace the line's last word, in the engine's order.</summary>
    public static IReadOnlyList<Item> Completions(string lineUpToCursor, CommandTree tree)
    {
        // The engine returns whole lines; the completion is what it put after the typed stem.
        var tokens = SuggestionEngine.Tokenize(lineUpToCursor, out var trailingSpace);
        var word = trailingSpace || tokens.Count < 2 ? string.Empty : tokens[^1];
        var stem = lineUpToCursor.Length - word.Length;

        // CompletionResult insists on a tooltip; the help text is the best one, the word itself the fallback.
        return SuggestionEngine.Suggest(lineUpToCursor, tree, max: int.MaxValue, commandNames: PacPredictor.Names)
            .Select(s => (text: s.Text[stem..].TrimStart(), s.Tooltip))
            .Where(x => x.text.Length > 0)
            .Select(x => new Item(x.text, string.IsNullOrWhiteSpace(x.Tooltip) ? x.text : x.Tooltip))
            .ToList();
    }
}
