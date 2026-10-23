using System.Collections.Concurrent;
using MikeShift.Core.Hosting;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MikeShift.App.Wheel;

/// <summary>
/// The dedicated STA thread that owns everything about the wheel (PLAN §3.1 item 3): the window, the OLE drop target (OLE calls it
/// through this thread's message loop), the Win2D renderer, <c>DragSession</c> and <c>WheelModel</c>. It idles in
/// <c>MsgWaitForMultipleObjectsEx</c> with an infinite timeout and only wakes for messages, posted work, or a timer the controller asked for
/// (a frame while animating, the drag session's deadlines, the 50 ms button poll while a drag is active), so a resident app costs no CPU.
/// </summary>
internal sealed unsafe class WheelThread : IDisposable
{
    private const uint WM_QUIT = 0x0012;

    private readonly Thread _thread;
    private readonly ConcurrentQueue<Action> _work = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly Func<WheelThread, WheelController> _factory;
    private volatile bool _stop;
    private Exception? _startError;
    private uint _threadId;

    public WheelThread(Func<WheelThread, WheelController> controllerFactory)
    {
        _factory = controllerFactory;
        _thread = new Thread(Run) { Name = "MikeShift wheel", IsBackground = true };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    /// <summary>The controller; valid after <see cref="Start"/> returned successfully.</summary>
    public WheelController Controller { get; private set; } = null!;

    /// <summary>True once the thread is up and the window exists.</summary>
    public bool IsRunning => _ready.IsSet && _startError is null && !_stop;

    /// <summary>Starts the thread and waits until the window, renderer and drop target exist. Throws if that failed.</summary>
    public void Start()
    {
        _thread.Start();
        _ready.Wait();
        if (_startError is { } error) throw new InvalidOperationException("The wheel could not start: " + error.Message, error);
    }

    /// <summary>Queues <paramref name="work"/> for the wheel thread (any thread may call).</summary>
    public void Post(Action work)
    {
        _work.Enqueue(work);
        try { _wake.Set(); }
        catch (ObjectDisposedException) { /* shutting down */ }
    }

    /// <summary>Wakes the loop without work (new input events, a changed deadline).</summary>
    public void Wake()
    {
        try { _wake.Set(); }
        catch (ObjectDisposedException) { }
    }

    private void Run()
    {
        _threadId = PInvoke.GetCurrentThreadId();
        var oleOk = false;
        try
        {
            var hr = PInvoke.OleInitialize();
            oleOk = hr.Succeeded; // S_FALSE (already initialised) is fine
            Controller = _factory(this);
            Controller.Initialize();
        }
        catch (Exception ex)
        {
            _startError = ex;
            WheelLog.Error("wheel thread failed to start", ex);
            _ready.Set();
            if (oleOk) PInvoke.OleUninitialize();
            return;
        }
        _ready.Set();

        // After start-up has been reported (the UI thread is not kept waiting): throw-away frame to pay one-off rendering costs.
        Controller.Warmup();

        var handle = new HANDLE(_wake.SafeWaitHandle.DangerousGetHandle());
        var backoff = new FailureBackoff();
        while (!_stop)
        {
            try
            {
                var timeout = Controller.NextTimeoutMs();
                PInvoke.MsgWaitForMultipleObjectsEx(1, &handle, timeout, QUEUE_STATUS_FLAGS.QS_ALLINPUT,
                    MSG_WAIT_FOR_MULTIPLE_OBJECTS_EX_FLAGS.MWMO_INPUTAVAILABLE);

                while (_work.TryDequeue(out var work))
                {
                    try { work(); }
                    catch (Exception ex) { WheelLog.Error("posted wheel work failed", ex); }
                }

                MSG msg;
                while (PInvoke.PeekMessage(&msg, HWND.Null, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_REMOVE))
                {
                    if (msg.message == WM_QUIT) { _stop = true; break; }
                    PInvoke.TranslateMessage(&msg);
                    PInvoke.DispatchMessage(&msg);
                }

                Controller.Pump();
                backoff.Reset();
            }
            catch (Exception ex)
            {
                // The loop must survive anything: log and carry on. A failure that repeats on every iteration (a timer that stays due
                // because the work that would clear it keeps throwing) would otherwise spin a core and fill the log: wait a little,
                // longer each time (50 ms up to 1 s).
                WheelLog.Error("wheel loop iteration failed", ex);
                Thread.Sleep(backoff.NextDelay());
            }
        }

        try { Controller.Dispose(); }
        catch (Exception ex) { WheelLog.Error("wheel shutdown failed", ex); }
        if (oleOk) PInvoke.OleUninitialize();
    }

    public void Dispose()
    {
        if (_stop && !_thread.IsAlive) return;
        _stop = true;
        if (_threadId != 0) PInvoke.PostThreadMessage(_threadId, WM_QUIT, default, default);
        Wake();
        if (_thread.IsAlive && Environment.CurrentManagedThreadId != _thread.ManagedThreadId)
            _thread.Join(TimeSpan.FromSeconds(2));
        _wake.Dispose();
    }
}
