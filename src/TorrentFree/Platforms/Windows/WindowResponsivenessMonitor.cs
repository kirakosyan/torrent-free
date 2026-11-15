using System.Diagnostics;
using Microsoft.UI.Dispatching;
using TorrentFree.Services;

namespace TorrentFree.WinUI;

/// <summary>Records a stalled UI dispatcher once, plus recovery, without blocking either thread.</summary>
internal sealed class WindowResponsivenessMonitor : IDisposable
{
    private readonly DispatcherQueue _dispatcher;
    private readonly Timer _timer;
    private long _lastResponse = Stopwatch.GetTimestamp();
    private long _lastCheck = Stopwatch.GetTimestamp();
    private int _pending;
    private int _checking;
    private bool _reported;
    private volatile bool _disposed;

    public WindowResponsivenessMonitor(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        _timer = new Timer(Check, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    private void Check(object? _)
    {
        if (_disposed || Interlocked.Exchange(ref _checking, 1) != 0) return;
        try { CheckDispatcher(); }
        catch (Exception ex) { AppDiagnostics.Record("Windows.ResponsivenessMonitor", ex); }
        finally { Interlocked.Exchange(ref _checking, 0); }
    }

    private void CheckDispatcher()
    {
        var now = Stopwatch.GetTimestamp();
        // A suspended computer/process also stops timer callbacks; that is not a UI hang.
        if (Stopwatch.GetElapsedTime(_lastCheck, now) > TimeSpan.FromSeconds(15))
            Interlocked.Exchange(ref _lastResponse, now);
        _lastCheck = now;
        var elapsed = Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastResponse));
        if (elapsed >= TimeSpan.FromSeconds(30) && !_reported)
        {
            _reported = true;
            AppDiagnostics.Record("Windows.UIUnresponsive", details: $"No dispatcher response for {elapsed.TotalSeconds:F0}s.");
        }
        else if (elapsed < TimeSpan.FromSeconds(10) && _reported)
        {
            _reported = false;
            AppDiagnostics.Record("Windows.UIResponsive");
        }
        if (Interlocked.Exchange(ref _pending, 1) != 0) return;
        if (!_dispatcher.TryEnqueue(() =>
        {
            Interlocked.Exchange(ref _lastResponse, Stopwatch.GetTimestamp());
            Interlocked.Exchange(ref _pending, 0);
        })) Interlocked.Exchange(ref _pending, 0);
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }
}
