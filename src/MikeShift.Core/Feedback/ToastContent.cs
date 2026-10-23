namespace MikeShift.Core.Feedback;

/// <summary>What the toast shows next to its text: a spinner while a job runs, a check, a warning, or nothing.</summary>
public enum ToastKind
{
    /// <summary>A plain message (no status glyph).</summary>
    Info,
    /// <summary>A job is running (progress ring).</summary>
    Progress,
    /// <summary>Everything worked (check mark).</summary>
    Success,
    /// <summary>Done, with a caveat: partial failure or the Downloads fallback (warning triangle).</summary>
    Warning,
}

/// <summary>
/// The content of one HUD toast (UX review H-04/H-05): a first line, an optional second line, a status kind and, when a file was
/// written, the path the "Show in folder" button reveals. Pure data so the wording is unit-tested without WinUI.
/// </summary>
/// <param name="Title">First line, e.g. "Saved photo 2.png" or "Converting to PNG…".</param>
/// <param name="Detail">Second line, e.g. "Next to the original", or null.</param>
/// <param name="Kind">Which status glyph to show.</param>
/// <param name="RevealPath">A written file or folder to reveal in Explorer, or null (no action button).</param>
public sealed record ToastContent(string Title, string? Detail = null, ToastKind Kind = ToastKind.Info, string? RevealPath = null)
{
    /// <summary>Both lines on one line, separated by " · ".</summary>
    public string Text => string.IsNullOrEmpty(Detail) ? Title : Title + " · " + Detail;

    /// <summary>What a screen reader announces: both lines as sentences.</summary>
    public string SpokenText => string.IsNullOrEmpty(Detail) ? Title : Title.TrimEnd('.', '…') + ". " + Detail;

    /// <summary>Whether the toast has the "Show in folder" button.</summary>
    public bool HasAction => !string.IsNullOrEmpty(RevealPath);

    /// <summary>How long the toast stays: progress hides soonest, results with an action stay longest so the button can be used.</summary>
    public TimeSpan Lifetime => Kind == ToastKind.Progress ? TimeSpan.FromSeconds(2.5) : HasAction ? TimeSpan.FromSeconds(6) : TimeSpan.FromSeconds(3.5);
}
