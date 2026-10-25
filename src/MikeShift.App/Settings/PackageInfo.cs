using System.Reflection;

namespace MikeShift.App.Settings;

/// <summary>Whether the process has package identity (MSIX) and the version string to display.</summary>
public static class PackageInfo
{
    private static readonly Lazy<bool> s_packaged = new(DetectPackaged);

    /// <summary>True when running from an installed MSIX; false for a plain <c>MikeShift.exe</c> run (dev, smoke tests).</summary>
    public static bool IsPackaged => s_packaged.Value;

    /// <summary>"0.1.0": the package version when packaged, else the assembly version.</summary>
    public static string VersionText
    {
        get
        {
            try
            {
                if (IsPackaged)
                {
                    var v = Windows.ApplicationModel.Package.Current.Id.Version;
                    return $"{v.Major}.{v.Minor}.{v.Build}";
                }
            }
            catch (Exception) { /* fall through to the assembly version */ }

            var info = typeof(PackageInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrEmpty(info))
            {
                var plus = info.IndexOf('+', StringComparison.Ordinal);
                return plus >= 0 ? info[..plus] : info;
            }
            return typeof(PackageInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }
    }

    private static bool DetectPackaged()
    {
        try
        {
            _ = Windows.ApplicationModel.Package.Current.Id;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
