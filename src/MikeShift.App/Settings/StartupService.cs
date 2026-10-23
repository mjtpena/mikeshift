using Microsoft.Win32;
using Windows.ApplicationModel;

namespace MikeShift.App.Settings;

/// <summary>Outcome of changing Open at Login.</summary>
/// <param name="Enabled">The state after the attempt.</param>
/// <param name="Message">Why it could not be changed (user disabled it in Windows Settings, policy...), or null.</param>
public sealed record StartupResult(bool Enabled, string? Message);

/// <summary>
/// Open at Login. Packaged: the manifest's <c>windows.startupTask</c> (<c>MikeShiftStartup</c>), which also shows up in
/// Settings → Apps → Startup. Unpackaged (dev runs): the HKCU Run key. Never throws.
/// </summary>
public sealed class StartupService
{
    /// <summary>The <c>TaskId</c> in <c>Package.appxmanifest</c>.</summary>
    public const string TaskId = "MikeShiftStartup";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "MikeShift";

    /// <summary>Is Open at Login currently on?</summary>
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

    /// <summary>Turns Open at Login on or off and reports the resulting state.</summary>
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
                key.SetValue(RunValueName, $"\"{exe}\"");
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
                    return new StartupResult(false, "Open at Login was turned off in Windows Settings (Apps, Startup). Turn it on there.");
                case StartupTaskState.DisabledByPolicy:
                    return new StartupResult(false, "Open at Login is disabled by your administrator.");
                default:
                    var state = await task.RequestEnableAsync();
                    return state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy
                        ? new StartupResult(true, null)
                        : new StartupResult(false, "Windows did not allow Open at Login. Check Settings, Apps, Startup.");
            }
        }

        if (task.State == StartupTaskState.Enabled) task.Disable();
        return new StartupResult(false, null);
    }
}
