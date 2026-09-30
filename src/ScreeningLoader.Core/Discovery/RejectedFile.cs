namespace ScreeningLoader.Core.Discovery;

/// <summary>
/// A file that is left out of the run, and why.
/// </summary>
public sealed record RejectedFile(string FileName, RejectionReason Reason);

/// <summary>
/// Reason a file is left out before being uploaded.
/// </summary>
public enum RejectionReason
{
    /// <summary>The agent does not accept that file type.</summary>
    UnsupportedType,

    /// <summary>It has no content.</summary>
    Empty,

    /// <summary>It exceeds the maximum size per file.</summary>
    TooLarge,

    /// <summary>On its own it exceeds a batch's size budget.</summary>
    ExceedsBatchBudget,

    /// <summary>The user left it out of this run.</summary>
    ExcludedByUser
}
