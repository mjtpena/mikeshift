namespace MikeShift.Core.Feedback;

/// <summary>
/// Several jobs failing while one failure dialog is still open (a batch of drags, a folder of bad files) must not stack up one topmost window
/// each. The open dialog is replaced by an aggregate: "3 jobs had problems" with every failed file listed (capped).
/// </summary>
public static class DialogMerge
{
    /// <summary>Most rows the aggregate lists before "…and N more".</summary>
    public const int MaxItems = 20;

    /// <summary>
    /// One dialog for <paramref name="notices"/> (oldest first), which together stand for <paramref name="jobCount"/> failed jobs. A single
    /// notice is returned unchanged.
    /// </summary>
    public static DialogContent Combine(IReadOnlyList<DialogContent> notices, int jobCount)
    {
        ArgumentNullException.ThrowIfNull(notices);
        if (notices.Count == 0) throw new ArgumentException("Nothing to combine.", nameof(notices));
        if (notices.Count == 1 && jobCount <= 1) return notices[0];

        var rows = new List<DialogItem>();
        foreach (var notice in notices)
        {
            // A one-file failure has no list: its heading ("Couldn't convert a.pdf") and reason are the row.
            if (notice.Items.Count == 0) rows.Add(new DialogItem(notice.Title, notice.Message));
            else rows.AddRange(notice.Items);
        }
        if (rows.Count > MaxItems)
        {
            var extra = rows.Count - MaxItems;
            rows.RemoveRange(MaxItems, extra);
            rows.Add(new DialogItem($"…and {extra} more", null));
        }

        var severity = notices.Any(n => n.Severity == DialogSeverity.Error) ? DialogSeverity.Error : DialogSeverity.Warning;
        return new DialogContent(
            $"{Math.Max(jobCount, notices.Count)} jobs had problems",
            null,
            rows,
            null,
            severity,
            [new DialogButton("Close", DialogCommand.Close)])
        { JobProblem = true };
    }
}
