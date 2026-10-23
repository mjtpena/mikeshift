using Microsoft.Win32;
using Windows.ApplicationModel;

namespace MikeShift.App.Settings;

/// <summary>Outcome of changing Open at login.</summary>
/// <param name="Enabled">The state after the attempt.</param>
/// <param name="Message">Why it could not be changed (user disabled it in Windows Settings, policy...), or null.</param>
public sealed record StartupResult(bool Enabled, string? Message);

/// <summary>
/// Open at login. Packaged: the manifest's <c>windows.startupTask</c> (<c>MikeShiftStartup</c>), which also shows up in
/// Settings → Apps → Startup. Unpackaged (dev runs): the HKCU Run key. Never throws.
/// </summary>
public sealed class StartupService
{
    /// <summary>The <c>TaskId</c> in <c>Package.appxmanifest</c>.</summary>
    public const string TaskId = "MikeShiftStartup";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "MikeShift";

    /// <summary>Argument the unpackaged Run-key entry passes, so a start at login can be told from the user starting the app.</summary>
    public const string StartupArgument = "--startup";

    /// <summary>
    /// True if this process was started by Open at login: packaged, the activation kind is StartupTask; unpackaged, the Run-key entry
    /// carries <see cref="StartupArgument"/>. Never throws.
    /// </summary>
    public static bool WasLaunchedAtLogin()
    {
        try
        {
            if (PackageInfo.IsPackaged)
                return Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs().Kind
                    == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.StartupTask;
            return Environment.GetCommandLineArgs().Contains(StartupArgument, StringComparer.Ordinal);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Is Open at login currently on?</summary>
    public async Task<bool> GetEnabledAsync()
    {
        try
        {
            if (PackageInfo.IsPackaged)
            {
                var task = await StartupTask.GetAsync(TaskId);
                return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
            }

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(RunValueName) is string;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Turns Open at login on or off and reports the resulting state.</summary>
    public async Task<StartupResult> SetEnabledAsync(bool enable)
    {
        try
        {
            if (PackageInfo.IsPackaged)
                return await SetPackagedAsync(enable);

            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (enable)
            {
                var exe = Environment.ProcessPath;
                if (exe is null) return new StartupResult(false, "Could not determine the application path.");
                key.SetValue(RunValueName, $"\"{exe}\" {StartupArgument}");
            }
            else
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
            return new StartupResult(enable, null);
        }
        catch (Exception ex)
        {
            return new StartupResult(await GetEnabledAsync(), ex.Message);
        }
    }

    private static async Task<StartupResult> SetPackagedAsync(bool enable)
    {
        var task = await StartupTask.GetAsync(TaskId);
        if (enable)
        {
            switch (task.State)
            {
                case StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy:
                    return new StartupResult(true, null);
                case StartupTaskState.DisabledByUser:
                    return new StartupResult(false, "Open at login was turned off in Windows Settings (Apps, Startup). Turn it on there.");
                case StartupTaskState.DisabledByPolicy:
                    return new StartupResult(false, "Open at login is disabled by your administrator.");
                default:
                    var state = await task.RequestEnableAsync();
                    return state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy
                        ? new StartupResult(true, null)
                        : new StartupResult(false, "Windows did not allow Open at login. Check Settings, Apps, Startup.");
            }
        }

        if (task.State == StartupTaskState.Enabled) task.Disable();
        return new StartupResult(false, null);
    }
}
