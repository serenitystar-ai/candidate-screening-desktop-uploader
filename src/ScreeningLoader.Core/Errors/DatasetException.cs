namespace ScreeningLoader.Core.Errors;

/// <summary>
/// Failure of an operation against the dataset.
/// </summary>
public sealed class DatasetException(
    ErrorKind kind,
    string operation,
    string message,
    Exception? inner = null)
    : ScreeningLoaderException(kind, message, inner)
{
    /// <summary>Verb of the statement that failed.</summary>
    public string Operation { get; } = operation;
}
