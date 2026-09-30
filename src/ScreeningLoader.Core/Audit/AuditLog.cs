using System.Text;
using ScreeningLoader.Core.Discovery;
using ScreeningLoader.Core.Run;

namespace ScreeningLoader.Core.Audit;

/// <summary>
/// Log of a run, for auditing and diagnostics.
/// </summary>
public sealed class AuditLog
{
    private const int ReasonLimit = 120;

    private static readonly UTF8Encoding s_logEncoding = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Lock gate = new();

    private readonly string path;
    private readonly string runId;
    private readonly string openingId;
    private readonly TimeProvider timeProvider;

    public AuditLog(
        string logDirectory,
        string openingId,
        string openingTitle,
        TimeProvider timeProvider,
        int retentionDays)
    {
        this.openingId = openingId;
        this.timeProvider = timeProvider;

        runId = Guid.NewGuid().ToString("N")[..8];

        Directory.CreateDirectory(logDirectory);
        path = Path.Combine(logDirectory, $"run-{timeProvider.GetUtcNow():yyyyMMdd-HHmmss}-{runId}.log");

        Prune(logDirectory, timeProvider, retentionDays);

        // The title goes in the log so the history does not depend on the job opening still being in the dataset.
        // A job title is not personal data, so it does not break the rule against logging candidate data.
        Write($"opening-title={Sanitize(openingTitle)}");
    }

    /// <summary>A file whose row was written and that was archived.</summary>
    public void Processed(int batchNumber, string fileName) =>
        Write($"batch={batchNumber} file={fileName} inserted moved");

    /// <summary>A file that never got a row.</summary>
    public void ToReview(int batchNumber, string fileName, string reason) =>
        Write($"batch={batchNumber} file={fileName} failed reason={Sanitize(reason)}");

    /// <summary>A file the filter left out before anything was uploaded.</summary>
    public void Rejected(string fileName, RejectionReason reason) =>
        Write($"file={fileName} rejected reason={reason}");

    /// <summary>A file that stayed where it was because it could not be moved.</summary>
    public void MoveFailed(int batchNumber, FailedMove move) =>
        Write($"batch={batchNumber} file={move.FileName} move-failed reason={Sanitize(move.Reason)}");

    /// <summary>A wait before retrying.</summary>
    public void Retrying(int batchNumber, RetryWaiting waiting) =>
        Write(
            $"batch={batchNumber} retry op={waiting.Operation} attempt={waiting.Attempt} "
            + $"delay={waiting.Delay.TotalSeconds:F0}s reason={Sanitize(waiting.Reason)}");

    /// <summary>The end of the run, with its totals.</summary>
    public void Completed(RunTotals totals) =>
        Write(
            $"run-completed processed={totals.Processed} to-review={totals.ToReview} "
            + $"rejected={totals.Rejected} move-failed={totals.FailedMoves}");

    private void Write(string line)
    {
        // File name, outcome and timestamp. Never the candidate's name, email, phone number or
        // a single line of their observations.
        string entry = $"{timeProvider.GetUtcNow():O}  run={runId} opening={openingId}  {line}{Environment.NewLine}";

        try
        {
            lock (gate)
                File.AppendAllText(path, entry, s_logEncoding);
        }
        catch (Exception)
        {
            // A log that cannot be written must not bring down the run.
        }
    }

    private static string Sanitize(string reason)
    {
        string flattened = reason.ReplaceLineEndings(" ").Trim();

        return flattened.Length <= ReasonLimit ? flattened : flattened[..ReasonLimit] + "…";
    }

    /// <summary>
    /// Deletes the logs of old runs, which are low-grade personal data piling up.
    /// </summary>
    private static void Prune(string logDirectory, TimeProvider timeProvider, int retentionDays)
    {
        try
        {
            DateTimeOffset cutoff = timeProvider.GetUtcNow().AddDays(-retentionDays);

            foreach (string old in Directory.EnumerateFiles(logDirectory, "run-*.log"))
            {
                if (File.GetLastWriteTimeUtc(old) < cutoff)
                    File.Delete(old);
            }
        }
        catch (Exception)
        {
        }
    }
}
