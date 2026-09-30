using System.Net;
using System.Text;

namespace ScreeningLoader.Core.Errors;

/// <summary>
/// Registro de diagnóstico de la corrida.
/// </summary>
public sealed class ErrorLog(string logDirectory)
{
    private const int MessageLimit = 400;

    private static readonly UTF8Encoding s_logEncoding = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Lock gate = new();

    /// <summary>
    /// Carpeta donde el motor deja sus logs, deliberadamente lejos de la carpeta de CVs.
    /// </summary>
    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CandidateScreeningLoader",
        "logs");

    /// <summary>
    /// Registra el fallo de una operación.
    /// </summary>
    public void Write(string operation, Exception ex) =>
        Append($"{operation} · {ex.GetType().Name} · {Describe(ex)}");

    /// <summary>
    /// Registra una línea de diagnóstico.
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
            // Un log que no se puede escribir no puede tirar abajo la corrida.
        }
    }

    private string CurrentFile() =>
        Path.Combine(logDirectory, $"screening-loader-{DateTime.UtcNow:yyyy-MM-dd}.log");

    /// <summary>
    /// Describe la excepción sin volcar contenido que pueda traer datos de un candidato.
    /// </summary>
    private static string Describe(Exception ex) => ex switch
    {
        // El mensaje de un fallo del dataset arrastra la sentencia completa, con la fila adentro.
        DatasetException dataset => $"dataset {dataset.Operation} status={Format(dataset.StatusCode)}",
        ScreeningLoaderException loader => $"{loader.Kind} status={Format(loader.StatusCode)} · {Sanitize(loader.Message)}",
        HttpRequestException http => $"http status={Format(http.StatusCode)}",
        _ => Sanitize(ex.Message)
    };

    private static string Format(HttpStatusCode? status) =>
        status is null ? "-" : ((int)status).ToString();

    /// <summary>
    /// Deja el mensaje en una sola línea y acotado, para que una entrada siga siendo una entrada.
    /// </summary>
    private static string Sanitize(string message)
    {
        string flattened = message.ReplaceLineEndings(" ").Trim();

        return flattened.Length <= MessageLimit ? flattened : flattened[..MessageLimit] + "…";
    }
}
