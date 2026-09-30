namespace ScreeningLoader.Core.Serenity;

/// <summary>
/// Un agente publicado a Nexus, con la app que sirve su canal.
/// </summary>
/// <param name="App">
/// Null en los agentes sin canal de Nexus activo, que son la mayoría.
/// </param>
public sealed record NexusAgent(string Code, string Name, string? App);
