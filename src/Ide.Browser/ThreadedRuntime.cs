// Avalonia 11.3 on the multithreaded .NET 8 WebAssembly runtime.
//
// The compilers and their runtimes need threads (WasmEnableThreads). When it sees a multithreaded runtime,
// Avalonia.Browser 11.3 moves its rendering and its dispatcher to web workers through private APIs of the .NET 9
// runtime, which do not exist in .NET 8 (it fails at start-up). The browser head therefore keeps Avalonia in its
// single-threaded mode (user interface on the main browser thread), and replaces its dispatcher by one that
// accepts posts from the other threads: in that mode the dispatcher is woken up by a JavaScript timer, which
// cannot be called from a worker thread, so a post from a worker only raises a flag, polled by the UI thread.

using System.Reflection;
using Avalonia;
using Avalonia.Threading;

namespace Studio.Ide.Browser;

static class ThreadedRuntime
{
    /// <summary>Makes Avalonia.Browser use its single-threaded mode (call before anything of Avalonia starts).</summary>
    public static void KeepAvaloniaSingleThreaded()
    {
        var platform = Type.GetType("Avalonia.Browser.BrowserWindowingPlatform, Avalonia.Browser", throwOnError: true)!;
        var field = platform.GetField("IsThreadingEnabled", BindingFlags.NonPublic | BindingFlags.Static)
                    ?? throw new MissingFieldException("Avalonia.Browser.BrowserWindowingPlatform.IsThreadingEnabled (Avalonia version changed?)");
        field.SetValue(null, false);
        if ((bool)field.GetValue(null)!) throw new InvalidOperationException("Avalonia.Browser threading mode could not be disabled.");
    }

    /// <summary>Replaces the dispatcher of the platform by <see cref="PollingDispatcher"/> (AfterPlatformServicesSetup).</summary>
    public static void InstallDispatcher()
    {
        var inner = AvaloniaLocator.Current.GetService<IDispatcherImpl>()
                    ?? throw new InvalidOperationException("No dispatcher registered by Avalonia.Browser.");
        AvaloniaLocator.CurrentMutable.Bind<IDispatcherImpl>().ToConstant(new PollingDispatcher(inner));
    }
}

/// <summary>
/// Dispatcher of the UI thread that can be used from any thread. On the UI thread it delegates to the
/// dispatcher of Avalonia.Browser; from another thread, a signal sets a flag and a new due time is only
/// recorded (Avalonia calls UpdateTimer on the posting thread for background priorities): a timer of the UI
/// thread checks them every 10 ms while the other threads are active (every 50 ms after 2 s without a signal).
/// </summary>
sealed class PollingDispatcher : IDispatcherImpl
{
    const long FastPoll = 10, SlowPoll = 50, FastPeriod = 2000;

    readonly IDispatcherImpl _inner;
    readonly object _gate = new();
    long? _due;
    long _armed;
    long _lastSignal = -FastPeriod;
    int _signaled;

    public PollingDispatcher(IDispatcherImpl inner)
    {
        _inner = inner;
        _inner.Signaled += () => Signaled?.Invoke();
        _inner.Timer += OnTick;
        Arm();
    }

    public event Action? Signaled;
    public event Action? Timer;

    public bool CurrentThreadIsLoopThread => _inner.CurrentThreadIsLoopThread;
    public long Now => _inner.Now;

    public void Signal()
    {
        if (_inner.CurrentThreadIsLoopThread) _inner.Signal();
        else Interlocked.Exchange(ref _signaled, 1);
    }

    public void UpdateTimer(long? dueTimeInMs)
    {
        lock (_gate) _due = dueTimeInMs;
        if (_inner.CurrentThreadIsLoopThread) Arm();
        else Interlocked.Exchange(ref _signaled, 1);
    }

    void OnTick()
    {
        if (Interlocked.Exchange(ref _signaled, 0) != 0)
        {
            _lastSignal = Now;
            Signaled?.Invoke();
        }
        bool timer = false;
        lock (_gate)
        {
            if (_due is { } due && Now >= due)
            {
                _due = null;
                timer = true;
            }
        }
        if (timer) Timer?.Invoke();
        Arm();
    }

    /// <summary>The timer of the inner dispatcher repeats: it is changed only when its period changes.</summary>
    void Arm()
    {
        long now = Now;
        long poll = now - _lastSignal < FastPeriod ? FastPoll : SlowPoll;
        long? next;
        lock (_gate) next = _due;
        long wait = next is { } due ? Math.Clamp(due - now, 1, poll) : poll;
        if (wait == _armed) return;
        _armed = wait;
        _inner.UpdateTimer(now + wait);
    }
}
