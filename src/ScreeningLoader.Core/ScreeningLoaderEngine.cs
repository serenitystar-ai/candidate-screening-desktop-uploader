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
/// The engine surface a host consumes.
/// </summary>
public sealed class ScreeningLoaderEngine : IDisposable
{
    private const string InterfaceCulture = "es";

    private const int MinFileSizeMb = 1;

    /// <summary>The web server's cap, which responds 413 at around 30 MB.</summary>
    private const int MaxFileSizeMb = 25;

    private const int MinRetentionDays = 30;
    private const int MaxRetentionDays = 730;
    private const int MinBatchSize = 1;
    private const int MaxBatchSize = 20;
    private const int MinBatchMb = 1;
    private const int MaxBatchMb = 50;

    private const string ChatBaseUrl = "https://chat.serenitystar.ai";

    /// <summary>The micro app version in the address where the candidates are shown.</summary>
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
            // The handler sets the per-request cap: the HttpClient's own would cut off a streaming turn.
            Timeout = Timeout.InfiniteTimeSpan,
            // A BaseAddress without a trailing slash drops its last segment when combined with the relative route.
            BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute)
        };

        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // The Hub's errors come localized by the server; asking for them in Spanish avoids translating them.
        httpClient.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(InterfaceCulture));

        tokenStore = new TokenStore(new AuthClient(httpClient), timeProvider);

        // The options are read through an accessor, not copied: the record is immutable, so resolving the
        // agent or saving a setting would leave the client pointing at an instance that no longer applies. The this.
        // is what makes the lambda capture the field and not the constructor parameter of the same name.
        serenityClient = new SerenityClient(httpClient, tokenStore, () => this.options, ResolvedAgentCode);
    }

    /// <summary>
    /// The current configuration, with the person's settings already applied.
    /// </summary>
    public ScreeningLoaderOptions Options => options;

    /// <summary>
    /// The agent that serves the configured app, or null while the session has not resolved it.
    /// </summary>
    public string? AgentCode => agentCode;

    /// <summary>
    /// Where to see the candidates, in the resolved agent's micro app.
    /// </summary>
    public string ResultsUrl =>
        $"{ChatBaseUrl}/chat/agent/{ResolvedAgentCode()}/app/{options.AppCode}/{AppVersion}";

    /// <summary>
    /// Applies the local settings over the defaults, and rejects the ones that cannot be represented.
    /// </summary>
    public void ApplySettings(SettingsOverlay settings)
    {
        string appCode = Trimmed(settings.AppCode) ?? defaults.AppCode;

        // Another app is another agent, so the resolved one is forgotten along with everything derived from it.
        if (!string.Equals(appCode, options.AppCode, StringComparison.OrdinalIgnoreCase))
            ForgetAgent();

        // Always start from the defaults and not from the current values: that way clearing a field returns it to its
        // original value instead of stacking layers.
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
    /// Finds the agent that publishes the configured app and works against it for the rest of the session.
    /// </summary>
    /// <exception cref="AgentNotFoundException">
    /// None publishes it, or several do and there is no way to choose.
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
    /// Returns the plan without the files the person left out, with the batches rebuilt.
    /// </summary>
    public DiscoveryResult Exclude(DiscoveryResult discovery, IEnumerable<string> fileNames)
    {
        HashSet<string> excluded = [.. fileNames.Where(name => name.Length > 0)];

        if (excluded.Count == 0)
            return discovery;

        List<CvFile> kept = [.. discovery.Accepted.Where(file => !excluded.Contains(file.FileName))];

        // The grouping depends on BatchSize and MaxBatchBytes, so the engine rebuilds it and not the host.
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
    /// Authenticates the user against the AI Hub.
    /// </summary>
    public Task LoginAsync(string email, string password, CancellationToken ct) =>
        ObservedAsync("login", () => tokenStore.LoginAsync(email, password, ct));

    /// <summary>
    /// Returns the code of the dataset skill the agent publishes.
    /// </summary>
    public Task<string> GetDatasetSkillCodeAsync(CancellationToken ct) =>
        ObservedAsync("dataset-skill-code", () => ResolveDatasetSkillCodeAsync(ct));

    /// <summary>
    /// Returns the organization's job openings, each with its candidate count.
    /// </summary>
    public Task<IReadOnlyList<JobOpening>> ListJobOpeningsAsync(CancellationToken ct) =>
        ObservedAsync("list-job-openings", async () =>
        {
            IScreeningClient screening = await ResolveScreeningClientAsync(ct);

            return await screening.ListJobOpeningsAsync(ct);
        });

    /// <summary>
    /// Classifies the files in a folder and builds the batches that would be processed.
    /// </summary>
    public Task<DiscoveryResult> DiscoverAsync(string folder, CancellationToken ct) =>
        ObservedAsync("discover", async () =>
        {
            // The agent publishes the accepted types: a local copy would silently go stale.
            acceptedMimeTypes ??= await serenityClient.GetAcceptedMimeTypesAsync(ct);

            return new FileDiscovery(options).Discover(folder, acceptedMimeTypes);
        });

    /// <summary>
    /// Moves the files that were in the failed folder back to the folder, and reads it again.
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
    /// Uploads the files of a batch and returns the ones that ended up ready for an execution.
    /// </summary>
    public Task<UploadOutcome> UploadBatchAsync(Batch batch, Action<RunEvent> emit, CancellationToken ct) =>
        ObservedAsync("upload-batch", async () => await (await ResolveRunnerAsync(ct)).UploadBatchAsync(batch, emit, ct));

    /// <summary>
    /// Processes an entire folder, batch by batch, and emits the progress.
    /// </summary>
    /// <param name="discovery">
    /// What the host already classified and showed, so that what was previewed and what runs are the same.
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
    /// Renews the session's access token without waiting for it to be about to expire.
    /// </summary>
    public Task RefreshSessionAsync(CancellationToken ct) =>
        ObservedAsync("refresh", () => tokenStore.ForceRefreshAsync(ct));

    /// <summary>
    /// Closes the session and forgets what was resolved with it.
    /// </summary>
    public Task LogoutAsync(CancellationToken ct) =>
        ObservedAsync("logout", async () =>
        {
            await tokenStore.CloseAsync(ct);

            // The agent's configuration is resolved once per session, and the next one may belong to another
            // tenant.
            ForgetAgent();
        });

    /// <summary>
    /// Returns the recorded runs, from most recent to oldest.
    /// </summary>
    public IReadOnlyList<RunRecord> ListRuns(int limit) => history.List(limit);

    /// <summary>
    /// Returns a previous run by its identifier, or null if its record is no longer there.
    /// </summary>
    public RunRecord? FindRun(string runId) => history.Find(runId);

    public void Dispose() => httpClient.Dispose();

    /// <summary>
    /// The agent the session works against, or a fatal error if sign-in has not discovered it yet.
    /// </summary>
    private string ResolvedAgentCode() =>
        agentCode ?? throw new ScreeningLoaderException(
            ErrorKind.Fatal,
            "Todavía no se resolvió el agente que sirve la app.");

    /// <summary>
    /// Forgets the agent and everything that was resolved through it.
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
    /// The agent publishes the code and it is not configured, so it is resolved once per session.
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
