using System.Management.Automation;
using System.Management.Automation.Subsystem;
using System.Management.Automation.Subsystem.Prediction;

namespace Pacx.Predictor;

/// <summary>PSReadLine predictor for pacx command lines.</summary>
public sealed class PacxPredictor : ICommandPredictor
{
    public static readonly Guid PredictorId = new("6f0c7a1e-3d2b-4b8e-9a51-2f1c0d7e5a10");

    public Guid Id => PredictorId;
    public string Name => "pacx";
    public string Description => "Inline suggestions for PACX (Greg.Xrm.Command) commands and options.";

    public SuggestionPackage GetSuggestion(PredictionClient client, PredictionContext context, CancellationToken cancellationToken)
    {
        var input = context.InputAst?.Extent.Text;
        if (input is null || !input.TrimStart().StartsWith("pacx", StringComparison.OrdinalIgnoreCase))
        {
            return default;
        }

        var tree = TreeLoader.Current;
        if (tree is null)
        {
            TreeLoader.EnsureLoading();
            return default;
        }

        var suggestions = SuggestionEngine.Suggest(input, tree);
        if (suggestions.Count == 0) return default;

        return new SuggestionPackage(
            suggestions.Select(s => new PredictiveSuggestion(s.Text, s.Tooltip)).ToList());
    }
}

/// <summary>Registers the predictor on import and removes it when the module is unloaded.</summary>
public sealed class ModuleInit : IModuleAssemblyInitializer, IModuleAssemblyCleanup
{
    public void OnImport()
    {
        SubsystemManager.RegisterSubsystem<ICommandPredictor, PacxPredictor>(new PacxPredictor());

        // Load the tree now rather than in the background. pacx locks its history file while
        // running, so a background export racing the tab completer's own first-TAB export
        // would make one of them fail. Loading here (from the disk cache whenever possible),
        // registering pacx's own tab completer and seeding its cache means pacx does not
        // run at all on a normal shell start, and at most once after a pacx update.
        try
        {
            if (!TreeLoader.EnsureLoading().Wait(TimeSpan.FromSeconds(5)) || TreeLoader.RawJson is not { } json)
            {
                return;
            }
            if (TreeLoader.CompleterScript is { } completer) RegisterCompleter(completer);
            SeedCompleterCache(json);
        }
        catch
        {
            // Suggestions start once the tree is available; nothing else to do.
        }
    }

    /// <summary>Runs the script from "pacx completion powershell", which registers the TAB completer.</summary>
    private static void RegisterCompleter(string script)
    {
        try
        {
            using var ps = PowerShell.Create(RunspaceMode.CurrentRunspace);
            ps.AddScript(script).Invoke();
        }
        catch
        {
            // TAB then falls back to file names until the user registers the completer manually.
        }
    }

    public void OnRemove(PSModuleInfo psModuleInfo)
    {
        SubsystemManager.UnregisterSubsystem<ICommandPredictor>(PacxPredictor.PredictorId);
    }

    /// <summary>
    /// The completer from "pacx completion powershell" keeps its tree in
    /// $global:PacxCompletionCache and would otherwise run pacx itself on the first TAB.
    /// Internal "!..." commands are dropped so TAB never offers them first.
    /// </summary>
    private static void SeedCompleterCache(string json)
    {
        const string script = """
            param($json)
            if ($null -ne $global:PacxCompletionCache) { return }
            $data = $json | ConvertFrom-Json
            $data.commands   = @($data.commands   | Where-Object { -not ([string]$_.verbs[0]).StartsWith('!') })
            $data.namespaces = @($data.namespaces | Where-Object { -not ([string]$_.verbs[0]).StartsWith('!') })
            $global:PacxCompletionCache = $data
            """;
        try
        {
            using var ps = PowerShell.Create(RunspaceMode.CurrentRunspace);
            ps.AddScript(script).AddArgument(json).Invoke();
        }
        catch
        {
            // Best effort; the completer falls back to loading the tree itself.
        }
    }
}
