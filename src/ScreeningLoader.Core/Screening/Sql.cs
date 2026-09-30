using ScreeningLoader.Core.Errors;

namespace ScreeningLoader.Core.Screening;

/// <summary>
/// Construye los literales SQL que el motor envía al dataset.
/// </summary>
internal static class Sql
{
    /// <summary>
    /// Devuelve un GUID como literal, o lanza si el valor no es un GUID.
    /// </summary>
    public static string Id(string value)
    {
        if (!Guid.TryParse(value, out Guid id))
            throw new ScreeningLoaderException(ErrorKind.Fatal, $"'{value}' no es un identificador válido.");

        return $"'{id}'";
    }

    /// <summary>
    /// Devuelve una lista de GUIDs como literal para una cláusula IN.
    /// </summary>
    public static string IdList(IEnumerable<string> values)
    {
        string joined = string.Join(", ", values.Select(Id));

        if (joined.Length == 0)
            throw new ScreeningLoaderException(ErrorKind.Fatal, "La lista de identificadores no puede estar vacía.");

        return joined;
    }

    /// <summary>
    /// Devuelve un texto de una sola línea como literal.
    /// </summary>
    public static string Text(string value)
    {
        // La barra invertida se saca en vez de duplicarse: duplicarla es correcto en MySQL y guarda un par
        // literal en Postgres, y no sabemos qué motor hay detrás del plugin. Sacarla es seguro en todos.
        string cleaned = new(value.Where(c => c is not '\\' && !char.IsControl(c)).ToArray());

        if (cleaned.Length > MaxTextLength)
            cleaned = cleaned[..MaxTextLength];

        return $"'{cleaned.Replace("'", "''")}'";
    }

    /// <summary>
    /// Devuelve una lista de textos como literal para una cláusula IN.
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
