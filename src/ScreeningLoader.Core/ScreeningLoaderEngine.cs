using System.Net.Http.Headers;
using ScreeningLoader.Core.Audit;
using ScreeningLoader.Core.Auth;
using ScreeningLoader.Core.Discovery;
using ScreeningLoader.Core.Errors;
using ScreeningLoader.Core.Run;
using ScreeningLoader.Core.Screening;
using ScreeningLoader.Core.Serenity;

namespace ScreeningLoader.Core;

/// <summary>
/// Superficie del motor que consume un host.
/// </summary>
public sealed class ScreeningLoaderEngine : IDisposable
{
    private const string InterfaceCulture = "es";

    private const int MinFileSizeMb = 1;

    /// <summary>Tope del servidor web, que responde 413 cerca de los 30 MB.</summary>
    private const int MaxFileSizeMb = 25;

    private const int MinRetentionDays = 30;
    private const int MaxRetentionDays = 730;
    private const int MinBatchSize = 1;
    private const int MaxBatchSize = 20;
    private const int MinBatchMb = 1;
    private const int MaxBatchMb = 50;

    private const string ChatBaseUrl = "https://chat.serenitystar.ai";

    /// <summary>Versión de la micro app en la dirección donde se ven los candidatos.</summary>
    private const int AppVersion = 1;

    private const string FileSizeLabel = "El tamaño máximo por archivo";
    private const string RetentionLabel = "Los días que se guarda el historial";
    private const string BatchSizeLabel = "Los CV por lote";
    private const string BatchWeightLabel = "El peso máximo por lote";

    private readonly ScreeningLoaderOptions defaults;
    private readonly HttpClient httpClient;
    private readonly TokenStore tokenStore;
    private readonly ISerenityClient serenityClient;
    private readonly ErrorLog errorLog;
    private readonly TimeProvider timeProvider;
    private readonly RetryPolicy retryPolicy;
    private readonly RunHistory history = new(ErrorLog.DefaultDirectory);

    private ScreeningLoaderOptions options;
    private string? agentCode;
    private string? datasetSkillCode;
    private IScreeningClient? screeningClient;
    private IReadOnlyList<string>? acceptedMimeTypes;

    public ScreeningLoaderEngine(ScreeningLoaderOptions options, ErrorLog errorLog)
        : this(options, errorLog, TimeProvider.System)
    {
    }

    public ScreeningLoaderEngine(ScreeningLoaderOptions options, ErrorLog errorLog, TimeProvider timeProvider)
    {
        defaults = options;
        this.options = options;
        this.errorLog = errorLog;
        this.timeProvider = timeProvider;
        retryPolicy = new RetryPolicy(options, errorLog, timeProvider);

        httpClient = new HttpClient(new TransportFaultHandler(TimeSpan.FromMilliseconds(options.RequestTimeoutMs)))
        {
            // El tope por pedido lo pone el handler: el del HttpClient cortaría un turno por streaming.
            Timeout = Timeout.InfiniteTimeSpan,
            // Una BaseAddress sin barra final descarta su último segmento al combinar la ruta relativa.
            BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute)
        };

        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Los errores del Hub vienen localizados por el servidor; pedirlos en castellano evita traducirlos.
        httpClient.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(InterfaceCulture));

        tokenStore = new TokenStore(new AuthClient(httpClient), timeProvider);

        // Las opciones se leen por accesor y no por copia: el record es inmutable, así que resolver el
        // agente o guardar un ajuste deja al cliente apuntando a una instancia que ya no rige. El this.
        // es lo que hace que la lambda tome el campo y no el parámetro homónimo del constructor.
        serenityClient = new SerenityClient(httpClient, tokenStore, () => this.options, ResolvedAgentCode);
    }

    /// <summary>
    /// La configuración vigente, con los ajustes de la persona ya aplicados.
    /// </summary>
    public ScreeningLoaderOptions Options => options;

    /// <summary>
    /// El agente que sirve la app configurada, o null mientras la sesión no lo resolvió.
    /// </summary>
    public string? AgentCode => agentCode;

    /// <summary>
    /// Dónde ver los candidatos, en la micro app del agente resuelto.
    /// </summary>
    public string ResultsUrl =>
        $"{ChatBaseUrl}/chat/agent/{ResolvedAgentCode()}/app/{options.AppCode}/{AppVersion}";

    /// <summary>
    /// Aplica los ajustes locales sobre los valores por defecto, y rechaza los que no son representables.
    /// </summary>
    public void ApplySettings(SettingsOverlay settings)
    {
        string appCode = Trimmed(settings.AppCode) ?? defaults.AppCode;

        // Otra app es otro agente, así que se olvida el que estaba resuelto y todo lo que salió de él.
        if (!string.Equals(appCode, options.AppCode, StringComparison.OrdinalIgnoreCase))
            ForgetAgent();

        // Se parte siempre de los defaults y no de lo vigente: así vaciar un campo lo devuelve a su valor
        // original en vez de acumular capas.
        options = defaults with
        {
            AppCode = appCode,
            MaxFileSizeMb = InRange(settings.MaxFileSizeMb, MinFileSizeMb, MaxFileSizeMb, FileSizeLabel)
                ?? defaults.MaxFileSizeMb,
            ResponseLanguage = Trimmed(settings.ResponseLanguage) ?? defaults.ResponseLanguage,
            AuditLogRetentionDays =
                InRange(settings.AuditLogRetentionDays, MinRetentionDays, MaxRetentionDays, RetentionLabel)
                ?? defaults.AuditLogRetentionDays,
            BatchSize = InRange(settings.BatchSize, MinBatchSize, MaxBatchSize, BatchSizeLabel)
                ?? defaults.BatchSize,
            MaxBatchBytes = InRange(settings.MaxBatchMb, MinBatchMb, MaxBatchMb, BatchWeightLabel) is { } megabytes
                ? megabytes * 1024L * 1024L
                : defaults.MaxBatchBytes
        };
    }

    /// <summary>
    /// Encuentra el agente que publica la app configurada y opera contra él el resto de la sesión.
    /// </summary>
    /// <exception cref="AgentNotFoundException">
    /// Ninguno lo publica, o lo publican varios y no hay forma de elegir.
    /// </exception>
    public Task ResolveAgentAsync(CancellationToken ct) =>
        ObservedAsync("resolve-agent", async () =>
        {
            IReadOnlyList<NexusAgent> agents = await serenityClient.GetNexusAgentsAsync(ct);

            string[] serving =
            [
                .. agents
                    .Where(agent => string.Equals(agent.App, options.AppCode, StringComparison.OrdinalIgnoreCase))
                    .Select(agent => agent.Code)
            ];

            if (serving.Length == 0)
            {
                throw new AgentNotFoundException(
                    $"Ningún agente del AI Hub publica la app '{options.AppCode}'.");
            }

            if (serving.Length > 1)
            {
                throw new AgentNotFoundException(
                    $"{serving.Length} agentes publican la app '{options.AppCode}', así que no hay forma "
                    + "de saber contra cuál trabajar.");
            }

            agentCode = serving[0];
        });

    /// <summary>
    /// Devuelve el plan sin los archivos que la persona dejó fuera, con los lotes rearmados.
    /// </summary>
    public DiscoveryResult Exclude(DiscoveryResult discovery, IEnumerable<string> fileNames)
    {
        HashSet<string> excluded = [.. fileNames.Where(name => name.Length > 0)];

        if (excluded.Count == 0)
            return discovery;

        List<CvFile> kept = [.. discovery.Accepted.Where(file => !excluded.Contains(file.FileName))];

        // El agrupamiento depende de BatchSize y MaxBatchBytes, así que lo rearma el motor y no el host.
        return new DiscoveryResult(
            kept,
            [
                .. discovery.Rejected,
                .. discovery.Accepted
                    .Where(file => excluded.Contains(file.FileName))
                    .Select(file => new RejectedFile(file.FileName, RejectionReason.ExcludedByUser))
            ],
            new FileDiscovery(options).IntoBatches(kept),
            discovery.Failed);
    }

    /// <summary>
    /// Autentica al usuario contra el AI Hub.
    /// </summary>
    public Task LoginAsync(string email, string password, CancellationToken ct) =>
        ObservedAsync("login", () => tokenStore.LoginAsync(email, password, ct));

    /// <summary>
    /// Devuelve el código de la skill del dataset que publica el agente.
    /// </summary>
    public Task<string> GetDatasetSkillCodeAsync(CancellationToken ct) =>
        ObservedAsync("dataset-skill-code", () => ResolveDatasetSkillCodeAsync(ct));

    /// <summary>
    /// Devuelve las búsquedas laborales de la organización, cada una con su conteo de candidatos.
    /// </summary>
    public Task<IReadOnlyList<JobOpening>> ListJobOpeningsAsync(CancellationToken ct) =>
        ObservedAsync("list-job-openings", async () =>
        {
            IScreeningClient screening = await ResolveScreeningClientAsync(ct);

            return await screening.ListJobOpeningsAsync(ct);
        });

    /// <summary>
    /// Clasifica los archivos de una carpeta y arma los lotes que se procesarían.
    /// </summary>
    public Task<DiscoveryResult> DiscoverAsync(string folder, CancellationToken ct) =>
        ObservedAsync("discover", async () =>
        {
            // Los tipos aceptados los publica el agente: una copia local se desactualizaría en silencio.
            acceptedMimeTypes ??= await serenityClient.GetAcceptedMimeTypesAsync(ct);

            return new FileDiscovery(options).Discover(folder, acceptedMimeTypes);
        });

    /// <summary>
    /// Devuelve a la carpeta los archivos que estaban en la de fallidos, y la vuelve a leer.
    /// </summary>
    public Task<DiscoveryResult> RestoreFailedAsync(
        string folder,
        IEnumerable<string> fileNames,
        CancellationToken ct) =>
        ObservedAsync("restore-failed", async () =>
        {
            new FileDiscovery(options).Restore(folder, fileNames);

            return await DiscoverAsync(folder, ct);
        });

    /// <summary>
    /// Sube los archivos de un lote y devuelve los que quedaron listos para una ejecución.
    /// </summary>
    public Task<UploadOutcome> UploadBatchAsync(Batch batch, Action<RunEvent> emit, CancellationToken ct) =>
        ObservedAsync("upload-batch", async () => await (await ResolveRunnerAsync(ct)).UploadBatchAsync(batch, emit, ct));

    /// <summary>
    /// Procesa una carpeta entera, lote por lote, y emite el avance.
    /// </summary>
    /// <param name="discovery">
    /// Lo que el host ya clasificó y mostró, para que lo previsualizado y lo que corre sean lo mismo.
    /// </param>
    public async IAsyncEnumerable<RunEvent> RunAsync(
        JobOpening opening,
        DiscoveryResult discovery,
        string folder,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        BatchRunner runner = await ResolveRunnerAsync(ct);

        AuditLog audit = new(
            ErrorLog.DefaultDirectory,
            opening.Id,
            opening.Title,
            timeProvider,
            options.AuditLogRetentionDays);

        await foreach (RunEvent progress in runner.RunAsync(opening, discovery, folder, audit, ct))
            yield return progress;
    }

    /// <summary>
    /// Renueva el access token de la sesión sin esperar a que esté por vencer.
    /// </summary>
    public Task RefreshSessionAsync(CancellationToken ct) =>
        ObservedAsync("refresh", () => tokenStore.ForceRefreshAsync(ct));

    /// <summary>
    /// Cierra la sesión y olvida lo que se resolvió con ella.
    /// </summary>
    public Task LogoutAsync(CancellationToken ct) =>
        ObservedAsync("logout", async () =>
        {
            await tokenStore.CloseAsync(ct);

            // La configuración del agente se resuelve una vez por sesión, y la próxima puede ser de otro
            // tenant.
            ForgetAgent();
        });

    /// <summary>
    /// Devuelve las corridas registradas, de la más reciente a la más vieja.
    /// </summary>
    public IReadOnlyList<RunRecord> ListRuns(int limit) => history.List(limit);

    /// <summary>
    /// Devuelve una corrida anterior por su identificador, o null si su registro ya no está.
    /// </summary>
    public RunRecord? FindRun(string runId) => history.Find(runId);

    public void Dispose() => httpClient.Dispose();

    /// <summary>
    /// El agente contra el que opera la sesión, o un fatal si el acceso todavía no lo descubrió.
    /// </summary>
    private string ResolvedAgentCode() =>
        agentCode ?? throw new ScreeningLoaderException(
            ErrorKind.Fatal,
            "Todavía no se resolvió el agente que sirve la app.");

    /// <summary>
    /// Olvida el agente y todo lo que se resolvió a través de él.
    /// </summary>
    private void ForgetAgent()
    {
        agentCode = null;
        datasetSkillCode = null;
        screeningClient = null;
        acceptedMimeTypes = null;
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? InRange(int? value, int minimum, int maximum, string label)
    {
        if (value is not { } number)
            return null;

        return number >= minimum && number <= maximum
            ? number
            : throw new ScreeningLoaderException(
                ErrorKind.Fatal,
                $"{label} tiene que estar entre {minimum} y {maximum}.");
    }

    /// <summary>
    /// El código lo publica el agente y no se configura, así que se resuelve una vez por sesión.
    /// </summary>
    private async Task<string> ResolveDatasetSkillCodeAsync(CancellationToken ct) =>
        datasetSkillCode ??= await serenityClient.GetDatasetSkillCodeAsync(ct);

    private async Task<BatchRunner> ResolveRunnerAsync(CancellationToken ct) =>
        new(serenityClient, await ResolveScreeningClientAsync(ct), tokenStore, retryPolicy, options);

    private async Task<IScreeningClient> ResolveScreeningClientAsync(CancellationToken ct) =>
        screeningClient ??= new ScreeningClient(serenityClient, await ResolveDatasetSkillCodeAsync(ct));

    private async Task ObservedAsync(string operation, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            errorLog.Write(operation, ex);
            throw;
        }
    }

    private async Task<T> ObservedAsync<T>(string operation, Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            errorLog.Write(operation, ex);
            throw;
        }
    }
}
