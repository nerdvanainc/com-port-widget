using ComPortWidget.Core.Devices;
using ComPortWidget.Core.Reset;
using ComPortWidget.Platform;

namespace ComPortWidget.Platform.Tests;

public class ResetRunnerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cpw-test-" + Guid.NewGuid().ToString("N"));
    public ResetRunnerTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }
    private string P(string n) => Path.Combine(_dir, n);

    private sealed class FakeBackend : IResetBackend, IDryRunLog
    {
        public List<string> Removed { get; } = new();
        public IReadOnlyList<string> Actions => Removed;
        public IReadOnlyList<DeviceRecord> GetRecords() =>
            [new DeviceRecord("USB\\VID_1&PID_2\\FAKE1", "COM5", false, null, null, null, null, Array.Empty<string>())];
        public ISet<int> GetReservedPorts() => new HashSet<int> { 5 };
        public ISet<int> GetOtherHolders() => new HashSet<int>();
        public void WriteBackup(ResetPlan plan, IReadOnlyList<DeviceRecord> targets) { }
        public bool IsPresent(string instanceId) => false;
        public bool RemoveDevice(string instanceId) { Removed.Add("remove " + instanceId); return false; }
        public void ReleasePort(int number) => Removed.Add("release COM" + number);
    }

    private sealed class ThrowingBackend : IResetBackend
    {
        public IReadOnlyList<DeviceRecord> GetRecords() => throw new IOException("boom");
        public ISet<int> GetReservedPorts() => new HashSet<int>();
        public ISet<int> GetOtherHolders() => new HashSet<int>();
        public void WriteBackup(ResetPlan plan, IReadOnlyList<DeviceRecord> targets) { }
        public bool IsPresent(string instanceId) => false;
        public bool RemoveDevice(string instanceId) => false;
        public void ReleasePort(int number) { }
    }

    private static readonly Func<string, bool, IResetBackend> Fake = (_, _) => new FakeBackend();

    [Fact]
    public void Missing_plan_gives_abort_result_and_nonzero_exit()
    {
        var code = ResetRunner.Run(["--reset-plan", P("reset-plan.json"), "--reset-result", P("reset-result.json"), "--dry-run"], Fake);
        Assert.NotEqual(0, code);
        var r = ResetFiles.ReadResult(P("reset-result.json"))!;
        Assert.True(r.Aborted);
        Assert.Equal("reset.abortPlanInvalid", r.AbortReason);
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("[]")]
    [InlineData("{\"InstanceIds\":[\"A\"]}")]
    [InlineData("{\"ReservedOnlyPorts\":[1]}")]
    [InlineData("{\"InstanceIds\":null,\"ReservedOnlyPorts\":[]}")]
    [InlineData("{\"InstanceIds\":[1],\"ReservedOnlyPorts\":[]}")]
    [InlineData("{\"InstanceIds\":[],\"ReservedOnlyPorts\":[\"x\"]}")]
    public void Malformed_plan_gives_abort_result_and_changes_nothing(string json)
    {
        File.WriteAllText(P("reset-plan.json"), json);
        var backend = new FakeBackend();
        var code = ResetRunner.Run(["--reset-plan", P("reset-plan.json"), "--reset-result", P("reset-result.json")], (_, _) => backend);
        Assert.NotEqual(0, code);
        Assert.True(ResetFiles.ReadResult(P("reset-result.json"))!.Aborted);
        Assert.Empty(backend.Removed);
    }

    [Fact]
    public void Relative_plan_path_is_rejected_without_writing()
    {
        var code = ResetRunner.Run(["--reset-plan", "reset-plan.json", "--reset-result", P("reset-result.json")], Fake);
        Assert.Equal(ResetRunner.ExitBadArgs, code);
        Assert.False(File.Exists(P("reset-result.json")));
    }

    [Fact]
    public void Relative_or_missing_result_path_exits_nonzero_without_writing()
    {
        File.WriteAllText(P("reset-plan.json"), "{\"InstanceIds\":[],\"ReservedOnlyPorts\":[]}");
        Assert.NotEqual(0, ResetRunner.Run(["--reset-plan", P("reset-plan.json"), "--reset-result", "reset-result.json"], Fake));
        Assert.NotEqual(0, ResetRunner.Run(["--reset-plan", P("reset-plan.json")], Fake));
        Assert.NotEqual(0, ResetRunner.Run(["--reset-plan", P("reset-plan.json"), "--reset-result"], Fake));
        Assert.False(File.Exists("r.json"));
    }

    [Fact]
    public void Unwritable_result_exits_nonzero()
    {
        File.WriteAllText(P("reset-plan.json"), "{\"InstanceIds\":[],\"ReservedOnlyPorts\":[]}");
        Directory.CreateDirectory(P("reset-result.json")); // 같은 이름의 폴더가 있어 파일을 쓸 수 없다
        Assert.NotEqual(0, ResetRunner.Run(["--reset-plan", P("reset-plan.json"), "--reset-result", P("reset-result.json")], Fake));
    }

    [Fact]
    public void Valid_plan_runs_executor_and_writes_result_with_dry_run_actions()
    {
        ResetFiles.WritePlan(P("reset-plan.json"), new ResetPlan(["USB\\VID_1&PID_2\\FAKE1"], []));
        var backend = new FakeBackend();
        var code = ResetRunner.Run(["--reset-plan", P("reset-plan.json"), "--reset-result", P("reset-result.json"), "--dry-run"], (dir, dry) =>
        {
            Assert.True(dry);
            Assert.Equal(_dir, dir); // 백업은 계획 파일과 같은 폴더
            return backend;
        });
        Assert.Equal(0, code);
        var r = ResetFiles.ReadResult(P("reset-result.json"))!;
        Assert.False(r.Aborted);
        Assert.True(r.DryRun);
        Assert.Equal(ResetItemStatus.Done, Assert.Single(r.Items).Status);
        Assert.Equal(2, r.Actions.Count);
    }

    [Fact]
    public void Executor_abort_gives_nonzero_exit_but_keeps_result()
    {
        ResetFiles.WritePlan(P("reset-plan.json"), new ResetPlan([], []));
        var code = ResetRunner.Run(["--reset-plan", P("reset-plan.json"), "--reset-result", P("reset-result.json")], (_, _) => new ThrowingBackend());
        Assert.NotEqual(0, code);
        Assert.True(ResetFiles.ReadResult(P("reset-result.json"))!.Aborted);
    }

    [Fact]
    public void IsResetMode_detects_the_switch()
    {
        Assert.True(ResetRunner.IsResetMode(["--reset-plan", "x"]));
        Assert.False(ResetRunner.IsResetMode(["--demo"]));
    }

    [Theory]
    [InlineData("--dryrun")]
    [InlineData("--demo")]
    [InlineData("stray")]
    public void Unknown_arguments_are_rejected_before_anything_runs(string extra)
    {
        ResetFiles.WritePlan(P("reset-plan.json"), new ResetPlan(["USB\\VID_1&PID_2\\FAKE1"], []));
        var backend = new FakeBackend();
        var code = ResetRunner.Run(["--reset-plan", P("reset-plan.json"), "--reset-result", P("reset-result.json"), extra], (_, _) => backend);
        Assert.Equal(ResetRunner.ExitBadArgs, code);
        Assert.Empty(backend.Removed);
        Assert.False(File.Exists(P("reset-result.json")));
    }

    [Fact]
    public void Duplicate_flags_are_rejected()
    {
        ResetFiles.WritePlan(P("reset-plan.json"), new ResetPlan([], []));
        var code = ResetRunner.Run(["--reset-plan", P("reset-plan.json"), "--reset-result", P("reset-result.json"), "--dry-run", "--dry-run"], Fake);
        Assert.Equal(ResetRunner.ExitBadArgs, code);
    }

    [Fact]
    public void Other_file_names_are_rejected_without_writing_anything()
    {
        File.WriteAllText(P("other.json"), "{\"InstanceIds\":[],\"ReservedOnlyPorts\":[]}");
        Assert.Equal(ResetRunner.ExitBadArgs,
            ResetRunner.Run(["--reset-plan", P("other.json"), "--reset-result", P("reset-result.json")], Fake));
        Assert.Equal(ResetRunner.ExitBadArgs,
            ResetRunner.Run(["--reset-plan", P("reset-plan.json"), "--reset-result", P("out.json")], Fake));
        Assert.False(File.Exists(P("reset-result.json")));
        Assert.False(File.Exists(P("out.json")));
    }

    [Fact]
    public void Folder_behind_a_junction_is_rejected_without_writing_anything()
    {
        var real = P("real");
        Directory.CreateDirectory(real);
        ResetFiles.WritePlan(Path.Combine(real, "reset-plan.json"), new ResetPlan([], []));
        var link = P("link");
        Assert.True(ResetPathsTests.TryMakeJunction(link, real), "junction could not be created");
        try
        {
            var code = ResetRunner.Run(["--reset-plan", Path.Combine(link, "reset-plan.json"),
                "--reset-result", Path.Combine(link, "reset-result.json")], Fake);
            Assert.Equal(ResetRunner.ExitBadArgs, code);
            Assert.Empty(Directory.GetFiles(real, "reset-result*"));
            Assert.Single(Directory.GetFiles(real)); // 계획 파일만 있다. 백업도 만들지 않았다
        }
        finally { Directory.Delete(link); }
    }
}
