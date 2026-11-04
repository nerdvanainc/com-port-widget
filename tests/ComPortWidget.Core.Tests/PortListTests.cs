using ComPortWidget.Core.Devices;
using ComPortWidget.Core.Listing;

namespace ComPortWidget.Core.Tests;

public class PortListTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Baseline = new(2026, 11, 4, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    private static readonly IReadOnlyDictionary<string, string> NoMemos = new Dictionary<string, string>();
    private static readonly IReadOnlyDictionary<string, ObservedTimes> NoObserved = new Dictionary<string, ObservedTimes>();

    private static DateTimeOffset At(string hhmmss)
        => DateTimeOffset.Parse($"2026-11-04T{hhmmss}+00:00");

    private static DeviceRecord Rec(string id, string port, bool present, DateTimeOffset? arrival, DateTimeOffset? removal = null)
        => new(id, port, present, "테스트 장치", null, arrival, removal, Array.Empty<string>());

    private static PortList Build(IEnumerable<DeviceRecord> records,
        IReadOnlyDictionary<string, string>? memos = null,
        IReadOnlyDictionary<string, ObservedTimes>? observed = null,
        DateTimeOffset? now = null)
        => PortListBuilder.Build(records, memos ?? NoMemos, observed ?? NoObserved, now ?? Now, Baseline, Window);

    [Fact]
    public void Build_sorts_connected_by_arrival_descending()
    {
        var list = Build(new[]
        {
            Rec("A", "COM3", true, At("09:00:00")),
            Rec("B", "COM4", true, At("09:30:00")),
            Rec("C", "COM5", true, At("09:10:00")),
        });
        Assert.Equal(new[] { "COM4", "COM5", "COM3" }, list.Connected.Select(r => r.Record.PortName));
    }

    [Fact]
    public void Build_sorts_missing_arrival_last()
    {
        var list = Build(new[]
        {
            Rec("A", "COM3", true, null),
            Rec("B", "COM9", true, At("09:00:00")),
        });
        Assert.Equal(new[] { "COM9", "COM3" }, list.Connected.Select(r => r.Record.PortName));
    }

    [Fact]
    public void Build_sorts_equal_time_by_com_number_ascending()
    {
        var list = Build(new[]
        {
            Rec("A", "COM10", true, At("09:00:00")),
            Rec("B", "COM2", true, At("09:00:00")),
        });
        Assert.Equal(new[] { "COM2", "COM10" }, list.Connected.Select(r => r.Record.PortName));
    }

    [Fact]
    public void Build_puts_absent_records_in_disconnected()
    {
        var list = Build(new[]
        {
            Rec("A", "COM3", true, At("09:00:00")),
            Rec("B", "COM4", false, At("07:00:00")),
        });
        Assert.Single(list.Connected);
        Assert.Equal("COM4", Assert.Single(list.Disconnected).Record.PortName);
    }

    [Fact]
    public void Build_sorts_disconnected_by_removal_then_arrival()
    {
        var list = Build(new[]
        {
            Rec("A", "COM3", false, At("07:00:00")),                        // 해제 없음, 인식 07:00
            Rec("B", "COM4", false, At("01:00:00"), At("06:00:00")),        // 해제 06:00
            Rec("C", "COM5", false, At("01:00:00"), At("07:30:00")),        // 해제 07:30
        });
        Assert.Equal(new[] { "COM5", "COM3", "COM4" }, list.Disconnected.Select(r => r.Record.PortName));
        Assert.True(list.Disconnected[0].HasRemovalTime);
        Assert.False(list.Disconnected[1].HasRemovalTime);
        Assert.Equal(At("07:00:00"), list.Disconnected[1].Time);
    }

    [Fact]
    public void Build_hides_names_that_are_not_COM()
    {
        var list = Build(new[]
        {
            Rec("A", "CNCA0", true, At("09:00:00")),
            Rec("B", "COM4", true, At("09:00:00")),
        });
        Assert.Equal("COM4", Assert.Single(list.Connected).Record.PortName);
    }

    [Fact]
    public void Build_highlights_arrival_within_window()
    {
        var list = Build(new[]
        {
            Rec("A", "COM3", true, At("09:56:00")),
            Rec("B", "COM4", true, At("09:54:00")),
        });
        Assert.True(list.Connected.Single(r => r.Record.PortName == "COM3").IsHighlighted);
        Assert.False(list.Connected.Single(r => r.Record.PortName == "COM4").IsHighlighted);
    }

    [Fact]
    public void Build_highlights_future_arrival()
    {
        var list = Build(new[] { Rec("A", "COM3", true, At("10:00:30")) });
        Assert.True(Assert.Single(list.Connected).IsHighlighted);
    }

    [Fact]
    public void Build_does_not_highlight_absent_record()
    {
        var list = Build(new[] { Rec("A", "COM3", false, At("09:58:00")) });
        Assert.False(Assert.Single(list.Disconnected).IsHighlighted);
    }

    [Fact]
    public void Build_marks_boot_arrival_without_highlight()
    {
        var list = Build(new[] { Rec("A", "COM3", true, At("08:00:30")) }, now: At("08:01:00"));
        var row = Assert.Single(list.Connected);
        Assert.True(row.IsBootArrival);
        Assert.False(row.IsHighlighted);
    }

    [Fact]
    public void Build_counts_90_seconds_as_boot_boundary()
    {
        var list = Build(new[]
        {
            Rec("A", "COM3", true, At("08:01:30")),
            Rec("B", "COM4", true, At("08:01:31")),
        });
        Assert.True(list.Connected.Single(r => r.Record.PortName == "COM3").IsBootArrival);
        Assert.False(list.Connected.Single(r => r.Record.PortName == "COM4").IsBootArrival);
    }

    [Fact]
    public void Build_does_not_mark_arrival_before_baseline_as_boot()
    {
        var list = Build(new[] { Rec("A", "COM3", true, At("07:59:54")) });
        Assert.False(Assert.Single(list.Connected).IsBootArrival);
    }

    [Fact]
    public void Build_marks_arrival_within_5_seconds_before_baseline_as_boot()
    {
        var list = Build(new[] { Rec("A", "COM3", true, At("07:59:55")), Rec("B", "COM4", true, At("07:59:59")) });
        Assert.All(list.Connected, r => Assert.True(r.IsBootArrival));
    }

    [Fact]
    public void Build_uses_observed_time_when_windows_time_missing()
    {
        var observed = new Dictionary<string, ObservedTimes>
        {
            ["A"] = new(At("09:30:00"), null),
            ["B"] = new(At("09:00:00"), At("09:10:00")),
        };
        var list = Build(new[]
        {
            Rec("A", "COM3", true, null),
            Rec("B", "COM4", false, null),
        }, observed: observed);
        Assert.Equal(At("09:30:00"), Assert.Single(list.Connected).Time);
        var gone = Assert.Single(list.Disconnected);
        Assert.Equal(At("09:10:00"), gone.Time);
        Assert.True(gone.HasRemovalTime);
    }

    [Fact]
    public void Build_prefers_later_observed_arrival()
    {
        var observed = new Dictionary<string, ObservedTimes> { ["A"] = new(At("09:59:00"), null) };
        var list = Build(new[]
        {
            Rec("B", "COM4", true, At("09:30:00")),
            Rec("A", "COM3", true, At("07:00:00")),
        }, observed: observed);
        Assert.Equal(new[] { "COM3", "COM4" }, list.Connected.Select(r => r.Record.PortName));
        Assert.Equal(At("09:59:00"), list.Connected[0].Time);
        Assert.True(list.Connected[0].IsHighlighted);
    }

    [Fact]
    public void Build_ignores_observed_arrival_earlier_than_windows()
    {
        var observed = new Dictionary<string, ObservedTimes> { ["A"] = new(At("09:30:00"), null) };
        var list = Build(new[] { Rec("A", "COM3", true, At("09:50:00")) }, observed: observed);
        Assert.Equal(At("09:50:00"), Assert.Single(list.Connected).Time);
    }

    [Fact]
    public void Build_shares_memo_between_rom_and_app_records()
    {
        // 같은 시리얼과 VID/PID를 쓰는 두 기록 (ROM 모드와 앱 모드)
        var rom = Rec(@"USB\VID_303A&PID_1001\FAKE0001", "COM7", true, At("09:00:00"));
        var app = Rec(@"USB\VID_303A&PID_1001\FAKE0001", "COM8", false, At("08:30:00"));
        var key = DeviceIdentity.Resolve(rom).Value;
        var memos = new Dictionary<string, string> { [key] = "테스트 보드" };
        var list = Build(new[] { rom, app }, memos: memos);
        Assert.Equal("테스트 보드", Assert.Single(list.Connected).Memo);
        Assert.Equal("테스트 보드", Assert.Single(list.Disconnected).Memo);
    }
}
