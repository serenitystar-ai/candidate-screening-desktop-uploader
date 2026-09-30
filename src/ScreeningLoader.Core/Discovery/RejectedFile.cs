namespace ScreeningLoader.Core.Discovery;

/// <summary>
/// Un archivo que no entra a la corrida, y por qué.
/// </summary>
public sealed record RejectedFile(string FileName, RejectionReason Reason);

/// <summary>
/// Motivo por el que un archivo queda afuera antes de subirse.
/// </summary>
public enum RejectionReason
{
    /// <summary>El agente no acepta ese tipo de archivo.</summary>
    UnsupportedType,

    /// <summary>No tiene contenido.</summary>
    Empty,

    /// <summary>Supera el tamaño máximo por archivo.</summary>
    TooLarge,

    /// <summary>Por sí solo excede el presupuesto de peso de un lote.</summary>
    ExceedsBatchBudget,

    /// <summary>La persona lo dejó fuera de esta corrida.</summary>
    ExcludedByUser
}
