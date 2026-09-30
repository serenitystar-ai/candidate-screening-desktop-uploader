using System.Text.Json;

namespace ScreeningLoader.Core;

/// <summary>
/// What the user changed in the engine configuration. Null in every field they did not touch.
/// </summary>
/// <remarks>
/// Saved as a layer over the defaults rather than as the whole configuration: that way a new field
/// in <see cref="ScreeningLoaderOptions"/> does not stay frozen at its old value in anyone's file.
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
/// Local preferences that survive closing the application.
/// </summary>
public sealed class Preferences(string path)
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// File where they are saved, alongside the rest of the application's local state.
    /// </summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CandidateScreeningLoader",
        "preferences.json");

    /// <summary>
    /// Returns the folder last used for this job opening, if it still exists.
    /// </summary>
    public string? FolderFor(string openingId) =>
        Read().Folders.GetValueOrDefault(openingId) is { } folder && Directory.Exists(folder) ? folder : null;

    /// <summary>
    /// Remembers a job opening's folder, so it does not have to be looked up again on the next run.
    /// </summary>
    public void RememberFolder(string openingId, string folder)
    {
        Stored stored = Read();

        stored.Folders[openingId] = folder;

        Save(stored);
    }

    /// <summary>
    /// Returns the saved settings.
    /// </summary>
    public SettingsOverlay ReadSettings() => Read().Settings ?? SettingsOverlay.Empty;

    /// <summary>
    /// Saves the settings, keeping the rest of the preferences.
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
            // A preference that cannot be saved costs one more click, not the run.
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
