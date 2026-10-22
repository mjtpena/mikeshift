using Windows.Media.Core;
using Windows.Media.Playback;

namespace Shiftvert.App.Hud;

/// <summary>Plays the bundled success chime (<c>Assets\chime.wav</c>) with <see cref="MediaPlayer"/>. Never throws.</summary>
public sealed class ChimePlayer
{
    private MediaPlayer? _player;
    private bool _failed;

    /// <summary>Plays the chime from the start. Call on any thread.</summary>
    public void Play()
    {
        if (_failed) return;
        try
        {
            if (_player is null)
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Assets", "chime.wav");
                if (!File.Exists(path)) { _failed = true; return; }
                _player = new MediaPlayer { Source = MediaSource.CreateFromUri(new Uri(path)), Volume = 0.6 };
            }
            _player.PlaybackSession.Position = TimeSpan.Zero;
            _player.Play();
        }
        catch (Exception)
        {
            _failed = true;
        }
    }
}
