namespace MikeShift.Core.Hosting;

/// <summary>
/// Where the app keeps its own files (log, hints counter, settings file of an unpackaged run). A packaged (MSIX) process that writes under
/// <c>%LOCALAPPDATA%</c> is redirected by file-system virtualisation to <c>Packages\&lt;family&gt;\LocalCache\Local\</c>; the app reads its files
/// back through the same redirection, but Explorer does not, so every place that <i>shows or opens</i> the folder must use the real path.
/// </summary>
public static class AppDataPaths
{
    /// <summary>The folder name under Local.</summary>
    public const string FolderName = "MikeShift";

    /// <summary>
    /// The real folder: <paramref name="packagedLocalCache"/> (<c>ApplicationData.Current.LocalCacheFolder</c>, null when unpackaged) + <c>Local\MikeShift</c>,
    /// else <paramref name="localAppData"/> + <c>MikeShift</c>.
    /// </summary>
    public static string Resolve(string? packagedLocalCache, string localAppData) =>
        string.IsNullOrWhiteSpace(packagedLocalCache)
            ? Path.Combine(localAppData, FolderName)
            : Path.Combine(packagedLocalCache, "Local", FolderName);
}
