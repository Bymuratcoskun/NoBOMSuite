using System;
using System.Threading.Tasks;

namespace NoBOMSuite.Desktop;

public static class Dispatcher
{
    public static class UIThread
    {
        public static void Post(Action action)
        {
            GLib.Functions.IdleAdd(0, () =>
            {
                action();
                return false;
            });
        }

        public static Task InvokeAsync(Action action)
        {
            var tcs = new TaskCompletionSource();
            GLib.Functions.IdleAdd(0, () =>
            {
                try
                {
                    action();
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
                return false;
            });
            return tcs.Task;
        }

        public static Task<T> InvokeAsync<T>(Func<T> func)
        {
            var tcs = new TaskCompletionSource<T>();
            GLib.Functions.IdleAdd(0, () =>
            {
                try
                {
                    var result = func();
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
                return false;
            });
            return tcs.Task;
        }
    }
}

public class DispatcherTimer
{
    private uint _timeoutId;
    public TimeSpan Interval { get; set; }
    public event EventHandler? Tick;

    public void Start()
    {
        Stop();
        _timeoutId = GLib.Functions.TimeoutAdd(0, (uint)Interval.TotalMilliseconds, () =>
        {
            Tick?.Invoke(this, EventArgs.Empty);
            return true; // Keep running
        });
    }

    public void Stop()
    {
        if (_timeoutId != 0)
        {
            GLib.Functions.SourceRemove(_timeoutId);
            _timeoutId = 0;
        }
    }
}
