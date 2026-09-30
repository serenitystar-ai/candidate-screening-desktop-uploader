namespace ScreeningLoader.Core.Screening;

/// <summary>
/// Una búsqueda laboral con su conteo de candidatos.
/// </summary>
public sealed record JobOpening(
    string Id,
    string Title,
    string Department,
    string Location,
    string EmploymentType,
    string Seniority,
    string Status,
    string Description,
    DateTimeOffset? CreatedAt,
    int CandidateCount)
{
    internal static IReadOnlyList<JobOpening> From(DatasetTable openings, DatasetTable counts)
    {
        Dictionary<string, int> byOpening = new(StringComparer.OrdinalIgnoreCase);

        foreach (DatasetRow row in counts.Rows)
            byOpening[row.Text("job_opening_id")] = row.Int32("candidate_count");

        return
        [
            .. openings.Rows.Select(row =>
            {
                string id = row.Text("id");

                return new JobOpening(
                    id,
                    row.Text("title"),
                    row.Text("department"),
                    row.Text("location"),
                    row.Text("employment_type"),
                    row.Text("seniority"),
                    row.Text("status"),
                    row.Text("description"),
                    row.Timestamp("created_at"),
                    byOpening.GetValueOrDefault(id));
            })
        ];
    }
}
