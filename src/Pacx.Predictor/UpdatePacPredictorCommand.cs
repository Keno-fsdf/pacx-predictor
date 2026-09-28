using System.Diagnostics;
using System.Management.Automation;

namespace Pacx.Predictor;

/// <summary>
/// Builds the pac command tree from the installed pac and caches it. Takes a couple of
/// minutes and shows progress; run it once after installing the module and again after a
/// pac update. Until then pac lines get no suggestions.
/// </summary>
[Cmdlet(VerbsData.Update, "PacPredictor")]
[OutputType(typeof(string))]
public sealed class UpdatePacPredictorCommand : PSCmdlet
{
    protected override void ProcessRecord()
    {
        var watch = Stopwatch.StartNew();
        var progress = new ProgressRecord(1, "Update-PacPredictor", "Reading pac help pages ...");
        WriteProgress(progress);

        // The builder reports from worker threads, but a cmdlet may only write from the
        // pipeline thread: count over there, report from here while waiting.
        var pages = 0;
        var build = Task.Run(() => PacTreeLoader.Rebuild(n => Volatile.Write(ref pages, n)));

        while (!build.Wait(TimeSpan.FromMilliseconds(500)))
        {
            progress.StatusDescription = $"{Volatile.Read(ref pages)} pages read, {watch.Elapsed.TotalSeconds:0} s";
            WriteProgress(progress);
        }
        progress.RecordType = ProgressRecordType.Completed;
        WriteProgress(progress);

        if (build.IsFaulted)
        {
            var inner = build.Exception?.InnerException ?? build.Exception ?? new Exception("Build failed.");
            ThrowTerminatingError(new ErrorRecord(inner, "PacTreeBuildFailed", ErrorCategory.NotSpecified, null));
            return;
        }

        var tree = build.Result;
        WriteObject($"pac: {tree.Commands.Count} commands, {tree.Namespaces.Count} groups, "
                    + $"{watch.Elapsed.TotalSeconds:0} s. Cached in {PacTreeLoader.CacheDirectory}.");
    }
}
