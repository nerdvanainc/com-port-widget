using System.IO;
using ComPortWidget.App;

namespace ComPortWidget.App.Tests;

public class CrashLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cpw-crash-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void Write_appends_source_and_exception_text()
    {
        var path = Path.Combine(_dir, "sub", "crash.log");
        var now = new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);
        Assert.True(CrashLog.Write(path, "TestSource", new InvalidOperationException("boom"), now));
        Assert.True(CrashLog.Write(path, "Second", null, now));
        var text = File.ReadAllText(path);
        Assert.Contains("TestSource", text);
        Assert.Contains("boom", text);
        Assert.Contains("Second", text);
    }

    [Fact]
    public void Write_clears_an_oversized_log_first()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "crash.log");
        File.WriteAllBytes(path, new byte[CrashLog.MaxBytes + 1]);
        Assert.True(CrashLog.Write(path, "New", new Exception("x"), DateTimeOffset.Now));
        Assert.True(new FileInfo(path).Length < CrashLog.MaxBytes);
    }

    [Fact]
    public void Write_returns_false_instead_of_throwing_when_the_path_is_unusable()
    {
        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, "blocker");
        File.WriteAllText(file, "x");
        Assert.False(CrashLog.Write(Path.Combine(file, "crash.log"), "S", null, DateTimeOffset.Now));
    }

    [Fact]
    public void Exception_before_startup_finished_is_not_kept_alive()
        => Assert.False(CrashLog.ShouldKeepAlive(runtimeStarted: false, handledCount: 1));

    [Fact]
    public void Exception_after_startup_is_kept_alive_until_the_limit()
    {
        Assert.True(CrashLog.ShouldKeepAlive(true, 1));
        Assert.True(CrashLog.ShouldKeepAlive(true, CrashLog.MaxKeepAlive));
        Assert.False(CrashLog.ShouldKeepAlive(true, CrashLog.MaxKeepAlive + 1));
    }
}
