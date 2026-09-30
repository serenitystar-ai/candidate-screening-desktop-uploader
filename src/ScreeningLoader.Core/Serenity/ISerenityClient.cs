using System.Text.Json;
using ScreeningLoader.Core.Discovery;
using ScreeningLoader.Core.Run;
using ScreeningLoader.Core.Screening;

namespace ScreeningLoader.Core.Serenity;

/// <summary>
/// Superficie del AI Hub que consume el motor.
/// </summary>
public interface ISerenityClient
{
    /// <summary>
    /// Ejecuta una skill del agente.
    /// </summary>
    /// <returns>
    /// El contenido estructurado de la respuesta, o un elemento <c>Undefined</c> cuando la skill
    /// devolvió algo que no es JSON.
    /// </returns>
    Task<JsonElement> ExecuteSkillAsync(string skillCode, object? body, CancellationToken ct);

    /// <summary>
    /// Devuelve los agentes publicados a Nexus, para encontrar el que sirve la app.
    /// </summary>
    Task<IReadOnlyList<NexusAgent>> GetNexusAgentsAsync(CancellationToken ct);

    /// <summary>
    /// Devuelve el código de la skill del dataset que publica el agente.
    /// </summary>
    Task<string> GetDatasetSkillCodeAsync(CancellationToken ct);

    /// <summary>
    /// Devuelve los tipos de archivo que el agente acepta como volatile knowledge.
    /// </summary>
    Task<IReadOnlyList<string>> GetAcceptedMimeTypesAsync(CancellationToken ct);

    /// <summary>
    /// Sube un archivo y espera a que el AI Hub termine de procesarlo.
    /// </summary>
    Task<VolatileKnowledgeRecord> UploadAndAwaitAsync(CvFile file, Action<RunEvent> emit, CancellationToken ct);

    /// <summary>
    /// Analiza los CVs de un lote en un turno del agente y devuelve su recibo.
    /// </summary>
    Task<AnalyzeReceipt> AnalyzeAsync(
        JobOpening opening,
        IReadOnlyList<UploadedCv> cvs,
        Action<RunEvent> emit,
        CancellationToken ct);
}
