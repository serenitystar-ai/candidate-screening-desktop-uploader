namespace ScreeningLoader.Core.Errors;

/// <summary>
/// No se pudo decidir qué agente sirve la app configurada.
/// </summary>
/// <remarks>
/// Se distingue del resto de los fatales porque el host la muestra con su propia pantalla: es lo único
/// que se arregla cambiando un ajuste y no llamando a quien administra el AI Hub.
/// </remarks>
public sealed class AgentNotFoundException(string message) : ScreeningLoaderException(ErrorKind.Fatal, message);
