using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using MikeShift.Core;
using MikeShift.Core.Shell;

namespace MikeShift.App.Shell;

// The objects Explorer talks to. Every method is a COM call from Explorer's UI thread: they only read names (never file contents), answer
// from the cached capabilities, and never throw (a managed exception crossing a COM boundary would end the server; Explorer itself is not
// in this process, but a dead server means a missing menu entry). Anything slow happens after Invoke on a background task.

/// <summary>The "Convert with MikeShift" command: title, icon, visibility and the submenu.</summary>
[GeneratedComClass]
internal sealed partial class ConvertCommand : IExplorerCommand
{
    // Explorer asks title, icon, state and sub-commands for the same selection one after the other: remember the last one.
    private IShellItemArray? _lastItems;
    private ContextMenuPlan? _lastPlan;
    private readonly object _gate = new();

    private ContextMenuPlan PlanFor(IShellItemArray? items)
    {
        lock (_gate)
        {
            if (items is not null && _lastPlan is not null && ReferenceEquals(items, _lastItems)) return _lastPlan;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var selection = ShellSelection.Read(items, ContextMenuPlanner.LargeSelection);
            var plan = selection.ToPlan(CachedCapabilities.Get());
            ShellLog.Info($"menu for {selection.Count} item(s): {(plan.Visible ? plan.Entries.Count + " entries" : "hidden")}{(plan.Capped ? " (capped)" : "")}, {clock.Elapsed.TotalMilliseconds:F1} ms");
            if (items is not null)
            {
                _lastItems = items;
                _lastPlan = plan;
            }
            return plan;
        }
    }

    public int GetTitle(IShellItemArray? items, out nint name)
    {
        using var _ = ServerLifetime.Begin();
        name = Native.ToCoTaskMem(ContextMenuPlanner.TopLevelTitle);
        return Hr.Ok;
    }

    public int GetIcon(IShellItemArray? items, out nint icon)
    {
        using var _ = ServerLifetime.Begin();
        icon = Native.ToCoTaskMem(IconPath.Value);
        return Hr.Ok;
    }

    public int GetToolTip(IShellItemArray? items, out nint tip)
    {
        tip = 0;
        return Hr.NotImpl;
    }

    public int GetCanonicalName(out Guid name)
    {
        name = ShellNames.ModernCommandClsid;
        return Hr.Ok;
    }

    public int GetState(IShellItemArray? items, int okToBeSlow, out int state)
    {
        using var _ = ServerLifetime.Begin();
        state = CommandState.Hidden;
        try
        {
            state = PlanFor(items).Visible ? CommandState.Enabled : CommandState.Hidden;
        }
        catch (Exception ex)
        {
            ShellLog.Error("GetState failed", ex);
        }
        return Hr.Ok;
    }

    public int Invoke(IShellItemArray? items, nint bindContext)
    {
        using var _ = ServerLifetime.Begin();
        // A command with a submenu is not normally invoked; if it is, it behaves like "More options…".
        return ShellDispatch.Send(ShellVerb.Wheel, "", items);
    }

    public int GetFlags(out int flags)
    {
        flags = CommandFlags.HasSubCommands;
        return Hr.Ok;
    }

    public unsafe int EnumSubCommands(out IEnumExplorerCommand? commands)
    {
        using var _ = ServerLifetime.Begin();
        commands = null;
        try
        {
            // EnumSubCommands gets no selection: it is the one GetState saw (Explorer always asks the state first). Without one, offer Zip.
            ContextMenuPlan plan;
            lock (_gate) plan = _lastPlan ?? new ContextMenuPlan(true, [new ContextMenuEntry("Zip", "Zip")], false);
            var list = new List<IExplorerCommand>();
            foreach (var entry in plan.Entries) list.Add(new ActionCommand(entry.Label, entry.ActionTitle));
            list.Add(new SeparatorCommand());
            list.Add(new MoreOptionsCommand());
            commands = new CommandEnumerator(list);
            return Hr.Ok;
        }
        catch (Exception ex)
        {
            ShellLog.Error("EnumSubCommands failed", ex);
            return Hr.Fail;
        }
    }

    /// <summary>The icon Explorer shows next to the entry: the app icon next to the executable.</summary>
    internal static class IconPath
    {
        public static readonly string Value = Path.Combine(AppContext.BaseDirectory, "Assets", "MikeShift.ico");
    }
}

/// <summary>One action line of the submenu ("Convert to JPG"): runs the action on the selection Explorer hands to <see cref="Invoke"/>.</summary>
[GeneratedComClass]
internal sealed partial class ActionCommand(string label, string actionTitle) : IExplorerCommand
{
    public int GetTitle(IShellItemArray? items, out nint name)
    {
        name = Native.ToCoTaskMem(label);
        return Hr.Ok;
    }

    public int GetIcon(IShellItemArray? items, out nint icon)
    {
        icon = 0;
        return Hr.NotImpl;
    }

    public int GetToolTip(IShellItemArray? items, out nint tip)
    {
        tip = 0;
        return Hr.NotImpl;
    }

    public int GetCanonicalName(out Guid name)
    {
        name = Guid.Empty;
        return Hr.Ok;
    }

    public int GetState(IShellItemArray? items, int okToBeSlow, out int state)
    {
        state = CommandState.Enabled;
        return Hr.Ok;
    }

    public int Invoke(IShellItemArray? items, nint bindContext)
    {
        using var _ = ServerLifetime.Begin();
        return ShellDispatch.Send(ShellVerb.Run, actionTitle, items);
    }

    public int GetFlags(out int flags)
    {
        flags = CommandFlags.Default;
        return Hr.Ok;
    }

    public int EnumSubCommands(out IEnumExplorerCommand? commands)
    {
        commands = null;
        return Hr.NotImpl;
    }
}

/// <summary>"More options…": opens the wheel for the selection.</summary>
[GeneratedComClass]
internal sealed partial class MoreOptionsCommand : IExplorerCommand
{
    public int GetTitle(IShellItemArray? items, out nint name)
    {
        name = Native.ToCoTaskMem(ContextMenuPlanner.MoreOptionsLabel);
        return Hr.Ok;
    }

    public int GetIcon(IShellItemArray? items, out nint icon)
    {
        icon = 0;
        return Hr.NotImpl;
    }

    public int GetToolTip(IShellItemArray? items, out nint tip)
    {
        tip = 0;
        return Hr.NotImpl;
    }

    public int GetCanonicalName(out Guid name)
    {
        name = Guid.Empty;
        return Hr.Ok;
    }

    public int GetState(IShellItemArray? items, int okToBeSlow, out int state)
    {
        state = CommandState.Enabled;
        return Hr.Ok;
    }

    public int Invoke(IShellItemArray? items, nint bindContext)
    {
        using var _ = ServerLifetime.Begin();
        return ShellDispatch.Send(ShellVerb.Wheel, "", items);
    }

    public int GetFlags(out int flags)
    {
        flags = CommandFlags.Default;
        return Hr.Ok;
    }

    public int EnumSubCommands(out IEnumExplorerCommand? commands)
    {
        commands = null;
        return Hr.NotImpl;
    }
}

/// <summary>The line between the actions and "More options…".</summary>
[GeneratedComClass]
internal sealed partial class SeparatorCommand : IExplorerCommand
{
    public int GetTitle(IShellItemArray? items, out nint name)
    {
        name = Native.ToCoTaskMem("");
        return Hr.Ok;
    }

    public int GetIcon(IShellItemArray? items, out nint icon)
    {
        icon = 0;
        return Hr.NotImpl;
    }

    public int GetToolTip(IShellItemArray? items, out nint tip)
    {
        tip = 0;
        return Hr.NotImpl;
    }

    public int GetCanonicalName(out Guid name)
    {
        name = Guid.Empty;
        return Hr.Ok;
    }

    public int GetState(IShellItemArray? items, int okToBeSlow, out int state)
    {
        state = CommandState.Enabled;
        return Hr.Ok;
    }

    public int Invoke(IShellItemArray? items, nint bindContext) => Hr.Ok;

    public int GetFlags(out int flags)
    {
        flags = CommandFlags.IsSeparator;
        return Hr.Ok;
    }

    public int EnumSubCommands(out IEnumExplorerCommand? commands)
    {
        commands = null;
        return Hr.NotImpl;
    }
}

/// <summary>The submenu's enumerator.</summary>
[GeneratedComClass]
internal sealed partial class CommandEnumerator : IEnumExplorerCommand
{
    private readonly List<IExplorerCommand> _items;
    private int _next;

    public CommandEnumerator(List<IExplorerCommand> items) => _items = items;

    public unsafe int Next(uint count, nint commands, out uint fetched)
    {
        fetched = 0;
        if (commands == 0) return Hr.Pointer;
        var output = (nint*)commands;
        while (fetched < count && _next < _items.Count)
        {
            output[fetched++] = ComBridge.ToPointer(_items[_next++]);
        }
        return fetched == count ? Hr.Ok : Hr.False;
    }

    public int Skip(uint count)
    {
        _next = (int)Math.Min((long)_next + count, _items.Count);
        return _next < _items.Count ? Hr.Ok : Hr.False;
    }

    public int Reset()
    {
        _next = 0;
        return Hr.Ok;
    }

    public int Clone(out IEnumExplorerCommand? clone)
    {
        clone = new CommandEnumerator(_items) { _next = _next };
        return Hr.Ok;
    }
}

/// <summary>Reads the selection Explorer hands to Invoke and passes the request on to the tray app (off the calling thread).</summary>
internal static class ShellDispatch
{
    private const int MaxItems = 100_000;

    public static int Send(ShellVerb verb, string actionTitle, IShellItemArray? items)
    {
        try
        {
            var selection = ShellSelection.Read(items, MaxItems);
            if (selection.Paths is not { Count: > 0 } paths)
            {
                ShellLog.Info($"{verb}: nothing to send (count {selection.Count}, file-system items {(selection.Paths is null ? "no" : "yes")})");
                return Hr.Ok;
            }
            ShellLog.Info($"{verb} {actionTitle}: {paths.Count} of {selection.Count} item(s) read from Explorer's selection");
            var request = new ShellRequest(verb, actionTitle, paths);
            var work = ServerLifetime.Begin(); // the process must outlive Invoke until the tray app has the request
            _ = Task.Run(() =>
            {
                try { ShellPipeClient.Send(request); }
                catch (Exception ex) { ShellLog.Error("sending to the tray app failed", ex); }
                finally { work.Dispose(); }
            });
            return Hr.Ok;
        }
        catch (Exception ex)
        {
            ShellLog.Error("Invoke failed", ex);
            return Hr.Fail;
        }
    }
}
