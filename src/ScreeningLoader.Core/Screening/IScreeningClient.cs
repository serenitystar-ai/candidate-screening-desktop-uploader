namespace ScreeningLoader.Core.Screening;

/// <summary>
/// Las consultas al dataset que hace el motor.
/// </summary>
public interface IScreeningClient
{
    /// <summary>
    /// Devuelve las búsquedas laborales de la organización, cada una con su conteo de candidatos.
    /// </summary>
    Task<IReadOnlyList<JobOpening>> ListJobOpeningsAsync(CancellationToken ct);

    /// <summary>
    /// Devuelve el archivo que produjo cada una de las filas indicadas.
    /// </summary>
    Task<IReadOnlyList<InsertedCandidate>> GetInsertedAsync(
        IReadOnlyList<string> ids,
        CancellationToken ct);

    /// <summary>
    /// Devuelve las filas de una búsqueda que corresponden a alguno de los archivos indicados.
    /// </summary>
    Task<IReadOnlyList<InsertedCandidate>> GetInsertedByFileNameAsync(
        string jobOpeningId,
        IReadOnlyList<string> fileNames,
        CancellationToken ct);
}
