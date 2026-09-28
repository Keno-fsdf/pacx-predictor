using System.Management.Automation.Subsystem.Prediction;

namespace Pacx.Predictor;

/// <summary>
/// PSReadLine predictor for pac (Microsoft Power Platform CLI) command lines. Same engine as
/// for pacx, fed from a tree built out of "pac ... help"; pac has no environment values, so
/// suggestions stop at the option names.
/// </summary>
public sealed class PacPredictor : ICommandPredictor
{
    public static readonly Guid PredictorId = new("b7e1d4a2-5c39-4f6e-a0d8-3e2f9c1b7d44");

    public static readonly string[] Names = { "pac", "pac.exe", "pac.cmd" };

    public Guid Id => PredictorId;
    public string Name => "pac";
    public string Description => "Inline suggestions for Power Platform CLI (pac) commands and options.";

    public SuggestionPackage GetSuggestion(PredictionClient client, PredictionContext context, CancellationToken cancellationToken)
    {
        var input = context.InputAst?.Extent.Text;
        if (input is null || !StartsWithPac(input))
        {
            return default;
        }

        var tree = PacTreeLoader.Current;
        if (tree is null)
        {
            PacTreeLoader.EnsureLoading();
            return default;
        }

        var suggestions = SuggestionEngine.Suggest(input, tree, commandNames: Names);
        if (suggestions.Count == 0) return default;

        return new SuggestionPackage(
            suggestions.Select(s => new PredictiveSuggestion(s.Text, s.Tooltip)).ToList());
    }

    /// <summary>"pac" followed by a space or the end of the line, so "pacx ..." is not ours.</summary>
    private static bool StartsWithPac(string input)
    {
        var trimmed = input.TrimStart();
        foreach (var name in Names)
        {
            if (trimmed.Length == name.Length && trimmed.Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.Length > name.Length
                && trimmed.StartsWith(name, StringComparison.OrdinalIgnoreCase)
                && char.IsWhiteSpace(trimmed[name.Length])) return true;
        }
        return false;
    }
}
