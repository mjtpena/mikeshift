namespace MikeShift.Core.Feedback;

/// <summary>How serious the dialog is; picks the icon and its colour.</summary>
public enum DialogSeverity
{
    /// <summary>Nothing worked (critical red).</summary>
    Error,
    /// <summary>Something worked, something did not (caution amber).</summary>
    Warning,
}

/// <summary>What a dialog button does.</summary>
public enum DialogCommand
{
    /// <summary>Just closes the dialog.</summary>
    Close,
    /// <summary>Reveals <see cref="DialogButton.Path"/> in Explorer.</summary>
    ShowInFolder,
    /// <summary>Quits MikeShift.</summary>
    QuitApp,
    /// <summary>Opens <see cref="DialogButton.Path"/> (an <c>ms-windows-store://</c> link) in the Microsoft Store.</summary>
    OpenStore,
}

/// <summary>One dialog button.</summary>
/// <param name="Label">Button text.</param>
/// <param name="Command">What it does.</param>
/// <param name="Path">For <see cref="DialogCommand.ShowInFolder"/>: the file or folder to reveal; for <see cref="DialogCommand.OpenStore"/>: the Store link.</param>
public sealed record DialogButton(string Label, DialogCommand Command, string? Path = null);

/// <summary>One line of a dialog's list: a file and why it failed.</summary>
/// <param name="Name">File name (or "…and 2 more").</param>
/// <param name="Reason">Why, or null for a plain line.</param>
public sealed record DialogItem(string Name, string? Reason)
{
    /// <summary>"huge.png - Too large (300 megapixels; the limit is 200)." on one line.</summary>
    public string Line => string.IsNullOrEmpty(Reason) ? Name : $"{Name} - {Reason}";
}

/// <summary>
/// Everything a Fluent dialog needs: title, intro text, a list, a footer and the buttons. The last button
/// is the default one (Enter), and Esc always closes.
/// </summary>
/// <param name="Title">Heading, e.g. "Couldn't convert corrupt.pdf".</param>
/// <param name="Message">Intro paragraph, or null.</param>
/// <param name="Items">List rows (empty when there is no list).</param>
/// <param name="Footer">A closing line such as "The 2 converted files are next to the originals.", or null.</param>
/// <param name="Severity">Error or warning.</param>
/// <param name="Buttons">Left to right; the last one is the default (accent) button.</param>
public sealed record DialogContent(
    string Title,
    string? Message,
    IReadOnlyList<DialogItem> Items,
    string? Footer,
    DialogSeverity Severity,
    IReadOnlyList<DialogButton> Buttons)
{
    /// <summary>The dialog reports a job that failed (fully or partly). While one is open, later ones are merged into it (<see cref="DialogMerge"/>) instead of opening more windows.</summary>
    public bool JobProblem { get; init; }

    /// <summary>A one-button dialog ("Close").</summary>
    public static DialogContent Simple(string title, string message, DialogSeverity severity = DialogSeverity.Error) =>
        new(title, message, [], null, severity, [new DialogButton("Close", DialogCommand.Close)]);

    /// <summary>The whole dialog as plain text (screen reader name, logs, tests).</summary>
    public string PlainText => string.Join(Environment.NewLine, new[] { Title, Message }
        .Concat(Items.Select(i => i.Line)).Append(Footer).Where(s => !string.IsNullOrEmpty(s)));
}
