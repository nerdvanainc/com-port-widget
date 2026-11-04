using ComPortWidget.Core.Devices;
using ComPortWidget.Core.Listing;
using ComPortWidget.Core.Text;

namespace ComPortWidget.Core.Tests;

public class TimeLabelTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset Parse(string s) => DateTimeOffset.Parse(s.Replace(' ', 'T') + "+00:00");

    private static PortRow Row(DateTimeOffset? time, bool present = true, bool boot = false, bool removal = false)
    {
        var rec = new DeviceRecord("X", "COM3", present, null, null, null, null, Array.Empty<string>());
        return new PortRow(rec, new DeviceKey("X", false), null, "", "", time, false, boot, removal);
    }

    [Theory]
    [InlineData("2026-11-04 09:59:30", "방금 전")]
    [InlineData("2026-11-04 09:48:00", "12분 전")]
    [InlineData("2026-11-04 08:14:00", "오늘 08:14")]
    [InlineData("2026-11-03 17:40:00", "어제 17:40")]
    [InlineData("2026-11-02 12:00:00", "11/02")]
    public void Format_uses_spec_labels(string time, string expected)
        => Assert.Equal(expected, TimeLabel.Format(Parse(time), Now, "ko"));

    [Theory]
    [InlineData("2026-11-04 09:59:30", "just now")]
    [InlineData("2026-11-04 09:48:00", "12 min ago")]
    [InlineData("2026-11-04 08:14:00", "today 08:14")]
    [InlineData("2026-11-03 17:40:00", "yesterday 17:40")]
    [InlineData("2026-11-02 12:00:00", "11/02")]
    public void Format_uses_english_labels(string time, string expected)
        => Assert.Equal(expected, TimeLabel.Format(Parse(time), Now, "en"));

    [Fact]
    public void Format_treats_future_time_as_just_now()
        => Assert.Equal("방금 전", TimeLabel.Format(Now.AddMinutes(5), Now, "ko"));

    [Fact]
    public void Format_compares_calendar_date_in_now_offset()
    {
        var now = new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.FromHours(9));
        // UTC 11-03 23:00 은 now 오프셋(+09:00)으로 11-04 08:00 이므로 오늘이다
        var time = new DateTimeOffset(2026, 11, 3, 23, 0, 0, TimeSpan.Zero);
        Assert.Equal("오늘 08:00", TimeLabel.Format(time, now, "ko"));
    }

    [Fact]
    public void Format_returns_unknown_label_for_null()
    {
        Assert.Equal("시간 없음", TimeLabel.Format(null, Now, "ko"));
        Assert.Equal("no time", TimeLabel.Format(null, Now, "en"));
    }

    [Fact]
    public void ForRow_says_boot_for_boot_arrival()
    {
        var row = Row(Parse("2026-11-04 08:00:30"), boot: true);
        Assert.Equal("부팅 시 인식", TimeLabel.ForRow(row, Now, "ko"));
        Assert.Equal("at boot", TimeLabel.ForRow(row, Now, "en"));
    }

    [Fact]
    public void ForRow_uses_removed_label_when_removal_known()
    {
        var row = Row(Parse("2026-11-02 12:00:00"), present: false, removal: true);
        Assert.Equal("뽑힘 11/02", TimeLabel.ForRow(row, Now, "ko"));
        Assert.Equal("removed 11/02", TimeLabel.ForRow(row, Now, "en"));
    }

    [Fact]
    public void ForRow_uses_last_seen_label_without_removal()
    {
        var row = Row(Parse("2026-11-02 12:00:00"), present: false);
        Assert.Equal("마지막 인식 11/02", TimeLabel.ForRow(row, Now, "ko"));
        Assert.Equal("last seen 11/02", TimeLabel.ForRow(row, Now, "en"));
    }

    [Fact]
    public void ForRow_uses_relative_label_for_connected_row()
        => Assert.Equal("12분 전", TimeLabel.ForRow(Row(Parse("2026-11-04 09:48:00")), Now, "ko"));

    [Fact]
    public void Strings_has_every_key_in_both_languages()
    {
        Assert.NotEmpty(Strings.Keys);
        foreach (var key in Strings.Keys)
        {
            Assert.False(string.IsNullOrWhiteSpace(Strings.Get(key, "ko")), key);
            Assert.False(string.IsNullOrWhiteSpace(Strings.Get(key, "en")), key);
        }
    }
}
