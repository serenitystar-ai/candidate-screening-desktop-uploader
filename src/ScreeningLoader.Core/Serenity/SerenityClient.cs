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
/// Los contratos del AI Hub que usa el motor, sobre HttpClient.
/// </summary>
public sealed class SerenityClient(
    HttpClient httpClient,
    TokenStore tokenStore,
    Func<ScreeningLoaderOptions> currentOptions,
    Func<string> resolvedAgentCode) : ISerenityClient
{
    private static readonly TimeSpan s_maxPollInterval = TimeSpan.FromSeconds(10);

    private const string InterfaceCulture = "es";

    /// <summary>Tope del endpoint. Se pide entero para no paginar por una lista de decenas.</summary>
    private const int NexusAgentPageSize = 1000;

    private const string ConfigurationSkillCode = "GetSerenityAppConfiguration";
    private const string DatasetSkillCodeProperty = "datasetSkillCode";

    /// <summary>Las opciones vigentes del motor, con los ajustes de la persona ya aplicados.</summary>
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

        // Un tenant con más agentes que la página se detecta acá y no dejando fuera al que buscábamos.
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

        // Una configuración ausente no puede degradar a vacío: sería indistinguible de un dataset sin filas.
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
            // El cuerpo se lee a medida que llega: con la respuesta completa no hay progreso que mostrar.
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
                "El turno de análisis no terminó dentro del tiempo de espera.",
                ex);
        }
    }

    /// <summary>
    /// Lee el progreso de un evento del stream, o null si no lo lleva.
    /// </summary>
    private static AgentProgress? ReadProgress(string name, JsonElement data)
    {
        // El progreso es decoración: un evento con una forma inesperada no puede tirar abajo un turno que
        // para entonces ya puede estar escribiendo filas.
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
    /// El texto final del turno, que es el recibo.
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

        // Un archivo trabado no cambia de estado nunca: el Hub mapea cualquier estado que no reconoce
        // a "analyzing", así que sin un tope propio este bucle no termina.
        using CancellationTokenSource attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
        attempt.CancelAfter(TimeSpan.FromMilliseconds(Options.UploadPollTimeoutMs));

        // Una sola vez: es un cambio de estado del archivo, no un latido. Emitirlo por vuelta llena el
        // host de líneas repetidas cuando un archivo tarda, que es justo cuando hay que poder leerlo.
        if (record.IsPending)
            emit(new FileProcessing(file.FileName));

        TimeSpan wait = TimeSpan.FromMilliseconds(Options.UploadPollIntervalMs);

        try
        {
            while (record.IsPending)
            {
                await Task.Delay(wait, attempt.Token);
                record = await GetVolatileKnowledgeAsync(record.Id, attempt.Token);

                // A un archivo que tarda no hace falta preguntarle cada segundo: son llamadas contra el
                // mismo límite de tasa que necesita el resto de la corrida.
                wait = wait * 2 < s_maxPollInterval ? wait * 2 : s_maxPollInterval;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // El motivo va sin el nombre del archivo: quien lo reporta ya lo lleva aparte.
            throw new ScreeningLoaderException(
                ErrorKind.File,
                "El AI Hub no terminó de procesarlo a tiempo.");
        }

        // Ejecutar contra un archivo aceptado pero sin procesar arma un candidato sobre un documento vacío.
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
    /// Traduce el estado de la respuesta a la taxonomía del motor.
    /// </summary>
    /// <param name="rejectionKind">
    /// Cómo tratar un rechazo del pedido. En una subida es el archivo el que no sirve, no la corrida.
    /// </param>
    private void EnsureSucceeded(HttpResponseMessage response, string operation, ErrorKind rejectionKind)
    {
        if (response.IsSuccessStatusCode)
            return;

        // El cuerpo de un fallo del dataset arrastra la sentencia, y con ella la fila del candidato.
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
    /// Cuánto pidió esperar el servidor, venga como segundos o como fecha.
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
