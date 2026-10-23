namespace MikeShift.Core.Documents;

/// <summary>Reads any supported document file into the pivot <see cref="DocModel"/>.</summary>
public static class DocReaders
{
    /// <summary>Refuse absurdly large inputs instead of exhausting memory.</summary>
    public const long MaxInputBytes = 256L * 1024 * 1024;

    /// <summary>True if a reader exists for the lower-case extension (no dot). Legacy .doc is not readable without Word.</summary>
    public static bool CanRead(string ext) => ext is "docx" or "odt" or "rtf" or "html" or "htm" or "txt" or "md" or "markdown";

    /// <summary>Reads <paramref name="path"/> by its extension. Throws <see cref="ConversionException"/> for unreadable or invalid files.</summary>
    public static DocModel Read(string path, CancellationToken ct = default)
    {
        var ext = Kind.ExtensionOf(path);
        if (!CanRead(ext))
            throw new ConversionException(ext == "doc"
                ? "Legacy .doc files need Microsoft Word to be installed."
                : $"Can't read .{ext} documents.");

        try
        {
            var length = new FileInfo(path).Length;
            if (length > MaxInputBytes)
                throw new ConversionException("This file is too large to convert.");

            ct.ThrowIfCancellationRequested();
            return ext switch
            {
                "docx" => DocxReader.Read(path),
                "odt" => OdtReader.Read(path),
                "rtf" => RtfReader.Read(File.ReadAllBytes(path)),
                "html" or "htm" => HtmlReader.Read(TextDecoding.DecodeHtml(File.ReadAllBytes(path))),
                _ => PlainTextReader.Read(TextDecoding.Decode(File.ReadAllBytes(path))),
            };
        }
        catch (FileNotFoundException)
        {
            throw new ConversionException("The file no longer exists.");
        }
        catch (DirectoryNotFoundException)
        {
            throw new ConversionException("The file no longer exists.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new ConversionException("MikeShift isn't allowed to read this file.");
        }
        catch (IOException ex)
        {
            throw new ConversionException("The file couldn't be read: " + ex.Message, ex);
        }
    }

    /// <summary>True if the file starts with the ZIP signature <c>PK</c> (DOCX and ODT are zip packages).</summary>
    public static bool StartsWithPk(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 16);
        return fs.ReadByte() == 'P' && fs.ReadByte() == 'K';
    }
}

/// <summary>TXT and Markdown: every line becomes a paragraph; Markdown stays plain text.</summary>
public static class PlainTextReader
{
    public static DocModel Read(string text)
    {
        var model = new DocModel();
        if (text.Length == 0) return model;

        var start = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] != '\n' && text[i] != '\r') continue;
            model.Blocks.Add(NewParagraph(text.AsSpan(start, i - start)));
            if (i < text.Length && text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            start = i + 1;
        }
        // A trailing newline terminates the last line; it does not start another paragraph.
        if (text[^1] is '\n' or '\r' && model.Blocks.Count > 0) model.Blocks.RemoveAt(model.Blocks.Count - 1);
        return model;
    }

    private static DocParagraph NewParagraph(ReadOnlySpan<char> line)
    {
        var p = new DocParagraph();
        p.Add(line.ToString().Replace("\f", ""));
        return p;
    }
}
