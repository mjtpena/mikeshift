using System.Threading.Channels;
using MikeShift.Core.Interaction;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MikeShift.App.Input;

/// <summary>
/// <see cref="IInputMonitor"/> on <c>WH_MOUSE_LL</c> + <c>WH_KEYBOARD_LL</c> (PLAN §3.1 item 1).
/// <list type="bullet">
/// <item>A dedicated, high-priority thread owns both hooks and a message loop (low-level hooks are only called while their installing thread pumps messages).</item>
/// <item>The callbacks do nothing but copy the hook struct into an <see cref="InputEvent"/>, <c>TryWrite</c> it into a bounded channel and call
/// <c>CallNextHookEx</c>: no allocation, no LINQ, no boxing, no logging, no locks. A callback slower than <c>LowLevelHooksTimeout</c> gets the hook silently removed by Windows.</item>
/// <item>Injected events (<c>LLMHF_INJECTED</c>, <c>LLMHF_LOWER_IL_INJECTED</c>, <c>LLKHF_INJECTED</c>, <c>LLKHF_LOWER_IL_INJECTED</c>) are flagged, not dropped, so consumers decide
/// (<see cref="DragSession"/> ignores them). Mouse events synthesised from touch or pen carry the Windows pen/touch signature in <c>dwExtraInfo</c> and are NOT flagged as injected.</item>
/// <item>A watchdog re-installs the hooks if the left button is down (per our events, or per <c>GetAsyncKeyState</c>) and nothing arrived for <see cref="WatchdogTimeout"/>:
/// the symptom of a silently removed hook. The new hooks are installed before the old ones are removed so no event can fall in a gap.</item>
/// <item>Only mouse move/left button/vertical wheel and Shift, Ctrl, Alt (all variants) and Esc key events are enqueued. No other key is ever read or stored.</item>
/// </list>
/// </summary>
public sealed unsafe class LowLevelInputMonitor : IInputMonitor
{
    // Window messages and flags (winuser.h). Plain constants so the hot path has no generated-API dependence.
    private const uint WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    private const uint WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_MOUSEWHEEL = 0x020A;
    private const uint WM_QUIT = 0x0012, WM_APP_REINSTALL = 0x8000 + 1;
    private const uint LLMHF_INJECTED = 0x01, LLMHF_LOWER_IL_INJECTED = 0x02;
    private const uint LLKHF_INJECTED = 0x10, LLKHF_LOWER_IL_INJECTED = 0x02;
    private const nuint PenTouchSignatureMask = 0xFFFFFF00, PenTouchSignature = 0xFF515700; // MI_WP_SIGNATURE

    private readonly Channel<InputEvent> _channel;
    private readonly HOOKPROC _mouseProc;     // rooted for the lifetime of the monitor: the GC must never collect a native callback target
    private readonly HOOKPROC _keyboardProc;
    private readonly object _lifecycle = new();
    private readonly ManualResetEventSlim _started = new(false);

    private Thread? _thread;
    private Timer? _watchdog;
    private uint _threadId;
    private Exception? _startError;
    private HHOOK _mouseHook;
    private HHOOK _keyboardHook;
    private bool _everStarted;
    private bool _disposed;

    // Written by the hook thread, read by the watchdog thread.
    private volatile bool _leftDown;
    private long _lastEventTicks;
    private long _lastReinstallTicks;
    private long _dropped;
    private int _reinstalls;
    private int _lastX = int.MinValue, _lastY = int.MinValue;

    public LowLevelInputMonitor(int capacity = 4096, TimeSpan? watchdogTimeout = null)
    {
        _channel = Channel.CreateBounded<InputEvent>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true, // both hooks live on the one hook thread
                AllowSynchronousContinuations = false, // never run the reader's continuation inside the hook callback
            },
            _ => Interlocked.Increment(ref _dropped)); // itemDropped (DropOldest overflow); the delegate is created once, here
        _mouseProc = MouseHookProc;
        _keyboardProc = KeyboardHookProc;
        WatchdogTimeout = watchdogTimeout ?? TimeSpan.FromSeconds(4);
    }

    /// <inheritdoc />
    public ChannelReader<InputEvent> Events => _channel.Reader;

    /// <inheritdoc />
    public bool IsRunning { get; private set; }

    /// <summary>How long a held left button may go without any event before the hooks are re-installed.</summary>
    public TimeSpan WatchdogTimeout { get; set; }

    /// <summary>How many times the watchdog has re-installed the hooks.</summary>
    public int Reinstalls => Volatile.Read(ref _reinstalls);

    /// <summary>Raised on a thread-pool thread after the watchdog re-installed the hooks (for diagnostics).</summary>
    public event Action? HooksReinstalled;

    /// <inheritdoc />
    public void Start()
    {
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_everStarted) throw new InvalidOperationException("An input monitor can be started only once.");
            _everStarted = true;

            _thread = new Thread(ThreadMain)
            {
                IsBackground = true,
                Name = "MikeShift input hooks",
                Priority = ThreadPriority.Highest,
            };
            _thread.Start();
            _started.Wait();
            if (_startError is { } error)
            {
                _channel.Writer.TryComplete();
                throw new InvalidOperationException("Could not install the low-level input hooks: " + error.Message, error);
            }

            IsRunning = true;
            Volatile.Write(ref _lastEventTicks, Environment.TickCount64);
            _watchdog = new Timer(static s => ((LowLevelInputMonitor)s!).WatchdogTick(), this, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        Thread? thread;
        lock (_lifecycle)
        {
            if (_thread is null) { _channel.Writer.TryComplete(); return; }
            IsRunning = false;
            _watchdog?.Dispose();
            _watchdog = null;
            thread = _thread;
            _thread = null;
            if (_threadId != 0) PInvoke.PostThreadMessage(_threadId, WM_QUIT, default, default);
        }
        thread.Join(TimeSpan.FromSeconds(2));
        _channel.Writer.TryComplete();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
        _started.Dispose();
    }

    // ------------------------------------------------------------------ hook thread

    private void ThreadMain()
    {
        try
        {
            _threadId = PInvoke.GetCurrentThreadId();
            // Make sure this thread has a message queue before anyone posts to it.
            PInvoke.PeekMessage(out _, HWND.Null, 0x0400, 0x0400, PEEK_MESSAGE_REMOVE_TYPE.PM_NOREMOVE);
            Install(replace: false);
        }
        catch (Exception ex)
        {
            _startError = ex;
            _started.Set();
            return;
        }
        _started.Set();

        try
        {
            while (true)
            {
                var r = PInvoke.GetMessage(out var msg, HWND.Null, 0, 0);
                if (r.Value <= 0) break; // 0 = WM_QUIT, -1 = error
                if (msg.message == WM_APP_REINSTALL)
                {
                    try { Install(replace: true); Interlocked.Increment(ref _reinstalls); ThreadPool.QueueUserWorkItem(static s => ((LowLevelInputMonitor)s!).HooksReinstalled?.Invoke(), this); }
                    catch { /* keep the old hooks; the watchdog will try again */ }
                }
            }
        }
        finally
        {
            Uninstall();
        }
    }

    /// <summary>Installs both hooks. With <paramref name="replace"/> the new ones go in first and the old ones come out afterwards.</summary>
    private void Install(bool replace)
    {
        // The executable's module handle (base address). Taken from the process rather than GetModuleHandle so no owning SafeHandle can FreeLibrary it.
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var hinst = new HINSTANCE(process.MainModule!.BaseAddress);

        var mouse = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL, _mouseProc, hinst, 0);
        if (mouse.IsNull) throw new System.ComponentModel.Win32Exception();
        HHOOK keyboard;
        try
        {
            keyboard = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, _keyboardProc, hinst, 0);
            if (keyboard.IsNull) throw new System.ComponentModel.Win32Exception();
        }
        catch
        {
            PInvoke.UnhookWindowsHookEx(mouse);
            throw;
        }

        var oldMouse = _mouseHook;
        var oldKeyboard = _keyboardHook;
        _mouseHook = mouse;
        _keyboardHook = keyboard;
        if (replace)
        {
            if (!oldMouse.IsNull) PInvoke.UnhookWindowsHookEx(oldMouse);
            if (!oldKeyboard.IsNull) PInvoke.UnhookWindowsHookEx(oldKeyboard);
        }
        Volatile.Write(ref _lastEventTicks, Environment.TickCount64);
    }

    private void Uninstall()
    {
        if (!_mouseHook.IsNull) PInvoke.UnhookWindowsHookEx(_mouseHook);
        if (!_keyboardHook.IsNull) PInvoke.UnhookWindowsHookEx(_keyboardHook);
        _mouseHook = default;
        _keyboardHook = default;
    }

    // ------------------------------------------------------------------ hot path

    private LRESULT MouseHookProc(int nCode, WPARAM wParam, LPARAM lParam)
    {
        if (nCode >= 0)
        {
            try { OnMouse((uint)wParam.Value, (MSLLHOOKSTRUCT*)lParam.Value); }
            catch { /* an exception must never cross the native boundary */ }
        }
        return PInvoke.CallNextHookEx(HHOOK.Null, nCode, wParam, lParam);
    }

    private LRESULT KeyboardHookProc(int nCode, WPARAM wParam, LPARAM lParam)
    {
        if (nCode >= 0)
        {
            try { OnKey((uint)wParam.Value, (KBDLLHOOKSTRUCT*)lParam.Value); }
            catch { }
        }
        return PInvoke.CallNextHookEx(HHOOK.Null, nCode, wParam, lParam);
    }

    private void OnMouse(uint message, MSLLHOOKSTRUCT* data)
    {
        InputKind kind;
        var delta = 0;
        switch (message)
        {
            case WM_MOUSEMOVE: kind = InputKind.MouseMove; break;
            case WM_LBUTTONDOWN: kind = InputKind.LButtonDown; break;
            case WM_LBUTTONUP: kind = InputKind.LButtonUp; break;
            case WM_MOUSEWHEEL: kind = InputKind.MouseWheel; delta = (short)(data->mouseData >> 16); break;
            default: return;
        }

        var now = Environment.TickCount64;
        Volatile.Write(ref _lastEventTicks, now);
        int x = data->pt.X, y = data->pt.Y;
        if (kind == InputKind.MouseMove)
        {
            if (x == _lastX && y == _lastY) return; // a move to where we already are (e.g. our own zero-move nudge) carries no information
            _lastX = x;
            _lastY = y;
        }
        else if (kind == InputKind.LButtonDown) _leftDown = true;
        else if (kind == InputKind.LButtonUp) _leftDown = false;

        var flags = data->flags;
        var injected = (flags & (LLMHF_INJECTED | LLMHF_LOWER_IL_INJECTED)) != 0
                       && (data->dwExtraInfo & PenTouchSignatureMask) != PenTouchSignature;
        Enqueue(new InputEvent(kind, new PointPx(x, y), 0, delta, injected, now));
    }

    private void OnKey(uint message, KBDLLHOOKSTRUCT* data)
    {
        bool down;
        switch (message)
        {
            case WM_KEYDOWN:
            case WM_SYSKEYDOWN: down = true; break;
            case WM_KEYUP:
            case WM_SYSKEYUP: down = false; break;
            default: return;
        }

        var vk = (int)data->vkCode;
        if (!IsInteresting(vk)) return; // privacy: never copy anything but modifiers and Esc

        var now = Environment.TickCount64;
        Volatile.Write(ref _lastEventTicks, now);
        var injected = ((uint)data->flags & (LLKHF_INJECTED | LLKHF_LOWER_IL_INJECTED)) != 0;
        Enqueue(new InputEvent(down ? InputKind.KeyDown : InputKind.KeyUp, default, vk, 0, injected, now));
    }

    private static bool IsInteresting(int vk) =>
        vk is >= VirtualKeys.Shift and <= VirtualKeys.Menu      // 0x10..0x12
           or >= VirtualKeys.LShift and <= VirtualKeys.RMenu    // 0xA0..0xA5
           or VirtualKeys.Escape;

    private void Enqueue(in InputEvent e)
    {
        _channel.Writer.TryWrite(e); // false only once the channel is completed, i.e. while stopping
    }

    /// <summary>Events discarded because the consumer fell behind and the bounded channel overflowed (oldest first).</summary>
    public long DroppedEvents => Interlocked.Read(ref _dropped);

    // ------------------------------------------------------------------ watchdog

    private void WatchdogTick()
    {
        if (!IsRunning || _threadId == 0) return;
        var now = Environment.TickCount64;
        var timeoutMs = (long)WatchdogTimeout.TotalMilliseconds;
        var idleFor = now - Volatile.Read(ref _lastEventTicks);
        if (idleFor < timeoutMs) return;
        if (now - Volatile.Read(ref _lastReinstallTicks) < timeoutMs) return; // do not thrash while the user simply holds still

        if (!_leftDown && !AsyncInput.IsLeftButtonDown()) return;
        Volatile.Write(ref _lastReinstallTicks, now);
        PInvoke.PostThreadMessage(_threadId, WM_APP_REINSTALL, default, default);
    }
}
