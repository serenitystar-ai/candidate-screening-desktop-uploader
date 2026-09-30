namespace ScreeningLoader.Core.Errors;

/// <summary>
/// Fallo de una operación contra el dataset.
/// </summary>
public sealed class DatasetException(
    ErrorKind kind,
    string operation,
    string message,
    Exception? inner = null)
    : ScreeningLoaderException(kind, message, inner)
{
    /// <summary>Verbo de la sentencia que falló.</summary>
    public string Operation { get; } = operation;
}
