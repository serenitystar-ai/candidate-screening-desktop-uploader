namespace ScreeningLoader.Core.Serenity;

/// <summary>
/// Resultado de una subida de volatile knowledge.
/// </summary>
public sealed record VolatileKnowledgeRecord
{
    /// <summary>Identificador que ancla el archivo a la ejecución.</summary>
    public required Guid Id { get; init; }

    /// <summary>Identificador con el que se descarga el archivo; puede venir vacío.</summary>
    public Guid? FileId { get; init; }

    public required string Status { get; init; }

    /// <summary>El archivo terminó de procesarse y el agente puede leerlo.</summary>
    public bool IsReady => Status.Equals(Ready, StringComparison.OrdinalIgnoreCase);

    /// <summary>El Hub todavía lo está procesando.</summary>
    public bool IsPending => Status.Equals(Pending, StringComparison.OrdinalIgnoreCase);

    private const string Ready = "success";
    private const string Pending = "analyzing";
}
