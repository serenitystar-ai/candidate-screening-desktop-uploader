namespace ScreeningLoader.Core.Errors;

/// <summary>
/// Cómo reacciona la corrida ante un error.
/// </summary>
public enum ErrorKind
{
    /// <summary>Se resuelve esperando; se reintenta con backoff.</summary>
    Transient,

    /// <summary>Afecta a un solo archivo; va a fallidos y la corrida sigue.</summary>
    File,

    /// <summary>La corrida no puede continuar.</summary>
    Fatal
}
