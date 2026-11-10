using Microsoft.UI.Dispatching;

namespace MedRecPlus.App.Security;

public sealed class AutoLockService
{
    private readonly DispatcherQueueTimer _timer;
    private readonly TimeSpan _timeout;
    private DateTimeOffset _lastActivity;
    private bool _running;
    public event EventHandler? AutoLocked;

    public AutoLockService(TimeSpan timeout)
    {
        _timeout = timeout;
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) =>
        {
            if (!_running) return;
            var remaining = _timeout - (DateTimeOffset.UtcNow - _lastActivity);
            if (remaining > TimeSpan.Zero) { Arm(remaining); return; }
            Stop();
            AutoLocked?.Invoke(this, EventArgs.Empty);
        };
    }

    public void Start()
    {
        Stop();
        if (_timeout <= TimeSpan.Zero) return;
        _lastActivity = default;
        _running = true;
        NotifyActivity();
    }

    public void Stop() { _running = false; _timer.Stop(); }

    public void NotifyActivity()
    {
        if (!_running) return;
        // An input queued on resume must not reset an already elapsed deadline.
        if (_lastActivity != default && DateTimeOffset.UtcNow - _lastActivity >= _timeout)
        {
            Stop();
            AutoLocked?.Invoke(this, EventArgs.Empty);
            return;
        }
        _lastActivity = DateTimeOffset.UtcNow;
        Arm(_timeout);
    }

    private void Arm(TimeSpan interval)
    {
        _timer.Stop();
        _timer.Interval = interval;
        _timer.Start();
    }
}
