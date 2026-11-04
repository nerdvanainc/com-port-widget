using ComPortWidget.Core.Devices;
using ComPortWidget.Core.Reset;

namespace ComPortWidget.Core.Tests;

public class ResetPlannerTests
{
    private static readonly DateTimeOffset T1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

    private static DeviceRecord Rec(string id, string port, bool present = false,
        DateTimeOffset? arrival = null, DateTimeOffset? removal = null)
        => new(id, port, present, null, null, arrival, removal, Array.Empty<string>());

    private static IReadOnlyList<ResetCandidate> Build(IReadOnlyList<DeviceRecord> records,
        int[]? reserved = null, int[]? others = null, Dictionary<string, string>? memos = null)
        => ResetPlanner.Build(records, new HashSet<int>(reserved ?? Array.Empty<int>()),
            new HashSet<int>(others ?? Array.Empty<int>()), memos ?? new Dictionary<string, string>());

    private const string UsbA = @"USB\VID_1234&PID_5678\SN0001A";
    // 같은 시리얼(상위 USB 장치)을 공유하는 두 인터페이스 기록 (같은 DeviceKey)
    private const string ChildNew = @"USB\VID_1234&PID_5678&MI_00\8&AAAA&0&0000";
    private const string ChildOld = @"USB\VID_1234&PID_5678&MI_00\8&BBBB&0&0000";
    private static DeviceRecord Child(string id, string port, bool present)
        => new(id, port, present, null, null, null, null, new[] { UsbA });

    [Fact]
    public void Record_with_incomplete_ancestors_is_not_selected_by_default()
    {
        var rec = Rec(UsbA, "COM3", arrival: T1, removal: T2) with { AncestorsIncomplete = true };
        var c = Assert.Single(Build(new[] { rec }, new[] { 3 }));
        Assert.Equal(ResetKind.UsbRecord, c.Kind);
        Assert.False(c.DefaultSelected);
    }

    [Fact]
    public void Candidates_exclude_present_records()
        => Assert.Empty(Build(new[] { Rec(UsbA, "COM3", present: true) }, new[] { 3 }));

    [Fact]
    public void Candidates_select_absent_usb_record_by_default()
    {
        var c = Assert.Single(Build(new[] { Rec(UsbA, "COM3", arrival: T1, removal: T2) }, new[] { 3 }));
        Assert.Equal(ResetKind.UsbRecord, c.Kind);
        Assert.True(c.DefaultSelected);
        Assert.Equal(3, c.PortNumber);
        Assert.Equal(UsbA, c.InstanceId);
        Assert.Equal(T2, c.LastSeen);
        Assert.True(c.ReleasesNumber);
    }

    [Fact]
    public void Candidates_use_last_arrival_when_no_removal()
    {
        var c = Assert.Single(Build(new[] { Rec(UsbA, "COM3", arrival: T1) }));
        Assert.Equal(T1, c.LastSeen);
    }

    [Fact]
    public void Candidates_treat_ftdibus_as_usb()
    {
        var c = Assert.Single(Build(new[] { Rec(@"FTDIBUS\VID_0403+PID_6001+FT1234AA\0000", "COM4") }));
        Assert.Equal(ResetKind.UsbRecord, c.Kind);
        Assert.True(c.DefaultSelected);
    }

    [Theory]
    [InlineData(@"ROOT\PORTS\0000")]
    [InlineData(@"COM0COM\PORT\CNCA0")]
    [InlineData(@"BTHENUM\{00001101-0000-1000-8000-00805F9B34FB}_LOCALMFG&0000\7&1&0&000000000000_C00000000")]
    public void Candidates_deselect_virtual_and_bluetooth(string id)
    {
        var c = Assert.Single(Build(new[] { Rec(id, "COM5") }));
        Assert.Equal(ResetKind.VirtualOrBluetooth, c.Kind);
        Assert.False(c.DefaultSelected);
    }

    [Fact]
    public void Candidates_deselect_unknown_enumerator()
    {
        var c = Assert.Single(Build(new[] { Rec(@"XYZ\FOO\1", "COM6") }));
        Assert.Equal(ResetKind.VirtualOrBluetooth, c.Kind);
        Assert.False(c.DefaultSelected);
    }

    [Fact]
    public void Candidates_deselect_same_device_connected()
    {
        var c = Assert.Single(Build(new[]
        {
            Child(ChildNew, "COM3", present: true),
            Child(ChildOld, "COM7", present: false),
        }));
        Assert.Equal(ChildOld, c.InstanceId);
        Assert.Equal(ResetKind.SameDeviceConnected, c.Kind);
        Assert.False(c.DefaultSelected);
    }

    [Theory]
    [InlineData("CNCA0")]
    [InlineData("")]
    public void Candidates_treat_present_record_with_non_com_name_as_connected(string presentPortName)
    {
        var c = Assert.Single(Build(new[]
        {
            Child(ChildNew, presentPortName, present: true),
            Child(ChildOld, "COM7", present: false),
        }));
        Assert.Equal(ChildOld, c.InstanceId);
        Assert.Equal(ResetKind.SameDeviceConnected, c.Kind);
        Assert.False(c.DefaultSelected);
    }

    [Fact]
    public void Candidates_detect_rom_and_app_mode_pair_by_mac_serial()
    {
        var rom = new DeviceRecord(@"USB\VID_303A&PID_1001&MI_00\9&25EF&0&0000", "COM7", false, null, null, null, null,
            new[] { @"USB\VID_303A&PID_1001\AA:BB:CC:DD:EE:01" });
        var app = new DeviceRecord(@"USB\VID_239A&PID_811B&MI_00\9&16AB&0&0000", "COM3", true, null, null, null, null,
            new[] { @"USB\VID_239A&PID_811B\aa:bb:cc:dd:ee:01" });
        var c = Assert.Single(Build(new[] { rom, app }));
        Assert.Equal(7, c.PortNumber);
        Assert.Equal(ResetKind.SameDeviceConnected, c.Kind);
        Assert.False(c.DefaultSelected);
    }

    [Fact]
    public void Candidates_do_not_treat_keyless_instance_as_same_device()
    {
        // 시리얼이 없는 키(인스턴스 ID 그대로)는 같은 값이어도 같은 장비로 보지 않는다
        var id = @"USB\VID_1234&PID_5678\6&1A2B3C&0&1";
        var c = Assert.Single(Build(new[] { Rec(id, "COM3", present: true), Rec(id, "COM7") }));
        Assert.Equal(ResetKind.UsbRecord, c.Kind);
        Assert.True(c.DefaultSelected);
    }

    [Fact]
    public void Candidates_list_reserved_only_deselected()
    {
        var c = Assert.Single(Build(Array.Empty<DeviceRecord>(), new[] { 23 }));
        Assert.Equal(ResetKind.ReservedOnly, c.Kind);
        Assert.Equal(23, c.PortNumber);
        Assert.Null(c.InstanceId);
        Assert.Null(c.Memo);
        Assert.Null(c.LastSeen);
        Assert.False(c.DefaultSelected);
        Assert.True(c.ReleasesNumber);
    }

    [Fact]
    public void Candidates_skip_reserved_held_by_other_device()
        => Assert.Empty(Build(Array.Empty<DeviceRecord>(), new[] { 23 }, new[] { 23 }));

    [Fact]
    public void Candidates_skip_reserved_held_by_present_record()
        => Assert.Empty(Build(new[] { Rec(UsbA, "COM23", present: true) }, new[] { 23 }));

    [Fact]
    public void Candidates_do_not_release_number_held_by_present_record()
    {
        var c = Assert.Single(Build(new[] { Rec(UsbA, "COM3", present: true), Rec(@"USB\VID_1111&PID_2222\SN9", "COM3") }));
        Assert.False(c.ReleasesNumber);
    }

    [Fact]
    public void Candidates_do_not_release_number_held_by_other_holder()
    {
        var c = Assert.Single(Build(new[] { Rec(UsbA, "COM3") }, others: new[] { 3 }));
        Assert.False(c.ReleasesNumber);
    }

    [Fact]
    public void Candidates_do_not_release_number_shared_by_another_absent_record()
    {
        var list = Build(new[] { Rec(UsbA, "COM3"), Rec(@"USB\VID_1111&PID_2222\SN9", "COM3") });
        Assert.Equal(2, list.Count);
        Assert.All(list, c => Assert.False(c.ReleasesNumber));
    }

    [Fact]
    public void Candidates_include_memo_by_device_key()
    {
        var key = DeviceIdentity.Resolve(Rec(UsbA, "COM3")).Value;
        var c = Assert.Single(Build(new[] { Rec(UsbA, "COM3") }, memos: new() { [key] = "테스트 보드" }));
        Assert.Equal("테스트 보드", c.Memo);
    }

    [Fact]
    public void Candidates_skip_non_com_port_names()
        => Assert.Empty(Build(new[] { Rec(UsbA, "LPT1") }));

    [Fact]
    public void Candidates_sorted_by_port_then_instance()
    {
        var list = Build(new[]
        {
            Rec(@"USB\VID_1111&PID_2222\SNB", "COM9"),
            Rec(@"USB\VID_1111&PID_2222\SNA", "COM9"),
            Rec(@"USB\VID_1111&PID_2222\SNC", "COM2"),
        }, new[] { 5 });
        Assert.Equal(new[] { 2, 5, 9, 9 }, list.Select(c => c.PortNumber));
        Assert.Equal(@"USB\VID_1111&PID_2222\SNA", list[2].InstanceId);
    }

    [Theory]
    [InlineData("COM3", 3)]
    [InlineData("com12", 12)]
    [InlineData("COM0", null)]
    [InlineData("COM", null)]
    [InlineData("COM-1", null)]
    [InlineData("COM1A", null)]
    [InlineData("LPT1", null)]
    [InlineData("", null)]
    public void ParsePortNumber_accepts_only_COM_and_positive_integer(string name, int? expected)
        => Assert.Equal(expected, ResetPlanner.ParsePortNumber(name));

    [Fact]
    public void Summarize_counts_records_and_distinct_released_numbers()
    {
        var c = new[]
        {
            new ResetCandidate(ResetKind.UsbRecord, 5, "USB\\A", null, null, true, true),
            new ResetCandidate(ResetKind.UsbRecord, 5, "USB\\B", null, null, true, true),
            new ResetCandidate(ResetKind.UsbRecord, 6, "USB\\C", null, null, true, false),
            new ResetCandidate(ResetKind.ReservedOnly, 7, null, null, null, false, true),
        };
        var (records, numbers) = ResetPlanner.Summarize(c);
        Assert.Equal(3, records);
        Assert.Equal(2, numbers); // COM5(두 기록이 공유), COM7. COM6은 해제하지 않는다
    }
}
