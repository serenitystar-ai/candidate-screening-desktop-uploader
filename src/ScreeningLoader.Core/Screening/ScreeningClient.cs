using System.Text.Json;
using ScreeningLoader.Core.Serenity;

namespace ScreeningLoader.Core.Screening;

/// <summary>
/// Consultas al dataset a través del skill del plugin.
/// </summary>
public sealed class ScreeningClient(ISerenityClient serenityClient, string datasetSkillCode) : IScreeningClient
{
    private const string OpeningColumns =
        "id, title, department, location, employment_type, seniority, status, description, created_at";

    public async Task<IReadOnlyList<JobOpening>> ListJobOpeningsAsync(CancellationToken ct)
    {
        // Dos viajes porque el plugin acepta una sentencia por llamada, no porque los datos lo pidan.
        Task<DatasetTable> openings = SelectAsync(
            $"SELECT {OpeningColumns} FROM Jobopenings ORDER BY created_at DESC",
            ["Jobopenings"],
            ct);

        Task<DatasetTable> counts = SelectAsync(
            "SELECT job_opening_id, COUNT(*) AS candidate_count FROM Candidates GROUP BY job_opening_id",
            ["Candidates"],
            ct);

        await Task.WhenAll(openings, counts);

        return JobOpening.From(await openings, await counts);
    }

    public async Task<IReadOnlyList<InsertedCandidate>> GetInsertedAsync(
        IReadOnlyList<string> ids,
        CancellationToken ct)
    {
        if (ids.Count == 0)
            return [];

        DatasetTable table = await SelectAsync(
            $"SELECT id, cv_file_name FROM Candidates WHERE id IN ({Sql.IdList(ids)})",
            ["Candidates"],
            ct);

        return InsertedCandidate.From(table);
    }

    public async Task<IReadOnlyList<InsertedCandidate>> GetInsertedByFileNameAsync(
        string jobOpeningId,
        IReadOnlyList<string> fileNames,
        CancellationToken ct)
    {
        if (fileNames.Count == 0)
            return [];

        DatasetTable table = await SelectAsync(
            $"SELECT id, cv_file_name FROM Candidates WHERE job_opening_id = {Sql.Id(jobOpeningId)} "
                + $"AND cv_file_name IN ({Sql.TextList(fileNames)})",
            ["Candidates"],
            ct);

        return InsertedCandidate.From(table);
    }

    /// <summary>
    /// Corre una sentencia de lectura y devuelve su tabla.
    /// </summary>
    private async Task<DatasetTable> SelectAsync(string sql, string[] tablesUsed, CancellationToken ct)
    {
        // Una consulta sin coincidencias vuelve 200 con rows vacío, así que un 404 acá no es "no hay filas":
        // es que el código de la skill no existe, y traducirlo a lista vacía lo escondería.
        JsonElement payload = await serenityClient.ExecuteSkillAsync(datasetSkillCode, new { sql, tablesUsed }, ct);

        return DatasetTable.From(payload);
    }
}
