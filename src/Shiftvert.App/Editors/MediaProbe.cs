using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Core;
using Windows.Media.Editing;
using Windows.Storage;

namespace Shiftvert.App.Editors;

/// <summary>Reads a media file's length and grabs a strip of frames for the trim dialog (Media Foundation via WinRT).</summary>
internal static class MediaProbe
{
    /// <summary>Thumbnail height in pixels (the strip is about 56 DIP high; this leaves room for high DPI).</summary>
    private const int ThumbnailHeight = 96;

    /// <summary>
    /// The duration, or <see cref="TimeSpan.Zero"/> when it cannot be determined. Tries the media source, then the
    /// file's video and music properties.
    /// </summary>
    public static async Task<TimeSpan> GetDurationAsync(StorageFile file)
    {
        try
        {
            using var source = MediaSource.CreateFromStorageFile(file);
            await source.OpenAsync();
            if (source.Duration is { } d && d > TimeSpan.Zero) return d;
        }
        catch (Exception)
        {
            // fall through to the property-based probes
        }

        try
        {
            var video = await file.Properties.GetVideoPropertiesAsync();
            if (video.Duration > TimeSpan.Zero) return video.Duration;
        }
        catch (Exception)
        {
        }

        try
        {
            var music = await file.Properties.GetMusicPropertiesAsync();
            if (music.Duration > TimeSpan.Zero) return music.Duration;
        }
        catch (Exception)
        {
        }

        return TimeSpan.Zero;
    }

    /// <summary>
    /// Evenly spaced frames across <paramref name="duration"/>, or an empty list for audio-only files and for anything
    /// Media Foundation cannot read (the dialog then shows a plain track).
    /// </summary>
    public static async Task<IReadOnlyList<ImageSource>> GetThumbnailsAsync(StorageFile file, TimeSpan duration, int count, CancellationToken ct)
    {
        var list = new List<ImageSource>();
        try
        {
            var clip = await MediaClip.CreateFromFileAsync(file);
            var props = clip.GetVideoEncodingProperties();
            if (props.Width == 0 || props.Height == 0) return list; // audio only

            var aspect = (double)props.Width / props.Height;
            var thumbW = (int)Math.Clamp(Math.Round(ThumbnailHeight * aspect), 16, 512);

            var composition = new MediaComposition();
            composition.Clips.Add(clip);
            var times = Enumerable.Range(0, count)
                .Select(i => TimeSpan.FromTicks((long)(duration.Ticks * (i + 0.5) / count)))
                .ToList();

            var streams = await composition.GetThumbnailsAsync(times, thumbW, ThumbnailHeight, VideoFramePrecision.NearestFrame);
            foreach (var stream in streams)
            {
                ct.ThrowIfCancellationRequested();
                var image = new BitmapImage();
                await image.SetSourceAsync(stream);
                stream.Dispose();
                list.Add(image);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Unreadable by MediaClip (codec, container...): a plain track is fine.
            list.Clear();
        }

        return list;
    }
}
