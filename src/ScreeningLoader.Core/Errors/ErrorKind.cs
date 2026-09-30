namespace ScreeningLoader.Core.Errors;

/// <summary>
/// How the run reacts to an error.
/// </summary>
public enum ErrorKind
{
    /// <summary>Resolved by waiting; retried with backoff.</summary>
    Transient,

    /// <summary>Affects a single file; it goes to fallidos and the run continues.</summary>
    File,

    /// <summary>The run cannot continue.</summary>
    Fatal
}
