using System.ComponentModel;
using ComPortWidget.Core.Reset;
using ComPortWidget.Platform;

namespace ComPortWidget.Platform.Tests;

public class ResetLauncherTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cpw-test-" + Guid.NewGuid().ToString("N"));

    public ResetLauncherTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string P(string name) => Path.Combine(_dir, name);

    private sealed class FakeStarter(Func<string, IReadOnlyList<string>, int> onRun) : IProcessStarter
    {
        public List<(string Exe, IReadOnlyList<string> Args)> Calls { get; } = new();
        public List<TimeSpan> Timeouts { get; } = new();
        public int RunElevated(string exe, IReadOnlyList<string> args, TimeSpan timeout)
        {
            Timeouts.Add(timeout);
            Calls.Add((exe, args));
            return onRun(exe, args);
        }
    }

    private static void WriteGoodResult(string path) =>
        ResetFiles.WriteResult(path,
            new ResetResult(false, null, [new ResetItemResult("COM9", ResetItemStatus.Done, "reset.doneRecordRemoved")]),
            false, []);

    [Fact]
    public void Launcher_reports_cancelled_on_error_1223()
    {
        var starter = new FakeStarter((_, _) => throw new Win32Exception(1223));
        Assert.Equal(LaunchOutcome.Cancelled, new ResetLauncher(starter).Run(P("plan.json"), P("result.json")));
    }

    [Fact]
    public void Launcher_reports_failed_on_other_error()
    {
        var starter = new FakeStarter((_, _) => throw new Win32Exception(5));
        Assert.Equal(LaunchOutcome.Failed, new ResetLauncher(starter).Run(P("plan.json"), P("result.json")));
        var starter2 = new FakeStarter((_, _) => throw new InvalidOperationException("x"));
        Assert.Equal(LaunchOutcome.Failed, new ResetLauncher(starter2).Run(P("plan.json"), P("result.json")));
    }

    [Fact]
    public void Launcher_reports_failed_on_nonzero_exit()
    {
        var result = P("result.json");
        var starter = new FakeStarter((_, _) => { WriteGoodResult(result); return 2; });
        Assert.Equal(LaunchOutcome.Failed, new ResetLauncher(starter).Run(P("plan.json"), result));
    }

    [Fact]
    public void Launcher_completes_when_exit_zero_and_result_readable()
    {
        var result = P("result.json");
        var starter = new FakeStarter((_, _) => { WriteGoodResult(result); return 0; });
        Assert.Equal(LaunchOutcome.Completed, new ResetLauncher(starter, @"C:\app\ComPortWidget.exe").Run(P("plan.json"), result));
        // exe를 직접 승격한다. 인자는 계획 파일과 결과 파일뿐이다
        Assert.Equal(@"C:\app\ComPortWidget.exe", starter.Calls[0].Exe);
        Assert.Equal(new[] { "--reset-plan", P("plan.json"), "--reset-result", result }, starter.Calls[0].Args);
    }

    [Fact]
    public void Launcher_passes_dry_run_flag()
    {
        var starter = new FakeStarter((_, _) => 0);
        new ResetLauncher(starter, @"C:\app\ComPortWidget.exe").Run(P("plan.json"), P("result.json"), 0, dryRun: true);
        Assert.Equal(new[] { "--reset-plan", P("plan.json"), "--reset-result", P("result.json"), "--dry-run" }, starter.Calls[0].Args);
    }

    [Fact]
    public void Launcher_fails_when_result_file_is_missing_or_unreadable()
    {
        var result = P("result.json");
        Assert.Equal(LaunchOutcome.Failed, new ResetLauncher(new FakeStarter((_, _) => 0)).Run(P("plan.json"), result));

        var bad = new FakeStarter((_, _) => { File.WriteAllText(result, "{ not json"); return 0; });
        Assert.Equal(LaunchOutcome.Failed, new ResetLauncher(bad).Run(P("plan.json"), result));
    }

    [Fact]
    public void Launcher_ignores_stale_result_file_from_a_previous_run()
    {
        var result = P("result.json");
        WriteGoodResult(result); // 이전 실행이 남긴 파일
        Assert.Equal(LaunchOutcome.Failed, new ResetLauncher(new FakeStarter((_, _) => 0)).Run(P("plan.json"), result));
    }

    [Fact]
    public void Launcher_rejects_relative_paths()
    {
        var starter = new FakeStarter((_, _) => 0);
        Assert.Throws<ArgumentException>(() => new ResetLauncher(starter).Run("plan.json", P("result.json")));
        Assert.Empty(starter.Calls);
    }

    [Fact]
    public void Launcher_waits_120_seconds_plus_70_per_item()
    {
        var starter = new FakeStarter((_, _) => 0);
        new ResetLauncher(starter).Run(P("plan.json"), P("result.json"), 3);
        Assert.Equal(TimeSpan.FromSeconds(120 + 70 * 3), starter.Timeouts[0]);
    }

    [Fact]
    public void Launcher_wait_is_capped_at_30_minutes()
    {
        var starter = new FakeStarter((_, _) => 0);
        new ResetLauncher(starter).Run(P("plan.json"), P("result.json"), 300);
        Assert.Equal(TimeSpan.FromSeconds(1800), starter.Timeouts[0]);
        Assert.Equal(TimeSpan.FromSeconds(1800), ResetLauncher.WaitFor(int.MaxValue));
        Assert.Equal(TimeSpan.FromSeconds(120 + 70 * 23), ResetLauncher.WaitFor(23)); // 상한 직전
    }

    [Fact]
    public void Launcher_reports_failed_on_timeout()
    {
        var starter = new FakeStarter((_, _) => throw new TimeoutException());
        Assert.Equal(LaunchOutcome.Failed, new ResetLauncher(starter).Run(P("plan.json"), P("result.json"), 1));
    }
}
