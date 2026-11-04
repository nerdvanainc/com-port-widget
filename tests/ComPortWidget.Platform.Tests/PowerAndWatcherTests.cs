using System.Diagnostics;
using ComPortWidget.Platform;

namespace ComPortWidget.Platform.Tests;

public class PowerAndWatcherTests
{
    [Fact]
    public void GetBootTime_is_within_a_minute_of_system_boot()
    {
        // 읽기 전용 CIM 조회로 실제 부팅 시각을 얻어 비교한다
        var psi = new ProcessStartInfo("powershell.exe",
            "-NoProfile -Command \"(Get-CimInstance Win32_OperatingSystem).LastBootUpTime.ToUniversalTime().ToString('o')\"")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        var text = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit();
        Assert.True(DateTimeOffset.TryParse(text, out var cim), $"CIM output not parseable: '{text}'");

        var boot = PowerBaseline.GetBootTime();
        Assert.True(Math.Abs((boot - cim).TotalSeconds) < 60, $"boot={boot:o} cim={cim:o}");
    }

    [Fact]
    public void GetLastResumeTime_returns_past_time_or_null()
    {
        var sw = Stopwatch.StartNew();
        var t = PowerBaseline.GetLastResumeTime();
        sw.Stop();
        if (t is not null) Assert.True(t <= DateTimeOffset.Now);
        Assert.True(sw.ElapsedMilliseconds < 2000, $"took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void DeviceWatcher_start_twice_and_dispose_on_sta_thread_does_not_throw()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var w = new DeviceWatcher();
                w.Start();
                w.Start();
                w.Dispose();
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(10000));
        Assert.Null(error);
    }

    private static readonly DateTimeOffset Now = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Created = Now.AddMinutes(-10);

    [Fact]
    public void ResolveWakeTime_uses_valid_wake_time()
    {
        var wake = Created.AddSeconds(-5);
        Assert.Equal(wake, PowerBaseline.ResolveWakeTime(wake.UtcDateTime, Created, Now));
        Assert.Equal(wake, PowerBaseline.ResolveWakeTime(wake.ToFileTime(), Created, Now));
    }

    [Fact]
    public void ResolveWakeTime_falls_back_on_zero()
    {
        Assert.Equal(Created, PowerBaseline.ResolveWakeTime(0L, Created, Now));
        Assert.Equal(Created, PowerBaseline.ResolveWakeTime(DateTime.FromFileTimeUtc(0), Created, Now));
    }

    [Fact]
    public void ResolveWakeTime_falls_back_on_future_or_later_than_created()
    {
        Assert.Equal(Created, PowerBaseline.ResolveWakeTime(Now.AddHours(1).UtcDateTime, Created, Now));
        Assert.Equal(Created, PowerBaseline.ResolveWakeTime(Created.AddSeconds(1).UtcDateTime, Created, Now));
    }

    [Fact]
    public void ResolveWakeTime_falls_back_when_property_missing()
    {
        Assert.Equal(Created, PowerBaseline.ResolveWakeTime(null, Created, Now));
        Assert.Equal(Created, PowerBaseline.ResolveWakeTime("garbage", Created, Now));
    }

    private sealed class QueueContext : SynchronizationContext
    {
        public readonly Queue<Action> Posted = new();
        public override void Post(SendOrPostCallback d, object? state) => Posted.Enqueue(() => d(state));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessageW(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private static void RunSta(Action body)
    {
        Exception? error = null;
        var t = new Thread(() => { try { body(); } catch (Exception ex) { error = ex; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        Assert.True(t.Join(15000));
        Assert.Null(error);
    }

    [Fact]
    public void DeviceWatcher_raises_events_through_captured_context_and_not_after_dispose()
    {
        RunSta(() =>
        {
            var ctx = new QueueContext();
            SynchronizationContext.SetSynchronizationContext(ctx);
            var changed = 0;
            var resumed = 0;
            var w = new DeviceWatcher();
            w.Changed += () => changed++;
            w.PowerResumed += () => resumed++;
            w.Start();

            // WM_DEVICECHANGE -> 300 ms 디바운스 -> 문맥으로 Post
            SendMessageW(w.WindowHandle, 0x0219, IntPtr.Zero, IntPtr.Zero);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (ctx.Posted.Count == 0 && sw.ElapsedMilliseconds < 3000)
            {
                System.Windows.Forms.Application.DoEvents();
                Thread.Sleep(10);
            }
            Assert.Equal(0, changed); // 문맥이 실행하기 전에는 올라가지 않는다
            Assert.True(ctx.Posted.Count >= 1);
            ctx.Posted.Dequeue()();
            Assert.Equal(1, changed);

            // 재개 알림을 Post한 뒤 Dispose하면 실행해도 이벤트가 오르지 않는다
            SendMessageW(w.WindowHandle, 0x0218, (IntPtr)0x12, IntPtr.Zero);
            Assert.Single(ctx.Posted);
            w.Dispose();
            ctx.Posted.Dequeue()();
            Assert.Equal(0, resumed);
            Assert.Equal(1, changed);
        });
    }
}
