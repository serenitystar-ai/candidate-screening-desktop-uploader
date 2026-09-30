namespace ScreeningLoader.Core.Serenity;

/// <summary>
/// Rutas relativas del AI Hub, sobre el BaseAddress del HttpClient.
/// </summary>
internal static class Routes
{
    public const string Login = "api/v2/Account/login";
    public const string Refresh = "api/v2/Account/refresh";

    /// <summary>
    /// Agentes publicados a Nexus, con el canal de cada uno. Pide rol NexusUser.
    /// </summary>
    public static string NexusAgents(int pageSize) => $"api/v2/agent/nexus?page=1&pageSize={pageSize}";

    public static string Skill(string agentCode, string skillCode) =>
        $"api/v2/agent/{Uri.EscapeDataString(agentCode)}/skill/{Uri.EscapeDataString(skillCode)}/execute";

    public static string Execute(string agentCode, string culture) =>
        $"api/v2/agent/{Uri.EscapeDataString(agentCode)}/execute?culture={Uri.EscapeDataString(culture)}";

    public static string MimeTypes(string agentCode) =>
        $"api/v2/agent/{Uri.EscapeDataString(agentCode)}/volatileKnowledge/mimeTypes";

    /// <summary>
    /// Subida de un archivo al agente. processEmbeddings viaja siempre explícito porque hay tres
    /// defaults distintos en juego según por dónde se entre.
    /// </summary>
    public static string AgentVolatileKnowledge(string agentCode, bool processEmbeddings) =>
        $"api/v2/agent/{Uri.EscapeDataString(agentCode)}/volatileKnowledge"
            + $"?processEmbeddings={(processEmbeddings ? "true" : "false")}";

    public static string VolatileKnowledge(Guid id) => $"api/v2/volatileKnowledge/{id}";
}
