namespace MikeShift.Core.Hosting;

/// <summary>Whether the welcome window opens at start-up.</summary>
public static class WelcomePolicy
{
    /// <summary>
    /// Show it on the first launch only. If "launched" could not be remembered (read-only profile, full disk), an interactive launch still
    /// shows it, but a launch by the login startup task does not, or it would pop up at every sign-in.
    /// </summary>
    /// <param name="alreadyLaunched">The welcome window was shown before.</param>
    /// <param name="rememberedNow">Storing "launched" just now worked.</param>
    /// <param name="startedAtLogin">This process was started by the Open at login startup task.</param>
    public static bool ShouldShow(bool alreadyLaunched, bool rememberedNow, bool startedAtLogin) =>
        !alreadyLaunched && (rememberedNow || !startedAtLogin);
}
