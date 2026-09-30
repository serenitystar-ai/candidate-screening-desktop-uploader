using System.Net;
using System.Text;

namespace ScreeningLoader.Core.Errors;

/// <summary>
/// Diagnostic log of the run.
/// </summary>
public sealed class ErrorLog(string logDirectory)
{
    private const int MessageLimit = 400;

    private static readonly UTF8Encoding s_logEncoding = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Lock gate = new();

    /// <summary>
    /// Folder where the engine writes its logs, deliberately away from the CV folder.
    /// </summary>
    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CandidateScreeningLoader",
        "logs");

    /// <summary>
    /// Logs the failure of an operation.
    /// </summary>
    public void Write(string operation, Exception ex) =>
        Append($"{operation} · {ex.GetType().Name} · {Describe(ex)}");

    /// <summary>
    /// Logs a diagnostic line.
    /// </summary>
    public void Write(string message) => Append(message);

    private void Append(string line)
    {
        string entry = $"{DateTimeOffset.UtcNow:O}  {line}{Environment.NewLine}";

        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(logDirectory);
                File.AppendAllText(CurrentFile(), entry, s_logEncoding);
            }
        }
        catch (Exception)
        {
            // A log that cannot be written must not bring down the run.
        }
    }

    private string CurrentFile() =>
        Path.Combine(logDirectory, $"screening-loader-{DateTime.UtcNow:yyyy-MM-dd}.log");

    /// <summary>
    /// Describes the exception without dumping content that could carry a candidate's data.
    /// </summary>
    private static string Describe(Exception ex) => ex switch
    {
        // The message of a dataset failure carries the full statement, with the row inside it.
        DatasetException dataset => $"dataset {dataset.Operation} status={Format(dataset.StatusCode)}",
        ScreeningLoaderException loader => $"{loader.Kind} status={Format(loader.StatusCode)} · {Sanitize(loader.Message)}",
        HttpRequestException http => $"http status={Format(http.StatusCode)}",
        _ => Sanitize(ex.Message)
    };

    private static string Format(HttpStatusCode? status) =>
        status is null ? "-" : ((int)status).ToString();

    /// <summary>
    /// Keeps the message on a single line and bounded, so that one entry stays one entry.
    /// </summary>
    private static string Sanitize(string message)
    {
        string flattened = message.ReplaceLineEndings(" ").Trim();

        return flattened.Length <= MessageLimit ? flattened : flattened[..MessageLimit] + "…";
    }
}
