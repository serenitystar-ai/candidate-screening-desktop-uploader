using System.Text.Json;

namespace ScreeningLoader.Core;

/// <summary>
/// Lo que la persona cambió de la configuración del motor. Null en cada campo que no tocó.
/// </summary>
/// <remarks>
/// Se guarda como capa sobre los valores por defecto y no como la configuración entera: así un campo nuevo
/// en <see cref="ScreeningLoaderOptions"/> no queda congelado con su valor viejo en el archivo de nadie.
/// </remarks>
public sealed record SettingsOverlay(
    string? AppCode,
    int? MaxFileSizeMb,
    string? ResponseLanguage,
    int? AuditLogRetentionDays,
    int? BatchSize,
    int? MaxBatchMb)
{
    public static SettingsOverlay Empty { get; } = new(null, null, null, null, null, null);
}

/// <summary>
/// Preferencias locales que sobreviven al cierre de la aplicación.
/// </summary>
public sealed class Preferences(string path)
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Archivo donde se guardan, junto al resto del estado local de la aplicación.
    /// </summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CandidateScreeningLoader",
        "preferences.json");

    /// <summary>
    /// Devuelve la carpeta que se usó la última vez para esta oferta, si todavía existe.
    /// </summary>
    public string? FolderFor(string openingId) =>
        Read().Folders.GetValueOrDefault(openingId) is { } folder && Directory.Exists(folder) ? folder : null;

    /// <summary>
    /// Recuerda la carpeta de una oferta, para no volver a buscarla en la próxima corrida.
    /// </summary>
    public void RememberFolder(string openingId, string folder)
    {
        Stored stored = Read();

        stored.Folders[openingId] = folder;

        Save(stored);
    }

    /// <summary>
    /// Devuelve los ajustes guardados.
    /// </summary>
    public SettingsOverlay ReadSettings() => Read().Settings ?? SettingsOverlay.Empty;

    /// <summary>
    /// Guarda los ajustes, conservando el resto de las preferencias.
    /// </summary>
    public void SaveSettings(SettingsOverlay settings)
    {
        Stored stored = Read();

        stored.Settings = settings;

        Save(stored);
    }

    private void Save(Stored stored)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(stored, s_json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Una preferencia que no se puede guardar cuesta un clic más, no la corrida.
        }
    }

    private Stored Read()
    {
        try
        {
            return JsonSerializer.Deserialize<Stored>(File.ReadAllText(path), s_json) ?? new Stored();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Stored();
        }
    }

    private sealed class Stored
    {
        public Dictionary<string, string> Folders { get; init; } = [];

        public SettingsOverlay? Settings { get; set; }
    }
}
