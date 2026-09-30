using System.Text.Json;
using ScreeningLoader.Core.Serenity;

namespace ScreeningLoader.Core.Screening;

/// <summary>
/// Dataset queries through the plugin's skill.
/// </summary>
public sealed class ScreeningClient(ISerenityClient serenityClient, string datasetSkillCode) : IScreeningClient
{
    private const string OpeningColumns =
        "id, title, department, location, employment_type, seniority, status, description, created_at";

    public async Task<IReadOnlyList<JobOpening>> ListJobOpeningsAsync(CancellationToken ct)
    {
        // Two round trips because the plugin accepts one statement per call, not because the data calls for it.
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
    /// Runs a read statement and returns its table.
    /// </summary>
    private async Task<DatasetTable> SelectAsync(string sql, string[] tablesUsed, CancellationToken ct)
    {
        // A query with no matches comes back 200 with empty rows, so a 404 here is not "no rows":
        // it means the skill code does not exist, and translating it to an empty list would hide that.
        JsonElement payload = await serenityClient.ExecuteSkillAsync(datasetSkillCode, new { sql, tablesUsed }, ct);

        return DatasetTable.From(payload);
    }
}
