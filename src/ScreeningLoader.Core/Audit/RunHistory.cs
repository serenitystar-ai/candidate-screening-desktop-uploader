using System.Globalization;
using System.Text.RegularExpressions;
using ScreeningLoader.Core.Run;

namespace ScreeningLoader.Core.Audit;

/// <summary>
/// Reads the audit logs of previous runs.
/// </summary>
public sealed partial class RunHistory(string logDirectory)
{
    private const string FilePattern = "run-*.log";
    private const string StampFormat = "yyyyMMdd-HHmmss";
    private const string TitleMarker = "opening-title=";

    private static readonly string[] s_fieldSeparator = ["  "];

    /// <summary>
    /// Returns the logged runs, from newest to oldest.
    /// </summary>
    public IReadOnlyList<RunRecord> List(int limit)
    {
        if (!Directory.Exists(logDirectory))
            return [];

        return
        [
            .. Directory.EnumerateFiles(logDirectory, FilePattern)
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                .Take(limit)
                .Select(Read)
                .OfType<RunRecord>()
        ];
    }

    /// <summary>
    /// Returns a run by its identifier, or null if its log is no longer there.
    /// </summary>
    public RunRecord? Find(string runId)
    {
        if (!Directory.Exists(logDirectory))
            return null;

        return Directory.EnumerateFiles(logDirectory, FilePattern)
            .Where(file => Path.GetFileNameWithoutExtension(file).EndsWith($"-{runId}", StringComparison.Ordinal))
            .Select(Read)
            .OfType<RunRecord>()
            .FirstOrDefault();
    }

    private static RunRecord? Read(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        string[] parts = name.Split('-');

        // run-{yyyyMMdd}-{HHmmss}-{id}
        if (parts.Length != 4)
            return null;

        if (!DateTimeOffset.TryParseExact(
                $"{parts[1]}-{parts[2]}",
                StampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out DateTimeOffset startedAt))
        {
            return null;
        }

        string[] lines;

        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        string openingId = string.Empty;
        string openingTitle = string.Empty;
        RunTotals? totals = null;
        List<FailedCv> toReview = [];

        foreach (string line in lines)
        {
            // The log is written as «timestamp  run/opening  detail», and the detail can carry a
            // file name with spaces in it.
            string[] fields = line.Split(s_fieldSeparator, 3, StringSplitOptions.None);

            if (fields.Length != 3)
                continue;

            if (openingId.Length == 0)
                openingId = OpeningOf(fields[1]);

            string detail = fields[2];

            if (detail.StartsWith(TitleMarker, StringComparison.Ordinal))
            {
                openingTitle = detail[TitleMarker.Length..];
            }
            else if (Completed().Match(detail) is { Success: true } completed)
            {
                totals = new RunTotals(
                    int.Parse(completed.Groups["processed"].Value, CultureInfo.InvariantCulture),
                    int.Parse(completed.Groups["review"].Value, CultureInfo.InvariantCulture),
                    int.Parse(completed.Groups["rejected"].Value, CultureInfo.InvariantCulture),
                    int.Parse(completed.Groups["moves"].Value, CultureInfo.InvariantCulture));
            }
            else if (Failed().Match(detail) is { Success: true } failed)
            {
                toReview.Add(new FailedCv(failed.Groups["file"].Value, failed.Groups["reason"].Value));
            }
        }

        return new RunRecord(parts[3], startedAt, openingId, openingTitle, totals, toReview);
    }

    private static string OpeningOf(string field)
    {
        const string Marker = "opening=";

        int start = field.IndexOf(Marker, StringComparison.Ordinal);

        return start < 0 ? string.Empty : field[(start + Marker.Length)..].Trim();
    }

    [GeneratedRegex(
        @"^run-completed processed=(?<processed>\d+) to-review=(?<review>\d+) "
        + @"rejected=(?<rejected>\d+) move-failed=(?<moves>\d+)$")]
    private static partial Regex Completed();

    [GeneratedRegex(@"^batch=\d+ file=(?<file>.+?) failed reason=(?<reason>.*)$")]
    private static partial Regex Failed();
}
