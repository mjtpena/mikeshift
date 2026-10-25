using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using MikeShift.Core.Interaction;
using Windows.Win32.Foundation;

namespace MikeShift.App.Wheel;

/// <summary>
/// The wheel's OLE drop target, registered with <c>RegisterDragDrop</c> on the wheel window. Every call is forwarded to the shell's
/// <c>IDropTargetHelper</c> so Explorer's drag image keeps rendering over the wheel. The effect answered is <b>always COPY or NONE, never MOVE</b>
/// (a Shift-drag means MOVE to Explorer, and a MOVE result would make it delete the originals). Exceptions never cross the COM boundary.
/// </summary>
[ComVisible(true)]
internal sealed class WheelDropTarget : IDropTarget, IDisposable
{
    private readonly WheelController _controller;
    private readonly HWND _hwnd;
    private IDropTargetHelper? _helper;
    private bool _registered;

    public WheelDropTarget(WheelController controller, HWND hwnd)
    {
        _controller = controller;
        _hwnd = hwnd;
    }

    /// <summary>Registers the target with OLE (the thread must be an initialised STA).</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2050", Justification = TrimJustification.ComInterop)]
    public void Register()
    {
        try
        {
            _helper = OleNative.CreateInstance<IDropTargetHelper>(OleNative.ClsidDragDropHelper, OleNative.IidDropTargetHelper);
            if (_helper is null) WheelLog.Error("IDropTargetHelper unavailable: Explorer's drag image will not render over the wheel");
        }
        catch (Exception ex)
        {
            WheelLog.Error("IDropTargetHelper unavailable: Explorer's drag image will not render over the wheel", ex);
        }

        var hr = OleNative.RegisterDragDrop(_hwnd, this);
        if (hr != OleNative.S_OK) throw new InvalidOperationException($"RegisterDragDrop failed: 0x{hr:X8}");
        _registered = true;
    }

    private static PointPx ToPoint(OlePoint p) => new(p.X, p.Y);

    public int DragEnter(System.Runtime.InteropServices.ComTypes.IDataObject pDataObj, uint grfKeyState, OlePoint pt, ref uint pdwEffect)
    {
        var allowed = pdwEffect;
        uint result = DropEffects.None;
        try
        {
            result = _controller.OnDragEnter(pDataObj, grfKeyState, ToPoint(pt), allowed);
        }
        catch (Exception ex)
        {
            WheelLog.Error("DragEnter failed", ex);
        }
        Forward(h => h.DragEnter(_hwnd, pDataObj, ref pt, result));
        pdwEffect = result;
        return OleNative.S_OK;
    }

    public int DragOver(uint grfKeyState, OlePoint pt, ref uint pdwEffect)
    {
        var allowed = pdwEffect;
        uint result = DropEffects.None;
        try
        {
            result = _controller.OnDragOver(grfKeyState, ToPoint(pt), allowed);
        }
        catch (Exception ex)
        {
            WheelLog.Error("DragOver failed", ex);
        }
        Forward(h => h.DragOver(ref pt, result));
        pdwEffect = result;
        return OleNative.S_OK;
    }

    public int DragLeave()
    {
        try
        {
            _controller.OnDragLeave();
        }
        catch (Exception ex)
        {
            WheelLog.Error("DragLeave failed", ex);
        }
        Forward(h => h.DragLeave());
        return OleNative.S_OK;
    }

    public int Drop(System.Runtime.InteropServices.ComTypes.IDataObject pDataObj, uint grfKeyState, OlePoint pt, ref uint pdwEffect)
    {
        var allowed = pdwEffect;
        Forward(h => h.Drop(pDataObj, ref pt, allowed));
        uint result = DropEffects.None;
        try
        {
            result = _controller.OnDrop(pDataObj, grfKeyState, ToPoint(pt), allowed);
        }
        catch (Exception ex)
        {
            WheelLog.Error("Drop failed", ex);
        }
        pdwEffect = result;
        return OleNative.S_OK;
    }

    private void Forward(Func<IDropTargetHelper, int> call, [System.Runtime.CompilerServices.CallerMemberName] string? caller = null)
    {
        if (_helper is not { } helper) return;
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            call(helper);
            var ms = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000 / System.Diagnostics.Stopwatch.Frequency;
            if (ms > 50) WheelLog.Stage($"slow IDropTargetHelper call from {caller}: {ms} ms");
        }
        catch (Exception ex)
        {
            WheelLog.Error("IDropTargetHelper call failed", ex);
        }
    }

    public void Dispose()
    {
        if (_registered)
        {
            OleNative.RevokeDragDrop(_hwnd);
            _registered = false;
        }
        if (_helper is not null)
        {
            Marshal.ReleaseComObject(_helper);
            _helper = null;
        }
    }
}
