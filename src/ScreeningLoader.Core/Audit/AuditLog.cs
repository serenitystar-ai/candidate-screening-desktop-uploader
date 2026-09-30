using System.Text;
using ScreeningLoader.Core.Discovery;
using ScreeningLoader.Core.Run;

namespace ScreeningLoader.Core.Audit;

/// <summary>
/// Registro de una corrida, para auditoría y diagnóstico.
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

        // El título va en el registro para que el historial no dependa de que la oferta siga en el dataset.
        // Un puesto no es un dato personal, así que no toca la regla de no registrar datos del candidato.
        Write($"opening-title={Sanitize(openingTitle)}");
    }

    /// <summary>Un archivo con su fila escrita y archivado.</summary>
    public void Processed(int batchNumber, string fileName) =>
        Write($"batch={batchNumber} file={fileName} inserted moved");

    /// <summary>Un archivo que no llegó a tener fila.</summary>
    public void ToReview(int batchNumber, string fileName, string reason) =>
        Write($"batch={batchNumber} file={fileName} failed reason={Sanitize(reason)}");

    /// <summary>Un archivo que el filtro dejó afuera antes de subir nada.</summary>
    public void Rejected(string fileName, RejectionReason reason) =>
        Write($"file={fileName} rejected reason={reason}");

    /// <summary>Un archivo que quedó donde estaba porque no se pudo mover.</summary>
    public void MoveFailed(int batchNumber, FailedMove move) =>
        Write($"batch={batchNumber} file={move.FileName} move-failed reason={Sanitize(move.Reason)}");

    /// <summary>Una espera antes de reintentar.</summary>
    public void Retrying(int batchNumber, RetryWaiting waiting) =>
        Write(
            $"batch={batchNumber} retry op={waiting.Operation} attempt={waiting.Attempt} "
            + $"delay={waiting.Delay.TotalSeconds:F0}s reason={Sanitize(waiting.Reason)}");

    /// <summary>El cierre de la corrida, con sus totales.</summary>
    public void Completed(RunTotals totals) =>
        Write(
            $"run-completed processed={totals.Processed} to-review={totals.ToReview} "
            + $"rejected={totals.Rejected} move-failed={totals.FailedMoves}");

    private void Write(string line)
    {
        // Nombre de archivo, resultado y momento. Nunca el nombre del candidato, su mail, su teléfono ni
        // una línea de sus observaciones.
        string entry = $"{timeProvider.GetUtcNow():O}  run={runId} opening={openingId}  {line}{Environment.NewLine}";

        try
        {
            lock (gate)
                File.AppendAllText(path, entry, s_logEncoding);
        }
        catch (Exception)
        {
            // Un registro que no se puede escribir no puede tirar abajo la corrida.
        }
    }

    private static string Sanitize(string reason)
    {
        string flattened = reason.ReplaceLineEndings(" ").Trim();

        return flattened.Length <= ReasonLimit ? flattened : flattened[..ReasonLimit] + "…";
    }

    /// <summary>
    /// Borra los registros de corridas viejas, que son datos personales de bajo grado acumulándose.
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
