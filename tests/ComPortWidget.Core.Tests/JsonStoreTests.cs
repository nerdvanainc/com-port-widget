using ComPortWidget.Core.Listing;
using ComPortWidget.Core.Storage;

namespace ComPortWidget.Core.Tests;

public class JsonStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cpw-test-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "data.json");

    public JsonStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void Load_returns_defaults_when_file_missing()
    {
        var store = new JsonStore(FilePath);
        var d = store.Load();
        Assert.Equal(1, d.Version);
        Assert.Empty(d.Memos);
        Assert.Empty(d.Observed);
        Assert.Equal(5, d.Settings.HighlightMinutes);
        Assert.Equal("Ctrl+Alt+Shift+C", d.Settings.Hotkey);
        Assert.False(store.LastLoadRecovered);
    }

    [Fact]
    public void Save_then_Load_round_trips_korean_memo()
    {
        var store = new JsonStore(FilePath);
        var t = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(9));
        var data = new AppData();
        data.Memos["10C4:EA60:TEST0001"] = "시험용 보드 A";
        data.Observed["10C4:EA60:TEST0001"] = new ObservedTimes(t, null);
        data.Settings.Left = 12.5;
        data.Settings.AlwaysOnTop = true;

        Assert.True(store.Save(data));
        Assert.Contains("시험용 보드 A", File.ReadAllText(FilePath, System.Text.Encoding.UTF8));

        var back = new JsonStore(FilePath).Load();
        Assert.Equal("시험용 보드 A", back.Memos["10C4:EA60:TEST0001"]);
        Assert.Equal(t, back.Observed["10C4:EA60:TEST0001"].Arrival);
        Assert.Null(back.Observed["10C4:EA60:TEST0001"].Removal);
        Assert.Equal(12.5, back.Settings.Left);
        Assert.Null(back.Settings.Top);
        Assert.True(back.Settings.AlwaysOnTop);
    }

    [Fact]
    public void Save_leaves_no_temp_file()
    {
        var store = new JsonStore(FilePath);
        Assert.True(store.Save(new AppData()));
        Assert.True(store.Save(new AppData()));
        Assert.Equal(new[] { FilePath }, Directory.GetFiles(_dir));
    }

    [Fact]
    public void Save_keeps_previous_file_when_write_fails()
    {
        var store = new JsonStore(FilePath);
        var first = new AppData();
        first.Memos["k"] = "처음";
        Assert.True(store.Save(first));

        var second = new AppData();
        second.Memos["k"] = "나중";
        // 대상 파일을 공유 없이 열어 두어 교체를 실패시킨다.
        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(store.Save(second));
        }

        Assert.Equal("처음", new JsonStore(FilePath).Load().Memos["k"]);
        Assert.Equal(new[] { FilePath }, Directory.GetFiles(_dir));
    }

    [Fact]
    public void Save_failure_does_not_throw_and_reports_false()
    {
        var target = Path.Combine(_dir, "asfolder");
        Directory.CreateDirectory(target);
        var store = new JsonStore(target);
        Assert.False(store.Save(new AppData()));
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void Save_creates_missing_parent_folder()
    {
        var path = Path.Combine(_dir, "sub", "deeper", "data.json");
        Assert.True(new JsonStore(path).Save(new AppData()));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Load_preserves_corrupt_file_and_returns_defaults()
    {
        File.WriteAllText(FilePath, "{ not json");
        var store = new JsonStore(FilePath);
        var d = store.Load();
        Assert.True(store.LastLoadRecovered);
        Assert.Empty(d.Memos);
        Assert.False(File.Exists(FilePath));
        var kept = Directory.GetFiles(_dir, "*.corrupt-*");
        Assert.Single(kept);
        Assert.Equal("{ not json", File.ReadAllText(kept[0]));
    }

    [Fact]
    public void Load_ignores_unknown_fields()
    {
        File.WriteAllText(FilePath, """{"Version":1,"Future":{"x":1},"Memos":{"a":"b"},"Settings":{"Zzz":3,"HighlightMinutes":9}}""");
        var store = new JsonStore(FilePath);
        var d = store.Load();
        Assert.False(store.LastLoadRecovered);
        Assert.Equal("b", d.Memos["a"]);
        Assert.Equal(9, d.Settings.HighlightMinutes);
    }

    [Fact]
    public void Load_fills_defaults_for_missing_settings()
    {
        File.WriteAllText(FilePath, """{"Memos":{"a":"b"}}""");
        var d = new JsonStore(FilePath).Load();
        Assert.Equal("b", d.Memos["a"]);
        Assert.NotNull(d.Observed);
        Assert.Equal(5, d.Settings.HighlightMinutes);
        Assert.Equal("auto", d.Settings.Language);
        Assert.False(d.Settings.DisconnectedExpanded);
    }

    [Fact]
    public void Load_failure_blocks_save_until_reload_succeeds()
    {
        var first = new AppData();
        first.Memos["k"] = "보존";
        var store = new JsonStore(FilePath);
        Assert.True(store.Save(first));
        var before = File.ReadAllBytes(FilePath);

        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var d = store.Load();
            Assert.Empty(d.Memos);
            Assert.True(store.LastLoadFailed);
            Assert.False(store.LastLoadRecovered);
        }

        Assert.False(store.Save(new AppData()));
        Assert.Equal(before, File.ReadAllBytes(FilePath));

        Assert.Equal("보존", store.Load().Memos["k"]);
        Assert.False(store.LastLoadFailed);
        Assert.True(store.Save(new AppData()));
    }

    [Fact]
    public void Load_blocks_save_when_corrupt_file_cannot_be_preserved()
    {
        File.WriteAllText(FilePath, "{ not json");
        var store = new JsonStore(FilePath) { PreserveHook = _ => false };
        store.Load();
        Assert.False(store.LastLoadRecovered);
        Assert.True(store.LastLoadFailed);
        Assert.False(store.Save(new AppData()));
        Assert.Equal("{ not json", File.ReadAllText(FilePath));
    }

    [Fact]
    public void Save_flushes_and_replaces_existing_file()
    {
        var store = new JsonStore(FilePath);
        var a = new AppData(); a.Memos["k"] = "하나";
        var b = new AppData(); b.Memos["k"] = "둘";
        Assert.True(store.Save(a));
        Assert.True(store.Save(b));
        Assert.Equal("둘", store.Load().Memos["k"]);
    }
}
