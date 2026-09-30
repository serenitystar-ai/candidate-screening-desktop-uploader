using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ScreeningLoader.Core.Auth;
using ScreeningLoader.Core.Discovery;
using ScreeningLoader.Core.Errors;
using ScreeningLoader.Core.Run;
using ScreeningLoader.Core.Screening;

namespace ScreeningLoader.Core.Serenity;

/// <summary>
/// The AI Hub contracts the engine uses, over HttpClient.
/// </summary>
public sealed class SerenityClient(
    HttpClient httpClient,
    TokenStore tokenStore,
    Func<ScreeningLoaderOptions> currentOptions,
    Func<string> resolvedAgentCode) : ISerenityClient
{
    private static readonly TimeSpan s_maxPollInterval = TimeSpan.FromSeconds(10);

    private const string InterfaceCulture = "es";

    /// <summary>The endpoint's cap. Requested in full to avoid paginating a list of dozens.</summary>
    private const int NexusAgentPageSize = 1000;

    private const string ConfigurationSkillCode = "GetSerenityAppConfiguration";
    private const string DatasetSkillCodeProperty = "datasetSkillCode";

    /// <summary>The engine's current options, with the person's settings already applied.</summary>
    private ScreeningLoaderOptions Options => currentOptions();

    public async Task<JsonElement> ExecuteSkillAsync(string skillCode, object? body, CancellationToken ct)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, Routes.Skill(resolvedAgentCode(), skillCode));

        if (body is not null)
            request.Content = JsonContent.Create(body);

        using HttpResponseMessage response = await SendAuthenticatedAsync(request, skillCode, ct);

        SkillExecutionRes? result = await response.Content.ReadFromJsonAsync<SkillExecutionRes>(ct);

        if (result?.PendingActions is { ValueKind: JsonValueKind.Array } pending && pending.GetArrayLength() > 0)
        {
            throw new ScreeningLoaderException(
                ErrorKind.Fatal,
                $"La skill '{skillCode}' no llegó a ejecutarse: el AI Hub pide completar acciones pendientes.");
        }

        return result?.JsonContent ?? default;
    }

    public async Task<IReadOnlyList<NexusAgent>> GetNexusAgentsAsync(CancellationToken ct)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, Routes.NexusAgents(NexusAgentPageSize));

        using HttpResponseMessage response = await SendAuthenticatedAsync(request, "nexus-agents", ct);

        NexusAgentPage? page = await response.Content.ReadFromJsonAsync<NexusAgentPage>(ct);

        if (page?.Items is null)
            throw new ScreeningLoaderException(ErrorKind.Fatal, "El AI Hub no devolvió la lista de agentes.");

        // A tenant with more agents than the page is detected here rather than by leaving out the one we were looking for.
        if (page.Total > page.Items.Count)
        {
            throw new ScreeningLoaderException(
                ErrorKind.Fatal,
                $"El AI Hub tiene {page.Total} agentes y sólo se leyeron {page.Items.Count}.");
        }

        return [.. page.Items.Select(item => new NexusAgent(item.Code, item.Name, item.Channel?.App))];
    }

    public async Task<string> GetDatasetSkillCodeAsync(CancellationToken ct)
    {
        JsonElement configuration = await ExecuteSkillAsync(ConfigurationSkillCode, body: null, ct);

        // A missing configuration cannot degrade to empty: it would be indistinguishable from a dataset with no rows.
        if (configuration.ValueKind is not JsonValueKind.Object
            || !configuration.TryGetProperty(DatasetSkillCodeProperty, out JsonElement code)
            || code.ValueKind is not JsonValueKind.String
            || code.GetString() is not { Length: > 0 } value)
        {
            throw new ScreeningLoaderException(
                ErrorKind.Fatal,
                $"El agente no publicó {DatasetSkillCodeProperty} en {ConfigurationSkillCode}.");
        }

        return value;
    }

    public async Task<IReadOnlyList<string>> GetAcceptedMimeTypesAsync(CancellationToken ct)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, Routes.MimeTypes(resolvedAgentCode()));

        using HttpResponseMessage response = await SendAuthenticatedAsync(request, "mimeTypes", ct);

        IReadOnlyList<string>? mimeTypes = await response.Content.ReadFromJsonAsync<IReadOnlyList<string>>(ct);

        return mimeTypes is { Count: > 0 }
            ? mimeTypes
            : throw new ScreeningLoaderException(
                ErrorKind.Fatal,
                "El agente no publicó ningún tipo de archivo aceptado.");
    }

    public async Task<AnalyzeReceipt> AnalyzeAsync(
        JobOpening opening,
        IReadOnlyList<UploadedCv> cvs,
        Action<RunEvent> emit,
        CancellationToken ct)
    {
        using HttpRequestMessage request = new(
            HttpMethod.Post,
            Routes.Execute(resolvedAgentCode(), InterfaceCulture))
        {
            Content = JsonContent.Create(AnalyzeDirective.Build(opening, cvs, Options.ResponseLanguage))
        };

        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Options.Set(RequestOptions.LongRunning, true);

        using CancellationTokenSource turn = CancellationTokenSource.CreateLinkedTokenSource(ct);
        turn.CancelAfter(TimeSpan.FromMilliseconds(Options.AnalyzeTimeoutMs));

        try
        {
            // The body is read as it arrives: with the full response there is no progress to show.
            using HttpResponseMessage response = await SendAuthenticatedAsync(
                request,
                "analyze-candidates",
                turn.Token,
                completion: HttpCompletionOption.ResponseHeadersRead);

            await using Stream stream = await response.Content.ReadAsStreamAsync(turn.Token);

            JsonElement result = await SseReader.ReadUntilStopAsync(
                stream,
                (name, data) =>
                {
                    if (ReadProgress(name, data) is { } progress)
                        emit(progress);
                },
                turn.Token);

            return AnalyzeReceipt.Parse(ReadTurnContent(result));
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ScreeningLoaderException(
                ErrorKind.Transient,
                "The analysis turn did not finish within the timeout.",
                ex);
        }
    }

    /// <summary>
    /// Reads the progress from a stream event, or null if it carries none.
    /// </summary>
    private static AgentProgress? ReadProgress(string name, JsonElement data)
    {
        // Progress is decoration: an event with an unexpected shape cannot bring down a turn that
        // by then may already be writing rows.
        try
        {
            return AgentProgress.Parse(name, data);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// The turn's final text, which is the receipt.
    /// </summary>
    private static string ReadTurnContent(JsonElement result) =>
        result.ValueKind is JsonValueKind.Object
        && result.TryGetProperty("content", out JsonElement content)
        && content.ValueKind is JsonValueKind.String
            ? content.GetString()!
            : string.Empty;

    public async Task<VolatileKnowledgeRecord> UploadAndAwaitAsync(
        CvFile file,
        Action<RunEvent> emit,
        CancellationToken ct)
    {
        emit(new FileUploading(file.FileName, file.SizeBytes));

        VolatileKnowledgeRecord record = await UploadAsync(file, ct);

        // A stuck file never changes status: the Hub maps any status it does not recognize
        // to "analyzing", so without a cap of our own this loop never ends.
        using CancellationTokenSource attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
        attempt.CancelAfter(TimeSpan.FromMilliseconds(Options.UploadPollTimeoutMs));

        // Only once: it is a change in the file's status, not a heartbeat. Emitting it on every pass floods the
        // host with repeated lines when a file is slow, which is exactly when it needs to be readable.
        if (record.IsPending)
            emit(new FileProcessing(file.FileName));

        TimeSpan wait = TimeSpan.FromMilliseconds(Options.UploadPollIntervalMs);

        try
        {
            while (record.IsPending)
            {
                await Task.Delay(wait, attempt.Token);
                record = await GetVolatileKnowledgeAsync(record.Id, attempt.Token);

                // A slow file does not need to be asked every second: those are calls against the
                // same rate limit the rest of the run needs.
                wait = wait * 2 < s_maxPollInterval ? wait * 2 : s_maxPollInterval;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The reason goes without the file name: whoever reports it already carries it separately.
            throw new ScreeningLoaderException(
                ErrorKind.File,
                "El AI Hub no terminó de procesarlo a tiempo.");
        }

        // Executing against a file that was accepted but not processed builds a candidate on an empty document.
        if (!record.IsReady)
            throw new ScreeningLoaderException(ErrorKind.File, $"El AI Hub lo dejó en '{record.Status}'.");

        emit(new FileUploaded(file.FileName));

        return record;
    }

    public async Task<VolatileKnowledgeRecord> UploadAsync(CvFile file, CancellationToken ct)
    {
        using MultipartFormDataContent form = [];
        await using FileStream stream = File.OpenRead(file.Path);
        using StreamContent part = new(stream);

        part.Headers.ContentType = new MediaTypeHeaderValue(FileTypes.ContentTypeFor(file.FileName));
        form.Add(part, "File", file.FileName);

        using HttpRequestMessage request = new(
            HttpMethod.Post,
            Routes.AgentVolatileKnowledge(resolvedAgentCode(), Options.ProcessEmbeddings))
        {
            Content = form
        };

        using HttpResponseMessage response = await SendAuthenticatedAsync(request, "volatileKnowledge", ct, ErrorKind.File);

        return await ReadRecordAsync(response, ct);
    }

    public async Task<VolatileKnowledgeRecord> GetVolatileKnowledgeAsync(Guid id, CancellationToken ct)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, Routes.VolatileKnowledge(id));

        using HttpResponseMessage response = await SendAuthenticatedAsync(request, "volatileKnowledge", ct);

        return await ReadRecordAsync(response, ct);
    }

    private static async Task<VolatileKnowledgeRecord> ReadRecordAsync(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        VolatileKnowledgeRecord? record = await response.Content.ReadFromJsonAsync<VolatileKnowledgeRecord>(ct);

        return record ?? throw new ScreeningLoaderException(
            ErrorKind.Transient,
            "El AI Hub no devolvió el estado de la subida.");
    }

    private async Task<HttpResponseMessage> SendAuthenticatedAsync(
        HttpRequestMessage request,
        string operation,
        CancellationToken ct,
        ErrorKind rejectionKind = ErrorKind.Fatal,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await tokenStore.GetAccessTokenAsync(ct));

        HttpResponseMessage response = await httpClient.SendAsync(request, completion, ct);

        try
        {
            EnsureSucceeded(response, operation, rejectionKind);
        }
        catch
        {
            response.Dispose();
            throw;
        }

        return response;
    }

    /// <summary>
    /// Translates the response status to the engine's taxonomy.
    /// </summary>
    /// <param name="rejectionKind">
    /// How to treat a rejection of the request. In an upload it is the file that is unusable, not the run.
    /// </param>
    private void EnsureSucceeded(HttpResponseMessage response, string operation, ErrorKind rejectionKind)
    {
        if (response.IsSuccessStatusCode)
            return;

        // The body of a dataset failure carries the statement, and with it the candidate's row.
        (ErrorKind kind, string message) = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized =>
                (ErrorKind.Fatal, "La sesión no es válida para el AI Hub."),
            HttpStatusCode.Forbidden =>
                (ErrorKind.Fatal,
                    $"La cuenta no tiene permiso de Execution sobre el agente que sirve '{Options.AppCode}', "
                    + "o el agente tiene dominios permitidos configurados."),
            HttpStatusCode.NotFound =>
                (ErrorKind.Fatal, $"El AI Hub no encontró '{operation}' en la versión publicada del agente."),
            HttpStatusCode.TooManyRequests =>
                (ErrorKind.Transient, "El AI Hub está limitando las llamadas."),
            HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.UnsupportedMediaType =>
                (rejectionKind, $"El AI Hub rechazó el pedido de '{operation}' con {(int)response.StatusCode}."),
            _ when (int)response.StatusCode >= 500 =>
                (ErrorKind.Transient, $"El AI Hub respondió {(int)response.StatusCode}."),
            _ =>
                (ErrorKind.Fatal, $"El AI Hub respondió {(int)response.StatusCode} al ejecutar '{operation}'.")
        };

        throw new ScreeningLoaderException(kind, message)
        {
            StatusCode = response.StatusCode,
            RetryAfter = ReadRetryAfter(response)
        };
    }

    /// <summary>
    /// How long the server asked to wait, whether it comes as seconds or as a date.
    /// </summary>
    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        RetryConditionHeaderValue? header = response.Headers.RetryAfter;

        if (header?.Delta is { } delta)
            return delta;

        return header?.Date is { } date && date > DateTimeOffset.UtcNow
            ? date - DateTimeOffset.UtcNow
            : null;
    }

    private sealed record SkillExecutionRes(JsonElement? JsonContent, JsonElement? PendingActions);

    private sealed record NexusAgentPage(int Total, IReadOnlyList<NexusAgentRes>? Items);

    private sealed record NexusAgentRes(string Code, string Name, NexusChannelRes? Channel);

    private sealed record NexusChannelRes(string? App, int? AppVersion);
}
