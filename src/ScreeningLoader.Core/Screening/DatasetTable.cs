using System.Globalization;
using System.Text.Json;
using ScreeningLoader.Core.Errors;

namespace ScreeningLoader.Core.Screening;

/// <summary>
/// El resultado tabular de una consulta al dataset.
/// </summary>
internal sealed class DatasetTable
{
    private readonly Dictionary<string, int> columns;
    private readonly List<JsonElement[]> rows;

    private DatasetTable(Dictionary<string, int> columns, List<JsonElement[]> rows)
    {
        this.columns = columns;
        this.rows = rows;
    }

    public IEnumerable<DatasetRow> Rows => rows.Select(cells => new DatasetRow(columns, cells));

    public static DatasetTable From(JsonElement payload)
    {
        if (payload.ValueKind is not JsonValueKind.Object
            || !payload.TryGetProperty("columns", out JsonElement columnNames)
            || columnNames.ValueKind is not JsonValueKind.Array
            || !payload.TryGetProperty("rows", out JsonElement rowValues)
            || rowValues.ValueKind is not JsonValueKind.Array)
        {
            throw new DatasetException(ErrorKind.Fatal, "SELECT", "El dataset no devolvió una tabla.");
        }

        Dictionary<string, int> columns = new(StringComparer.OrdinalIgnoreCase);
        int index = 0;

        foreach (JsonElement name in columnNames.EnumerateArray())
            columns[name.GetString() ?? string.Empty] = index++;

        List<JsonElement[]> rows = [.. rowValues.EnumerateArray().Select(row => row.EnumerateArray().ToArray())];

        return new DatasetTable(columns, rows);
    }
}

/// <summary>
/// Una fila del resultado, direccionable por nombre de columna.
/// </summary>
internal readonly struct DatasetRow(Dictionary<string, int> columns, JsonElement[] cells)
{
    public string Text(string column) => Cell(column) switch
    {
        { ValueKind: JsonValueKind.String } value => value.GetString() ?? string.Empty,
        { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => string.Empty,
        JsonElement value => value.ToString()
    };

    public int Int32(string column) =>
        Cell(column) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out int number)
            ? number
            : 0;

    /// <summary>
    /// Un datetime del dataset, que llega como texto plano sin zona y está expresado en UTC.
    /// </summary>
    public DateTimeOffset? Timestamp(string column) =>
        DateTimeOffset.TryParseExact(
            Text(column),
            "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out DateTimeOffset parsed)
                ? parsed
                : null;

    private JsonElement Cell(string column)
    {
        if (!columns.TryGetValue(column, out int index) || index >= cells.Length)
            throw new DatasetException(ErrorKind.Fatal, "SELECT", $"El dataset no devolvió la columna '{column}'.");

        return cells[index];
    }
}
