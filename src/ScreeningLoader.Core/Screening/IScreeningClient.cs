namespace ScreeningLoader.Core.Screening;

/// <summary>
/// The dataset queries the engine makes.
/// </summary>
public interface IScreeningClient
{
    /// <summary>
    /// Returns the organization's job openings, each with its candidate count.
    /// </summary>
    Task<IReadOnlyList<JobOpening>> ListJobOpeningsAsync(CancellationToken ct);

    /// <summary>
    /// Returns the file that produced each of the given rows.
    /// </summary>
    Task<IReadOnlyList<InsertedCandidate>> GetInsertedAsync(
        IReadOnlyList<string> ids,
        CancellationToken ct);

    /// <summary>
    /// Returns the rows of a job opening that match any of the given files.
    /// </summary>
    Task<IReadOnlyList<InsertedCandidate>> GetInsertedByFileNameAsync(
        string jobOpeningId,
        IReadOnlyList<string> fileNames,
        CancellationToken ct);
}
