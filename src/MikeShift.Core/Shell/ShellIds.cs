namespace MikeShift.Core.Shell;

/// <summary>
/// The names that tie the Explorer right-click entry together across the package manifest, the extension process and the tray app. Kept here (not in
/// the app) so a test can check them against <c>Package.appxmanifest</c>.
/// </summary>
public static class ShellIds
{
    /// <summary>CLSID of the IExplorerCommand. <c>Package.appxmanifest</c> declares it as a COM class and as the verb's handler.</summary>
    public static readonly Guid CommandClsid = new("02dcb673-3099-4372-86cb-c7f4fad75db8");

    /// <summary>The argument COM adds when it starts the extension server (<c>com:ExeServer Arguments</c> in the manifest).</summary>
    public const string EmbeddingSwitch = "-Embedding";

    /// <summary>The local pipe the tray app listens on, one per Windows user (the SID keeps two signed-in users on one PC apart).</summary>
    public static string PipeNameFor(string userSid)
    {
        if (string.IsNullOrWhiteSpace(userSid)) throw new ArgumentException("A user id is required.", nameof(userSid));
        return "mikeshift-shell-" + userSid.Trim();
    }
}
