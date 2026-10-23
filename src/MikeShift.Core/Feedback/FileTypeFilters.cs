namespace MikeShift.Core.Feedback;

/// <summary>One entry of the file type drop-down in the Convert files… picker.</summary>
/// <param name="Name">Label, e.g. "Images".</param>
/// <param name="Spec">Semicolon-separated wildcard patterns, e.g. "*.png;*.jpg".</param>
public sealed record FileTypeFilter(string Name, string Spec);

/// <summary>The wording and file type list of the Convert files… dialog (UX review C-01).</summary>
public static class FileTypeFilters
{
    /// <summary>Dialog title: an instruction, not a label.</summary>
    public const string Title = "Choose files to convert";

    /// <summary>Text of the confirm button (instead of "Open").</summary>
    public const string OkLabel = "Choose";

    /// <summary>The drop-down: every supported kind together first, then each kind, then everything.</summary>
    public static IReadOnlyList<FileTypeFilter> All { get; } = Build();

    private static List<FileTypeFilter> Build()
    {
        string Spec(IEnumerable<string> extensions) => string.Join(';', extensions.Select(e => "*." + e));

        var images = Kind.ImageExtensions.Order(StringComparer.Ordinal).ToList();
        var video = Kind.VideoExtensions.Order(StringComparer.Ordinal).ToList();
        var audio = Kind.AudioExtensions.Order(StringComparer.Ordinal).ToList();
        var documents = Kind.DocumentExtensions.Order(StringComparer.Ordinal).ToList();
        var everything = images.Concat([Kind.PdfExtension]).Concat(video).Concat(audio).Concat(documents);

        return
        [
            new("Supported files", Spec(everything)),
            new("Images", Spec(images)),
            new("Videos", Spec(video)),
            new("Audio", Spec(audio)),
            new("PDF and documents", Spec(new[] { Kind.PdfExtension }.Concat(documents))),
            new("All files", "*.*"),
        ];
    }
}
