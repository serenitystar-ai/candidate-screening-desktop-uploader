using System.Text.Json;
using ScreeningLoader.Core.Run;
using ScreeningLoader.Core.Screening;

namespace ScreeningLoader.Core.Serenity;

/// <summary>
/// Builds the body of an analyze-candidates execution.
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
            // The agent scores against this text: it cannot infer it from the title or look it up in the dataset.
            jobDescription = opening.Description,
            cvs = cvs.Select(cv => new
            {
                volatileKnowledgeId = cv.VolatileKnowledgeId,
                fileId = cv.FileId?.ToString() ?? string.Empty,
                fileName = cv.File.FileName
            })
        });

        // action travels twice: as a sibling key, so the platform renders the Liquid before
        // the model reads anything, and inside the directive so the model reads it.
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
