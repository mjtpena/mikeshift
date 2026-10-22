using Microsoft.UI.Dispatching;
using Shiftvert.App.Hud;
using Shiftvert.App.Jobs;
using Shiftvert.App.Settings;
using Shiftvert.Core;

namespace Shiftvert.App.Tray;

/// <summary>
/// The shared services of the running app, created once in <c>App.OnLaunched</c> and reachable as
/// <c>App.Current.Services</c>. W3 (wheel) and W4a (editors) use these; nothing here is static state.
/// </summary>
public sealed class AppServices
{
    private AppServices(DispatcherQueue dispatcher)
    {
        Dispatcher = dispatcher;
        Preferences = new Preferences();
        Startup = new StartupService();
        Hud = new HudController(dispatcher);
        Chime = new ChimePlayer();
        Dialogs = new Win32DialogService();
        Interaction = new UiInteraction(dispatcher, Hud);
        Jobs = new JobRunner(dispatcher, Preferences, Hud, Dialogs, Chime) { DefaultInteraction = Interaction };
    }

    /// <summary>The UI thread's queue.</summary>
    public DispatcherQueue Dispatcher { get; }

    /// <summary>User preferences (chime, showsToast, toolsKey, launched).</summary>
    public Preferences Preferences { get; }

    /// <summary>Open at Login.</summary>
    public StartupService Startup { get; }

    /// <summary>The HUD toast (<c>Hud.Show("text")</c>, any thread).</summary>
    public HudController Hud { get; }

    /// <summary>The success chime.</summary>
    public ChimePlayer Chime { get; }

    /// <summary>Modal failure/warning dialogs.</summary>
    public Win32DialogService Dialogs { get; }

    /// <summary>How Crop/Trim ask the user for input; W4a attaches the editors here.</summary>
    public UiInteraction Interaction { get; }

    /// <summary>Runs actions and reports the outcome; also holds <c>LastOutputs</c>/<c>LastFinishedAt</c>.</summary>
    public JobRunner Jobs { get; }

    /// <summary>What this machine can do (probed once; the first access may take a moment, so startup warms it up).</summary>
    public Capabilities Capabilities => Capabilities.Current;

    /// <summary>Creates the services on the UI thread.</summary>
    public static AppServices Create(DispatcherQueue dispatcher) => new(dispatcher);
}
