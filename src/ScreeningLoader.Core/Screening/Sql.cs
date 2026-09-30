using ScreeningLoader.Core.Errors;

namespace ScreeningLoader.Core.Screening;

/// <summary>
/// Builds the SQL literals the engine sends to the dataset.
/// </summary>
internal static class Sql
{
    /// <summary>
    /// Returns a GUID as a literal, or throws if the value is not a GUID.
    /// </summary>
    public static string Id(string value)
    {
        if (!Guid.TryParse(value, out Guid id))
            throw new ScreeningLoaderException(ErrorKind.Fatal, $"'{value}' no es un identificador válido.");

        return $"'{id}'";
    }

    /// <summary>
    /// Returns a list of GUIDs as a literal for an IN clause.
    /// </summary>
    public static string IdList(IEnumerable<string> values)
    {
        string joined = string.Join(", ", values.Select(Id));

        if (joined.Length == 0)
            throw new ScreeningLoaderException(ErrorKind.Fatal, "La lista de identificadores no puede estar vacía.");

        return joined;
    }

    /// <summary>
    /// Returns a single-line text as a literal.
    /// </summary>
    public static string Text(string value)
    {
        // The backslash is removed instead of doubled: doubling it is correct in MySQL but stores a literal
        // pair in Postgres, and we don't know which engine is behind the plugin. Removing it is safe in all.
        string cleaned = new(value.Where(c => c is not '\\' && !char.IsControl(c)).ToArray());

        if (cleaned.Length > MaxTextLength)
            cleaned = cleaned[..MaxTextLength];

        return $"'{cleaned.Replace("'", "''")}'";
    }

    /// <summary>
    /// Returns a list of texts as a literal for an IN clause.
    /// </summary>
    public static string TextList(IEnumerable<string> values)
    {
        string joined = string.Join(", ", values.Select(Text));

        if (joined.Length == 0)
            throw new ScreeningLoaderException(ErrorKind.Fatal, "La lista de valores no puede estar vacía.");

        return joined;
    }

    private const int MaxTextLength = 400;
}
