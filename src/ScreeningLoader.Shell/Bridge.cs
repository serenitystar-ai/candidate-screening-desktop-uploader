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
/// Translates WebView messages into engine operations, and their results back.
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

    /// <summary>An analysis is in progress, so closing the window is not harmless.</summary>
    public bool IsRunning => runCancellation is not null;

    public void Attach()
    {
        try
        {
            engine.ApplySettings(preferences.ReadSettings());
        }
        catch (ScreeningLoaderException ex)
        {
            // A saved setting that is no longer valid (the file was edited by hand) must not prevent opening.
            errorLog.Write("settings", ex);
        }

        webView.WebMessageReceived += OnMessageReceived;
    }

    /// <summary>
    /// Tells the interface that closing the window was attempted, so it confirms in its own voice.
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
            // What the engine did not classify has no policy, so it aborts the operation in progress.
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

        // The agent comes from the configured app, not from a constant, so the application works in any
        // tenant without recompiling.
        await engine.ResolveAgentAsync(ct);

        // Resolving the skill here is what makes a missing permission on the agent show up at sign-in
        // rather than only when the first analysis is launched.
        await engine.GetDatasetSkillCodeAsync(ct);

        return null;
    }

    private async Task<object?> LogoutAsync(CancellationToken ct)
    {
        runCancellation?.Cancel();

        await engine.LogoutAsync(ct);

        // The cache holds what this session saw; the next one may belong to someone else.
        openings = [];
        discovery = null;
        discoveryFolder = string.Empty;

        return null;
    }

    /// <summary>
    /// The current settings: the engine defaults with whatever the person changed on top.
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

        // The engine validates and throws on anything unrepresentable, so only what it applied is saved.
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
            // An app that resolves to no agent is not saved: it is checked before being written,
            // and if it is not valid the session stays as it was.
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
        // The engine builds it from the resolved agent, and it is https by construction: that is what makes
        // it safe to launch with the system browser.
        Process.Start(new ProcessStartInfo(engine.ResultsUrl) { UseShellExecute = true })?.Dispose();

        return Task.FromResult<object?>(null);
    }

    private Task<object?> CloseWindow()
    {
        forceClose();

        return Task.FromResult<object?>(null);
    }

    /// <summary>
    /// Runs the analysis and pushes its progress to the interface.
    /// </summary>
    private async Task PumpAsync(
        JobOpening opening,
        string folder,
        IReadOnlyList<string> excluded,
        CancellationTokenSource cancellation)
    {
        try
        {
            // What was previewed and what runs must be the same; the folder is only re-read if it changed.
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
        // The analysis runs off the window thread, and the WebView may only be touched from there.
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
            // The window is closing: there is no one left to notify.
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
