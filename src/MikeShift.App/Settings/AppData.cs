using MikeShift.Core.Hosting;

namespace MikeShift.App.Settings;

/// <summary>
/// The folder for the app's own files (diagnostic log, wheel-hint counter, the settings file of an unpackaged run). Packaged, this is the
/// <i>real</i> folder (<c>%LOCALAPPDATA%\Packages\&lt;family&gt;\LocalCache\Local\MikeShift</c>), because writes to <c>%LOCALAPPDATA%\MikeShift</c> are
/// redirected there by MSIX file-system virtualisation and Explorer would show an empty or missing folder. See <see cref="AppDataPaths"/>.
/// </summary>
internal static class AppData
{
    private static readonly Lazy<string> s_folder = new(Resolve);

    /// <summary>The folder (not created here).</summary>
    public static string Folder => s_folder.Value;

    /// <summary>The path of <paramref name="fileName"/> in <see cref="Folder"/>.</summary>
    public static string FilePath(string fileName) => Path.Combine(Folder, fileName);

    private static string Resolve()
    {
        string? localCache = null;
        try
        {
            if (PackageInfo.IsPackaged) localCache = Windows.Storage.ApplicationData.Current.LocalCacheFolder.Path;
        }
        catch (Exception)
        {
            // fall back to the plain AppData path (the same files, through the redirection)
        }
        return AppDataPaths.Resolve(localCache, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    }
}
