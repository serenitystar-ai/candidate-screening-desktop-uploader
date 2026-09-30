using System.Text.Json;
using ScreeningLoader.Core.Discovery;
using ScreeningLoader.Core.Run;
using ScreeningLoader.Core.Screening;

namespace ScreeningLoader.Core.Serenity;

/// <summary>
/// The AI Hub surface the engine consumes.
/// </summary>
public interface ISerenityClient
{
    /// <summary>
    /// Executes one of the agent's skills.
    /// </summary>
    /// <returns>
    /// The structured content of the response, or an <c>Undefined</c> element when the skill
    /// returned something that is not JSON.
    /// </returns>
    Task<JsonElement> ExecuteSkillAsync(string skillCode, object? body, CancellationToken ct);

    /// <summary>
    /// Returns the agents published to Nexus, to find the one that serves the app.
    /// </summary>
    Task<IReadOnlyList<NexusAgent>> GetNexusAgentsAsync(CancellationToken ct);

    /// <summary>
    /// Returns the code of the dataset skill the agent publishes.
    /// </summary>
    Task<string> GetDatasetSkillCodeAsync(CancellationToken ct);

    /// <summary>
    /// Returns the file types the agent accepts as volatile knowledge.
    /// </summary>
    Task<IReadOnlyList<string>> GetAcceptedMimeTypesAsync(CancellationToken ct);

    /// <summary>
    /// Uploads a file and waits for the AI Hub to finish processing it.
    /// </summary>
    Task<VolatileKnowledgeRecord> UploadAndAwaitAsync(CvFile file, Action<RunEvent> emit, CancellationToken ct);

    /// <summary>
    /// Analyzes a batch's CVs in one agent turn and returns its receipt.
    /// </summary>
    Task<AnalyzeReceipt> AnalyzeAsync(
        JobOpening opening,
        IReadOnlyList<UploadedCv> cvs,
        Action<RunEvent> emit,
        CancellationToken ct);
}
