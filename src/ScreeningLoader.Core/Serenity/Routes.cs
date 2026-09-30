namespace ScreeningLoader.Core.Serenity;

/// <summary>
/// Relative AI Hub routes, on top of the HttpClient's BaseAddress.
/// </summary>
internal static class Routes
{
    public const string Login = "api/v2/Account/login";
    public const string Refresh = "api/v2/Account/refresh";

    /// <summary>
    /// Agents published to Nexus, with each one's channel. Requires the NexusUser role.
    /// </summary>
    public static string NexusAgents(int pageSize) => $"api/v2/agent/nexus?page=1&pageSize={pageSize}";

    public static string Skill(string agentCode, string skillCode) =>
        $"api/v2/agent/{Uri.EscapeDataString(agentCode)}/skill/{Uri.EscapeDataString(skillCode)}/execute";

    public static string Execute(string agentCode, string culture) =>
        $"api/v2/agent/{Uri.EscapeDataString(agentCode)}/execute?culture={Uri.EscapeDataString(culture)}";

    public static string MimeTypes(string agentCode) =>
        $"api/v2/agent/{Uri.EscapeDataString(agentCode)}/volatileKnowledge/mimeTypes";

    /// <summary>
    /// Upload of a file to the agent. processEmbeddings is always sent explicitly because there are three
    /// different defaults in play depending on the entry point.
    /// </summary>
    public static string AgentVolatileKnowledge(string agentCode, bool processEmbeddings) =>
        $"api/v2/agent/{Uri.EscapeDataString(agentCode)}/volatileKnowledge"
            + $"?processEmbeddings={(processEmbeddings ? "true" : "false")}";

    public static string VolatileKnowledge(Guid id) => $"api/v2/volatileKnowledge/{id}";
}
