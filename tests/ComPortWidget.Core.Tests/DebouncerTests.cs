using ComPortWidget.Core.Devices;

namespace ComPortWidget.Core.Tests;

public class DebouncerTests
{
    /// <summary>시간을 수동으로 진행시키는 가짜 타이머.</summary>
    private sealed class FakeTimer : IDelayTimer
    {
        private TimeSpan _remaining;
        private Action? _callback;

        public void Restart(TimeSpan delay, Action callback)
        {
            _remaining = delay;
            _callback = callback;
        }

        public void Advance(TimeSpan span)
        {
            if (_callback is null) return;
            _remaining -= span;
            if (_remaining <= TimeSpan.Zero)
            {
                var cb = _callback;
                _callback = null;
                cb();
            }
        }
    }

    [Fact]
    public void Trigger_runs_action_once_for_burst()
    {
        var timer = new FakeTimer();
        var count = 0;
        var d = new Debouncer(TimeSpan.FromMilliseconds(300), () => count++, timer);

        d.Trigger();
        timer.Advance(TimeSpan.FromMilliseconds(200));
        d.Trigger(); // 지연이 다시 시작된다
        timer.Advance(TimeSpan.FromMilliseconds(200));
        Assert.Equal(0, count);
        d.Trigger();
        timer.Advance(TimeSpan.FromMilliseconds(299));
        Assert.Equal(0, count);
        timer.Advance(TimeSpan.FromMilliseconds(1));
        timer.Advance(TimeSpan.FromMilliseconds(1000));

        Assert.Equal(1, count);
    }

    [Fact]
    public void Trigger_runs_again_after_delay_passes()
    {
        var timer = new FakeTimer();
        var count = 0;
        var d = new Debouncer(TimeSpan.FromMilliseconds(300), () => count++, timer);

        d.Trigger();
        timer.Advance(TimeSpan.FromMilliseconds(300));
        d.Trigger();
        timer.Advance(TimeSpan.FromMilliseconds(300));

        Assert.Equal(2, count);
    }
}
