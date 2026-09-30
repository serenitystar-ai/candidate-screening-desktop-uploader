namespace ScreeningLoader.Core.Serenity;

/// <summary>
/// An agent published to Nexus, with the app its channel serves.
/// </summary>
/// <param name="App">
/// Null for agents without an active Nexus channel, which are most of them.
/// </param>
public sealed record NexusAgent(string Code, string Name, string? App);
