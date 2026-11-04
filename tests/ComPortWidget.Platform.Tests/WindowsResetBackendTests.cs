using ComPortWidget.Core.Devices;
using ComPortWidget.Core.Reset;
using ComPortWidget.Platform;

namespace ComPortWidget.Platform.Tests;

public class WindowsResetBackendTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cpw-test-" + Guid.NewGuid().ToString("N"));
    public WindowsResetBackendTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private sealed class FakeRunner(int exit = 0, string output = "") : IProcessRunner
    {
        public List<(string Exe, string[] Args)> Calls { get; } = new();
        public ProcessRunResult Run(string exe, IReadOnlyList<string> args)
        {
            Calls.Add((exe, args.ToArray()));
            return new ProcessRunResult(exit, output);
        }
    }

    private sealed class FakeComDb(int openRc = 0, int releaseRc = 0) : IComDbNative
    {
        public List<string> Log { get; } = new();
        public int Open(out nint handle) { Log.Add("open"); handle = openRc == 0 ? 77 : 0; return openRc; }
        public int ReleasePort(nint handle, int number) { Log.Add($"release:{handle}:{number}"); return releaseRc; }
        public int Close(nint handle) { Log.Add($"close:{handle}"); return 0; }
    }

    private static DeviceRecord Rec(string id, string port, bool present) =>
        new(id, port, present, null, null, null, null, Array.Empty<string>());

    private WindowsResetBackend Make(bool dry, IProcessRunner runner, IComDbNative comDb,
        Func<IReadOnlyList<DeviceRecord>>? records = null, TimeSpan? comDbTimeout = null) =>
        new(_dir, dry, records ?? (() => new[] { Rec("USB\\VID_1&PID_2\\FAKE1", "COM5", false) }),
            () => new byte[] { 0x10 }, () => new HashSet<int> { 5 }, runner, comDb,
            () => new DateTime(2026, 1, 2, 3, 4, 5), comDbTimeout);

    [Fact]
    public void DryRun_backend_never_changes_system()
    {
        var runner = new FakeRunner();
        var db = new FakeComDb();
        var b = Make(true, runner, db);
        b.RemoveDevice("USB\\VID_1&PID_2\\FAKE1");
        b.ReleasePort(5);
        Assert.Empty(runner.Calls);
        Assert.Empty(db.Log);
        Assert.Equal(2, b.Actions.Count);
        Assert.Contains("USB\\VID_1&PID_2\\FAKE1", b.Actions[0]);
        Assert.Contains("COM5", b.Actions[1]);
    }

    [Fact]
    public void RemoveDevice_passes_exactly_two_arguments_to_pnputil()
    {
        var runner = new FakeRunner();
        Make(false, runner, new FakeComDb()).RemoveDevice("USB\\VID_1&PID_2\\FAKE1");
        var call = Assert.Single(runner.Calls);
        Assert.EndsWith("pnputil.exe", call.Exe, StringComparison.OrdinalIgnoreCase);
        Assert.True(Path.IsPathFullyQualified(call.Exe));
        Assert.Equal(new[] { "/remove-device", "USB\\VID_1&PID_2\\FAKE1" }, call.Args);
    }

    [Fact]
    public void RemoveDevice_throws_with_output_on_nonzero_exit()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Make(false, new FakeRunner(5, "access denied text"), new FakeComDb()).RemoveDevice("X\\Y\\Z"));
        Assert.Contains("access denied text", ex.Message);
        Assert.Contains("5", ex.Message);
    }

    [Fact]
    public void ReleasePort_opens_releases_and_closes_each_call()
    {
        var db = new FakeComDb();
        var b = Make(false, new FakeRunner(), db);
        b.ReleasePort(5);
        b.ReleasePort(6);
        Assert.Equal(new[] { "open", "release:77:5", "close:77", "open", "release:77:6", "close:77" }, db.Log);
    }

    [Fact]
    public void ReleasePort_closes_and_throws_when_release_fails()
    {
        var db = new FakeComDb(releaseRc: 2);
        var b = Make(false, new FakeRunner(), db);
        Assert.Throws<InvalidOperationException>(() => b.ReleasePort(5));
        Assert.Equal(new[] { "open", "release:77:5", "close:77" }, db.Log);
    }

    [Fact]
    public void ReleasePort_throws_when_open_fails_without_close()
    {
        var db = new FakeComDb(openRc: 5);
        Assert.Throws<InvalidOperationException>(() => Make(false, new FakeRunner(), db).ReleasePort(5));
        Assert.Equal(new[] { "open" }, db.Log);
    }

    [Fact]
    public void IsPresent_reads_the_current_state()
    {
        var b = Make(false, new FakeRunner(), new FakeComDb(),
            () => new[] { Rec("A\\B\\C", "COM3", true), Rec("A\\B\\D", "COM4", false) });
        Assert.True(b.IsPresent("a\\b\\c"));
        Assert.False(b.IsPresent("A\\B\\D"));
    }

    [Fact]
    public void IsPresent_throws_when_device_not_found()
    {
        var b = Make(false, new FakeRunner(), new FakeComDb(), () => new[] { Rec("A\\B\\C", "COM3", true) });
        Assert.Throws<InvalidOperationException>(() => b.IsPresent("A\\B\\GONE"));
    }

    [Fact]
    public void IsPresent_throws_when_lookup_fails()
    {
        var b = Make(false, new FakeRunner(), new FakeComDb(), () => throw new IOException("read failed"));
        Assert.Throws<IOException>(() => b.IsPresent("A\\B\\C"));
    }

    [Fact]
    public void WriteBackup_writes_timestamped_file_with_comdb_and_targets()
    {
        var b = Make(true, new FakeRunner(), new FakeComDb());
        b.WriteBackup(new ResetPlan(["A\\B\\C"], [9]), [Rec("A\\B\\C", "COM3", false)]);
        var file = Assert.Single(Directory.GetFiles(_dir));
        Assert.Equal("backup-20260102-030405.json", Path.GetFileName(file));
        var text = File.ReadAllText(file);
        Assert.Contains(Convert.ToBase64String(new byte[] { 0x10 }), text);
        Assert.Contains("A\\\\B\\\\C", text);
        // 같은 초에 한 번 더 써도 덮어쓰지 않는다
        b.WriteBackup(new ResetPlan([], []), []);
        Assert.Equal(2, Directory.GetFiles(_dir).Length);
    }

    [Fact]
    public void GetReservedPorts_converts_comdb_bytes()
    {
        Assert.Equal(new[] { 5 }, Make(false, new FakeRunner(), new FakeComDb()).GetReservedPorts().OrderBy(x => x));
    }

    [Fact]
    public void RemoveDevice_returns_true_for_reboot_required_exit_3010_and_false_for_zero()
    {
        Assert.True(Make(false, new FakeRunner(3010, "reboot"), new FakeComDb()).RemoveDevice("X\\Y\\Z"));
        Assert.False(Make(false, new FakeRunner(0), new FakeComDb()).RemoveDevice("X\\Y\\Z"));
        Assert.False(Make(true, new FakeRunner(0), new FakeComDb()).RemoveDevice("X\\Y\\Z"));
    }

    private sealed class HangingComDb : IComDbNative
    {
        public ManualResetEventSlim Release { get; } = new();
        public int Opens;
        public int Open(out nint handle) { Interlocked.Increment(ref Opens); Release.Wait(); handle = 1; return 0; }
        public int ReleasePort(nint handle, int number) => 0;
        public int Close(nint handle) => 0;
    }

    [Fact]
    public void ReleasePort_times_out_when_ComDB_hangs_and_then_fails_fast()
    {
        var db = new HangingComDb();
        try
        {
            var b = Make(false, new FakeRunner(), db, comDbTimeout: TimeSpan.FromMilliseconds(200));
            Assert.Throws<TimeoutException>(() => b.ReleasePort(5));
            Assert.Throws<TimeoutException>(() => b.ReleasePort(6));
            Assert.Equal(1, db.Opens); // 두 번째 호출은 다시 기다리지 않는다
        }
        finally { db.Release.Set(); }
    }

    private sealed class GatedComDb : IComDbNative
    {
        public ManualResetEventSlim OpenGate { get; } = new();
        public ManualResetEventSlim Closed { get; } = new();
        public List<string> Log { get; } = new();
        public int Open(out nint handle) { lock (Log) Log.Add("open"); OpenGate.Wait(); handle = 9; return 0; }
        public int ReleasePort(nint handle, int number) { lock (Log) Log.Add("release"); return 0; }
        public int Close(nint handle) { lock (Log) Log.Add("close"); Closed.Set(); return 0; }
    }

    [Fact]
    public void Abandoned_ComDB_call_never_releases_after_the_timeout()
    {
        var db = new GatedComDb();
        var b = Make(false, new FakeRunner(), db, comDbTimeout: TimeSpan.FromMilliseconds(150));
        Assert.Throws<TimeoutException>(() => b.ReleasePort(5));
        db.OpenGate.Set(); // 포기한 뒤에 Open이 풀린다
        Assert.True(db.Closed.Wait(5000), "worker did not close the handle");
        lock (db.Log) Assert.Equal(new[] { "open", "close" }, db.Log);
    }
}
