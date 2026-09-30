namespace ScreeningLoader.Core.Screening;

/// <summary>
/// An inserted row and the file that produced it.
/// </summary>
public sealed record InsertedCandidate(string Id, string CvFileName)
{
    internal static IReadOnlyList<InsertedCandidate> From(DatasetTable table) =>
    [
        .. table.Rows
            .Select(row => new InsertedCandidate(row.Text("id"), row.Text("cv_file_name")))
            .Where(candidate => candidate.CvFileName.Length > 0)
    ];
}
