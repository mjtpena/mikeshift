using MikeShift.App.Wheel;
using MikeShift.Core.Hotkeys;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MikeShift.App.Input;

/// <summary>Whether the hotkey is active.</summary>
internal enum HotkeyState
{
    /// <summary>No hotkey is configured ("None").</summary>
    Off,
    /// <summary>Registered: pressing it reaches MikeShift.</summary>
    Registered,
    /// <summary>Windows refused it, usually because another program already owns this shortcut.</summary>
    Taken,
}

/// <summary>
/// One global hotkey (<c>RegisterHotKey</c>) on a thread of its own: an STA with a message loop that blocks in <c>GetMessage</c>, so it costs
/// nothing while idle and never runs on the low-level hook thread (whose callbacks must return in milliseconds) or the wheel thread. The shell
/// calls that read the Explorer selection take as long as Explorer takes to answer, so the <see cref="Pressed"/> handler runs here, on this
/// thread, and hands its result to the UI/wheel by posting. A hotkey with no window is delivered as <c>WM_HOTKEY</c> to the registering thread's queue.
/// </summary>
internal sealed unsafe class HotkeyService : IDisposable
{
    private const uint WM_HOTKEY = 0x0312;
    private const uint WM_APP_APPLY = 0x8001;
    private const uint WM_QUIT = 0x0012;
    private const int HotkeyId = 0x4D53; // "MS"

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly object _gate = new();
    private HotkeySpec? _wanted;
    private uint _threadId;
    private volatile bool _disposed;

    public HotkeyService()
    {
        _thread = new Thread(Run) { Name = "MikeShift hotkey", IsBackground = true };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    /// <summary>Raised on the hotkey thread when the registered shortcut is pressed. Exceptions are logged, never thrown into the loop.</summary>
    public event Action? Pressed;

    /// <summary>Raised on the hotkey thread after each (re)registration.</summary>
    public event Action<HotkeyState>? StateChanged;

    /// <summary>The state after the most recent (re)registration.</summary>
    public HotkeyState State { get; private set; } = HotkeyState.Off;

    /// <summary>Starts the thread. Returns once its message queue exists, so <see cref="Apply"/> can post to it.</summary>
    public void Start()
    {
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    /// <summary>Sets the shortcut (null = none). Registration happens on the hotkey thread; <see cref="StateChanged"/> reports the outcome.</summary>
    public void Apply(HotkeySpec? spec)
    {
        lock (_gate) _wanted = spec;
        if (_threadId != 0 && !_disposed) PInvoke.PostThreadMessage(_threadId, WM_APP_APPLY, default, default);
    }

    private void Run()
    {
        _threadId = PInvoke.GetCurrentThreadId();
        // Force the thread's message queue into existence before anyone posts to it.
        MSG msg;
        PInvoke.PeekMessage(&msg, HWND.Null, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_NOREMOVE);
        _ready.Set();
        var registered = false;
        try
        {
            ApplyWanted(ref registered);
            while (true)
            {
                var result = PInvoke.GetMessage(&msg, HWND.Null, 0, 0);
                if (result.Value <= 0) break; // WM_QUIT (0) or an error (-1)
                try
                {
                    if (msg.message == WM_HOTKEY && (int)msg.wParam.Value == HotkeyId)
                    {
                        WheelLog.Stage("hotkey pressed");
                        Pressed?.Invoke();
                    }
                    else if (msg.message == WM_APP_APPLY)
                    {
                        ApplyWanted(ref registered);
                    }
                    else
                    {
                        PInvoke.TranslateMessage(&msg);
                        PInvoke.DispatchMessage(&msg);
                    }
                }
                catch (Exception ex)
                {
                    WheelLog.Error("the hotkey handler failed", ex);
                }
            }
        }
        finally
        {
            if (registered) PInvoke.UnregisterHotKey(HWND.Null, HotkeyId);
        }
    }

    private void ApplyWanted(ref bool registered)
    {
        HotkeySpec? wanted;
        lock (_gate) wanted = _wanted;
        if (registered)
        {
            PInvoke.UnregisterHotKey(HWND.Null, HotkeyId);
            registered = false;
        }

        var state = HotkeyState.Off;
        if (wanted is { } spec)
        {
            var mods = HOT_KEY_MODIFIERS.MOD_NOREPEAT;
            if ((spec.Modifiers & HotkeyModifiers.Alt) != 0) mods |= HOT_KEY_MODIFIERS.MOD_ALT;
            if ((spec.Modifiers & HotkeyModifiers.Ctrl) != 0) mods |= HOT_KEY_MODIFIERS.MOD_CONTROL;
            if ((spec.Modifiers & HotkeyModifiers.Shift) != 0) mods |= HOT_KEY_MODIFIERS.MOD_SHIFT;
            if ((spec.Modifiers & HotkeyModifiers.Win) != 0) mods |= HOT_KEY_MODIFIERS.MOD_WIN;
            if (PInvoke.RegisterHotKey(HWND.Null, HotkeyId, mods, (uint)spec.VirtualKey))
            {
                registered = true;
                state = HotkeyState.Registered;
                WheelLog.Info($"hotkey {spec} registered");
            }
            else
            {
                state = HotkeyState.Taken;
                WheelLog.Info($"hotkey {spec} could not be registered (win32 error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}): another program probably owns it");
            }
        }
        State = state;
        StateChanged?.Invoke(state);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_threadId != 0) PInvoke.PostThreadMessage(_threadId, WM_QUIT, default, default);
        if (_thread.IsAlive && Environment.CurrentManagedThreadId != _thread.ManagedThreadId) _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }
}
