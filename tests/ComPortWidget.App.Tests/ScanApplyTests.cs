using ComPortWidget.App;
using ComPortWidget.Core.Devices;

namespace ComPortWidget.App.Tests;

public class ScanApplyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Baseline = Now.AddHours(-5);

    private static DeviceRecord Rec(string serial, string port, bool present)
        => new(@"USB\VID_10C4&PID_EA60\" + serial, port, present, "테스트 장치", null, Now.AddHours(-1), null, Array.Empty<string>());

    private static WidgetViewModel Create()
        => new(new DemoDeviceSource(), null, () => Baseline, () => Now);

    [Fact]
    public void Empty_scan_with_connected_records_is_held_back_until_confirmed()
    {
        var vm = Create();
        Assert.True(vm.Apply(new[] { Rec("DEMOA", "COM3", true) }, Baseline));
        Assert.Single(vm.Connected);

        // 연결 기록이 있었는데 빈 목록이 오면 반영하지 않는다 (목록은 그대로)
        Assert.False(vm.Apply(Array.Empty<DeviceRecord>(), Baseline));
        Assert.Single(vm.Connected);

        // 두 번째 조회도 비어 있으면 그때 반영한다
        Assert.True(vm.Apply(Array.Empty<DeviceRecord>(), Baseline));
        Assert.Empty(vm.Connected);
    }

    [Fact]
    public void Empty_scan_followed_by_normal_scan_highlights_nothing_new()
    {
        var vm = Create();
        vm.Apply(new[] { Rec("DEMOA", "COM3", true), Rec("DEMOB", "COM4", true) }, Baseline);
        Assert.False(vm.Apply(Array.Empty<DeviceRecord>(), Baseline));
        Assert.True(vm.Apply(new[] { Rec("DEMOA", "COM3", true), Rec("DEMOB", "COM4", true) }, Baseline));
        Assert.Empty(vm.Data.Observed);
        Assert.All(vm.Connected, r => Assert.False(r.IsHighlighted));
    }

    [Fact]
    public void Empty_scan_with_nothing_connected_is_applied_at_once()
    {
        var vm = Create();
        Assert.True(vm.Apply(new[] { Rec("DEMOA", "COM3", false) }, Baseline));
        Assert.True(vm.Apply(Array.Empty<DeviceRecord>(), Baseline));
    }

    [Fact]
    public void Record_skipped_once_does_not_get_false_arrival_or_lose_removal()
    {
        var vm = Create();
        var a = Rec("DEMOA", "COM3", true);
        var b = Rec("DEMOB", "COM4", true);
        vm.Apply(new[] { a, b }, Baseline);
        vm.Apply(new[] { b }, Baseline);          // a가 이번 조회에서 빠짐(읽기 실패)
        vm.Apply(new[] { a, b }, Baseline);       // 다시 나타남
        Assert.False(vm.Data.Observed.ContainsKey(a.InstanceId));
    }

    [Fact]
    public void Empty_first_scan_then_normal_scan_highlights_nothing()
    {
        var vm = Create();
        Assert.True(vm.Apply(Array.Empty<DeviceRecord>(), Baseline));
        Assert.True(vm.Apply(new[] { Rec("DEMOA", "COM3", true), Rec("DEMOB", "COM4", true) }, Baseline));
        Assert.Equal(2, vm.Connected.Count);
        Assert.Empty(vm.Data.Observed);
        Assert.Equal(0, vm.Connected.Count(r => r.IsHighlighted));
    }

    [Fact]
    public void Record_missing_from_first_scan_gets_no_observed_arrival()
    {
        var vm = Create();
        vm.Apply(new[] { Rec("DEMOB", "COM4", true) }, Baseline);
        vm.Apply(new[] { Rec("DEMOA", "COM3", true), Rec("DEMOB", "COM4", true) }, Baseline);
        Assert.Empty(vm.Data.Observed);
    }

    [Fact]
    public void Real_unplug_and_replug_records_removal_then_arrival()
    {
        var vm = Create();
        var a = Rec("DEMOA", "COM3", true);
        vm.Apply(new[] { a, Rec("DEMOB", "COM4", true) }, Baseline);
        vm.Apply(new[] { a with { Present = false }, Rec("DEMOB", "COM4", true) }, Baseline);
        Assert.NotNull(vm.Data.Observed[a.InstanceId].Removal);
        vm.Apply(new[] { a, Rec("DEMOB", "COM4", true) }, Baseline);
        Assert.NotNull(vm.Data.Observed[a.InstanceId].Arrival);
    }
}
