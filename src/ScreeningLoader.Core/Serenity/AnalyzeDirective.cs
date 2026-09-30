using System.Text.Json;
using ScreeningLoader.Core.Run;
using ScreeningLoader.Core.Screening;

namespace ScreeningLoader.Core.Serenity;

/// <summary>
/// Arma el cuerpo de una ejecución de analyze-candidates.
/// </summary>
internal static class AnalyzeDirective
{
    public const string Action = "analyze-candidates";

    public static object[] Build(JobOpening opening, IReadOnlyList<UploadedCv> cvs, string responseLanguage)
    {
        string message = JsonSerializer.Serialize(new
        {
            action = Action,
            jobOpeningId = opening.Id,
            jobTitle = opening.Title,
            // El agente puntúa contra este texto: no puede inferirlo del título ni buscarlo en el dataset.
            jobDescription = opening.Description,
            cvs = cvs.Select(cv => new
            {
                volatileKnowledgeId = cv.VolatileKnowledgeId,
                fileId = cv.FileId?.ToString() ?? string.Empty,
                fileName = cv.File.FileName
            })
        });

        // action viaja dos veces: como clave hermana, para que la plataforma renderice el Liquid antes de que
        // el modelo lea nada, y adentro de la directiva para que el modelo lo lea.
        return
        [
            new { Key = "message", Value = (object)message },
            new { Key = "action", Value = (object)Action },
            new { Key = "stream", Value = (object)true },
            new { Key = "response_language", Value = (object)responseLanguage },
            new { Key = "volatileKnowledgeIds", Value = (object)cvs.Select(cv => cv.VolatileKnowledgeId).ToArray() }
        ];
    }
}
