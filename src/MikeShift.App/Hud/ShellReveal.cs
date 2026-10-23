using System.Diagnostics;

namespace MikeShift.App.Hud;

/// <summary>"Show in folder": Explorer with the written file (or folder) selected.</summary>
public static class ShellReveal
{
    /// <summary>
    /// Opens Explorer on <paramref name="path"/>'s folder with the item selected; if the item is gone, opens its folder (or nothing).
    /// Never throws.
    /// </summary>
    public static void ShowInFolder(string path)
    {
        try
        {
            var full = Path.GetFullPath(path.TrimEnd('\\', '/'));
            var exists = File.Exists(full) || Directory.Exists(full);
            var parent = Path.GetDirectoryName(full);
            var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            if (exists) start.Arguments = $"/select,\"{full}\"";
            else if (parent is not null && Directory.Exists(parent)) start.Arguments = $"\"{parent}\"";
            else return;
            using var _ = Process.Start(start);
        }
        catch (Exception)
        {
            // Explorer missing or a bad path: nothing sensible to do.
        }
    }
}
