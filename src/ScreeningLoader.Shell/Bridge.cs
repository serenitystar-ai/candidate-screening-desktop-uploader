using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Web.WebView2.Core;
using ScreeningLoader.Core;
using ScreeningLoader.Core.Discovery;
using ScreeningLoader.Core.Errors;
using ScreeningLoader.Core.Run;
using ScreeningLoader.Core.Screening;

namespace ScreeningLoader.Shell;

/// <summary>
/// Traduce los mensajes del WebView a operaciones del motor y sus respuestas de vuelta.
/// </summary>
internal sealed class Bridge(
    Control owner,
    CoreWebView2 webView,
    ScreeningLoaderEngine engine,
    ErrorLog errorLog,
    Action forceClose)
{
    private const int HistoryLimit = 30;

    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly Preferences preferences = new(Preferences.DefaultPath);

    private IReadOnlyList<JobOpening> openings = [];
    private DiscoveryResult? discovery;
    private string discoveryFolder = string.Empty;
    private CancellationTokenSource? runCancellation;

    /// <summary>Hay un análisis en marcha, así que cerrar la ventana no es inocuo.</summary>
    public bool IsRunning => runCancellation is not null;

    public void Attach()
    {
        try
        {
            engine.ApplySettings(preferences.ReadSettings());
        }
        catch (ScreeningLoaderException ex)
        {
            // Un ajuste guardado que ya no vale —el archivo se editó a mano— no puede impedir abrir.
            errorLog.Write("settings", ex);
        }

        webView.WebMessageReceived += OnMessageReceived;
    }

    /// <summary>
    /// Avisa a la interfaz de que se intentó cerrar la ventana, para que confirme con su propia voz.
    /// </summary>
    public void RequestClose() => PostEvent("closeRequested", new { });

    private async void OnMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        BridgeCommand? command;

        try
        {
            command = JsonSerializer.Deserialize<BridgeCommand>(e.WebMessageAsJson, s_json);
        }
        catch (JsonException ex)
        {
            errorLog.Write("bridge-message", ex);
            return;
        }

        if (command is null)
            return;

        try
        {
            Post(new BridgeReply(command.Id, await DispatchAsync(command, CancellationToken.None), Error: null));
        }
        catch (ScreeningLoaderException ex)
        {
            Post(new BridgeReply(command.Id, Result: null, BridgeError.From(ex)));
        }
        catch (Exception ex)
        {
            // Lo que no clasificó el motor no tiene política, así que corta la operación en curso.
            errorLog.Write($"bridge-{command.Type}", ex);
            Post(new BridgeReply(command.Id, Result: null, new BridgeError(ErrorKind.Fatal, ex.Message)));
        }
    }

    private Task<object?> DispatchAsync(BridgeCommand command, CancellationToken ct) => command.Type switch
    {
        "login" => LoginAsync(command.Payload, ct),
        "logout" => LogoutAsync(ct),
        "readSettings" => ReadSettings(),
        "saveSettings" => SaveSettingsAsync(command.Payload, ct),
        "listOpenings" => ListOpeningsAsync(ct),
        "rememberedFolder" => RememberedFolder(command.Payload),
        "pickFolder" => PickFolder(command.Payload),
        "discover" => DiscoverAsync(command.Payload, ct),
        "restoreFailed" => RestoreFailedAsync(command.Payload, ct),
        "startRun" => StartRun(command.Payload),
        "cancelRun" => CancelRun(),
        "listRuns" => ListRuns(),
        "openResults" => OpenResults(),
        "closeWindow" => CloseWindow(),
        _ => throw new ScreeningLoaderException(
            ErrorKind.Fatal,
            $"La ventana pidió una operación que el motor no expone: {command.Type}.")
    };

    private async Task<object?> LoginAsync(JsonElement payload, CancellationToken ct)
    {
        Credentials credentials = payload.Deserialize<Credentials>(s_json)
            ?? throw new ScreeningLoaderException(ErrorKind.Fatal, "El acceso llegó sin credenciales.");

        await engine.LoginAsync(credentials.Email, credentials.Password, ct);

        // El agente sale de la app configurada, no de una constante: así la aplicación sirve en cualquier
        // tenant sin recompilar.
        await engine.ResolveAgentAsync(ct);

        // Resolver la skill acá es lo que hace que la falta de permiso sobre el agente se vea al entrar
        // y no recién al lanzar el primer análisis.
        await engine.GetDatasetSkillCodeAsync(ct);

        return null;
    }

    private async Task<object?> LogoutAsync(CancellationToken ct)
    {
        runCancellation?.Cancel();

        await engine.LogoutAsync(ct);

        // Lo cacheado es lo que vio esta sesión; la próxima puede ser de otra persona.
        openings = [];
        discovery = null;
        discoveryFolder = string.Empty;

        return null;
    }

    /// <summary>
    /// Los ajustes vigentes, que son los defaults del motor con lo que la persona haya cambiado encima.
    /// </summary>
    private Task<object?> ReadSettings()
    {
        ScreeningLoaderOptions current = engine.Options;

        return Task.FromResult<object?>(
            new SettingsWire(
                current.AppCode,
                current.MaxFileSizeMb,
                current.ResponseLanguage,
                current.AuditLogRetentionDays,
                current.BatchSize,
                (int)(current.MaxBatchBytes / (1024 * 1024))));
    }

    private async Task<object?> RestoreFailedAsync(JsonElement payload, CancellationToken ct)
    {
        string folder = Folder(payload);

        discovery = await engine.RestoreFailedAsync(folder, Names(payload, "fileNames"), ct);
        discoveryFolder = folder;

        return DiscoveryWire.From(discovery);
    }

    private async Task<object?> SaveSettingsAsync(JsonElement payload, CancellationToken ct)
    {
        SettingsWire wire = payload.Deserialize<SettingsWire>(s_json)
            ?? throw new ScreeningLoaderException(ErrorKind.Fatal, "Los ajustes llegaron vacíos.");

        // El motor valida y lanza si algo no es representable, así que se guarda sólo lo que ya aplicó.
        SettingsOverlay overlay = new(
            wire.AppCode,
            wire.MaxFileSizeMb,
            wire.ResponseLanguage,
            wire.AuditLogRetentionDays,
            wire.BatchSize,
            wire.MaxBatchMb);

        bool appChanged = !string.Equals(wire.AppCode, engine.Options.AppCode, StringComparison.OrdinalIgnoreCase);
        SettingsOverlay previous = preferences.ReadSettings();

        engine.ApplySettings(overlay);

        try
        {
            // Una app que no resuelve a ningún agente no se guarda: se comprueba antes de dejarla escrita,
            // y si no vale la sesión se queda como estaba.
            if (appChanged)
            {
                await engine.ResolveAgentAsync(ct);
                await engine.GetDatasetSkillCodeAsync(ct);

                openings = [];
                discovery = null;
                discoveryFolder = string.Empty;
            }

            preferences.SaveSettings(overlay);
        }
        catch
        {
            engine.ApplySettings(previous);
            throw;
        }

        return null;
    }

    private async Task<object?> ListOpeningsAsync(CancellationToken ct)
    {
        openings = await engine.ListJobOpeningsAsync(ct);

        return openings.Select(OpeningWire.From).ToArray();
    }

    private Task<object?> RememberedFolder(JsonElement payload) =>
        Task.FromResult<object?>(preferences.FolderFor(Opening(payload).Id));

    private Task<object?> PickFolder(JsonElement payload)
    {
        using FolderBrowserDialog dialog = new()
        {
            UseDescriptionForTitle = true,
            Description = "Elige la carpeta con los CV",
            SelectedPath = preferences.FolderFor(Opening(payload).Id) ?? string.Empty
        };

        return Task.FromResult<object?>(dialog.ShowDialog(owner) is DialogResult.OK ? dialog.SelectedPath : null);
    }

    private async Task<object?> DiscoverAsync(JsonElement payload, CancellationToken ct)
    {
        string folder = Folder(payload);

        discovery = await engine.DiscoverAsync(folder, ct);
        discoveryFolder = folder;

        return DiscoveryWire.From(discovery);
    }

    private Task<object?> StartRun(JsonElement payload)
    {
        if (IsRunning)
            throw new ScreeningLoaderException(ErrorKind.Fatal, "Ya hay un análisis en marcha.");

        JobOpening opening = Opening(payload);
        string folder = Folder(payload);

        preferences.RememberFolder(opening.Id, folder);

        CancellationTokenSource cancellation = new();
        runCancellation = cancellation;

        _ = Task.Run(() => PumpAsync(opening, folder, Excluded(payload), cancellation));

        return Task.FromResult<object?>(null);
    }

    private Task<object?> CancelRun()
    {
        runCancellation?.Cancel();

        return Task.FromResult<object?>(null);
    }

    private Task<object?> ListRuns()
    {
        Dictionary<string, string> titles = openings.ToDictionary(
            opening => opening.Id,
            opening => opening.Title,
            StringComparer.OrdinalIgnoreCase);

        return Task.FromResult<object?>(
            engine.ListRuns(HistoryLimit)
                .Select(run => RunWire.From(run, titles.GetValueOrDefault(run.OpeningId, "Oferta retirada")))
                .ToArray());
    }

    private Task<object?> OpenResults()
    {
        // La arma el motor con el agente resuelto, y es https por construcción: eso es lo que hace
        // seguro lanzarla con el navegador del sistema.
        Process.Start(new ProcessStartInfo(engine.ResultsUrl) { UseShellExecute = true })?.Dispose();

        return Task.FromResult<object?>(null);
    }

    private Task<object?> CloseWindow()
    {
        forceClose();

        return Task.FromResult<object?>(null);
    }

    /// <summary>
    /// Corre el análisis y empuja su avance a la interfaz.
    /// </summary>
    private async Task PumpAsync(
        JobOpening opening,
        string folder,
        IReadOnlyList<string> excluded,
        CancellationTokenSource cancellation)
    {
        try
        {
            // Lo previsualizado y lo que corre tienen que ser lo mismo; sólo se relee si la carpeta cambió.
            DiscoveryResult scanned =
                discovery is { } cached
                && string.Equals(discoveryFolder, folder, StringComparison.OrdinalIgnoreCase)
                    ? cached
                    : await engine.DiscoverAsync(folder, cancellation.Token);

            DiscoveryResult planned = engine.Exclude(scanned, excluded);
            int byUser = planned.Rejected.Count - scanned.Rejected.Count;

            await foreach (RunEvent progress in engine.RunAsync(opening, planned, folder, cancellation.Token))
            {
                if (RunEventWire.From(progress, byUser) is { } wire)
                    PostEvent("run", wire);
            }
        }
        catch (OperationCanceledException)
        {
            PostEvent("run", RunEventWire.Cancelled());
        }
        catch (ScreeningLoaderException ex)
        {
            PostEvent("run", RunEventWire.Failed(ex.Kind, ex.Message));
        }
        catch (Exception ex)
        {
            errorLog.Write("run", ex);
            PostEvent("run", RunEventWire.Failed(ErrorKind.Fatal, ex.Message));
        }
        finally
        {
            runCancellation = null;
            cancellation.Dispose();
        }
    }

    private JobOpening Opening(JsonElement payload)
    {
        string id = Text(payload, "openingId");

        return openings.FirstOrDefault(opening => opening.Id == id)
            ?? throw new ScreeningLoaderException(ErrorKind.Fatal, "La oferta ya no está en la lista.");
    }

    private static string Folder(JsonElement payload) => Text(payload, "folder");

    private static IReadOnlyList<string> Excluded(JsonElement payload) => Names(payload, "excluded");

    private static IReadOnlyList<string> Names(JsonElement payload, string property)
    {
        if (payload.ValueKind is not JsonValueKind.Object
            || !payload.TryGetProperty(property, out JsonElement value)
            || value.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        return
        [
            .. value.EnumerateArray()
                .Where(name => name.ValueKind is JsonValueKind.String)
                .Select(name => name.GetString()!)
        ];
    }

    private static string Text(JsonElement payload, string property) =>
        payload.ValueKind is JsonValueKind.Object
        && payload.TryGetProperty(property, out JsonElement value)
        && value.ValueKind is JsonValueKind.String
            ? value.GetString()!
            : throw new ScreeningLoaderException(ErrorKind.Fatal, $"El mensaje llegó sin {property}.");

    private void PostEvent(string name, object payload) => Post(new BridgeEvent(name, payload));

    private void Post(object message)
    {
        // El análisis corre fuera del hilo de la ventana, y el WebView sólo se toca desde ahí.
        if (owner.InvokeRequired)
            owner.BeginInvoke(() => Send(message));
        else
            Send(message);
    }

    private void Send(object message)
    {
        try
        {
            webView.PostWebMessageAsJson(JsonSerializer.Serialize(message, s_json));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // La ventana se está cerrando: ya no hay a quién avisarle.
        }
    }

    private sealed record BridgeCommand(int Id, string Type, JsonElement Payload);

    private sealed record BridgeReply(int Id, object? Result, BridgeError? Error);

    private sealed record BridgeEvent(string Event, object Payload);

    private sealed record BridgeError(ErrorKind Kind, string Message, string? Code = null)
    {
        private const string AgentNotFound = "agentNotFound";

        public static BridgeError From(ScreeningLoaderException ex) =>
            new(ex.Kind, ex.Message, ex is AgentNotFoundException ? AgentNotFound : null);
    }

    private sealed record Credentials(string Email, string Password);
}
