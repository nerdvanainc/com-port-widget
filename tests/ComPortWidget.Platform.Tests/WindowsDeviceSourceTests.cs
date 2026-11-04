using System.Diagnostics;
using ComPortWidget.Core.Devices;
using ComPortWidget.Platform;
using Microsoft.Win32;
using System.Security.AccessControl;
using Xunit.Abstractions;

namespace ComPortWidget.Platform.Tests;

public class WindowsDeviceSourceTests(ITestOutputHelper output)
{
    private sealed class FakeReader(Func<string, DeviceRecord?> read, params string[] ids) : IDevicePropertyReader
    {
        public IReadOnlyList<string> ListInstanceIds() => ids;
        public DeviceRecord? TryRead(string instanceId) => read(instanceId);
    }

    private static DeviceRecord Rec(string id) =>
        new(id, "COM9", true, null, null, null, null, Array.Empty<string>());

    [Fact]
    public void Enumerate_skips_record_when_property_read_fails()
    {
        var reader = new FakeReader(id => id == "BAD" ? null : Rec(id), "A", "BAD", "B");
        var records = new WindowsDeviceSource(reader).GetRecords();
        Assert.Equal(new[] { "A", "B" }, records.Select(r => r.InstanceId).ToArray());
    }

    [Fact]
    public void Enumerate_skips_record_when_reader_throws()
    {
        var reader = new FakeReader(id => id == "BAD" ? throw new InvalidOperationException() : Rec(id), "A", "BAD", "B");
        var records = new WindowsDeviceSource(reader).GetRecords();
        Assert.Equal(2, records.Count);
    }

    [Fact]
    public void Enumerate_reads_real_records_quickly()
    {
        var source = new WindowsDeviceSource();
        source.GetRecords(); // 첫 호출의 로딩 비용 제외
        var sw = Stopwatch.StartNew();
        var records = source.GetRecords();
        sw.Stop();

        var absent = records.Where(r => !r.Present).ToList();
        // 개수만 출력한다. 인스턴스 ID와 시리얼은 출력하지 않는다.
        output.WriteLine($"total={records.Count} present={records.Count - absent.Count} absent={absent.Count}");
        output.WriteLine($"emptyPortName={records.Count(r => r.PortName.Length == 0)}");
        output.WriteLine($"serialKey={records.Count(r => DeviceIdentity.Resolve(r).HasSerial)}");
        output.WriteLine($"blankVendor={records.Count(r => string.IsNullOrWhiteSpace(VendorTable.DisplayVendor(r)))}");
        output.WriteLine($"absentEmptyAncestors={absent.Count(r => r.Ancestors.Count == 0)}");
        output.WriteLine($"elapsedMs={sw.ElapsedMilliseconds}");

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"elapsed {sw.ElapsedMilliseconds} ms");
        Assert.All(records, r => Assert.False(string.IsNullOrEmpty(r.InstanceId)));
    }

    [Fact]
    public void GetRecordsStrict_throws_when_reader_returns_null()
    {
        var reader = new FakeReader(id => id == "BAD" ? null : Rec(id), "A", "BAD");
        Assert.Throws<InvalidOperationException>(() => new WindowsDeviceSource(reader).GetRecordsStrict());
    }

    [Fact]
    public void GetRecordsStrict_throws_when_port_name_is_empty()
    {
        var reader = new FakeReader(id => Rec(id) with { PortName = "" }, "A");
        Assert.Throws<InvalidOperationException>(() => new WindowsDeviceSource(reader).GetRecordsStrict());
    }

    [Fact]
    public void GetRecordsStrict_returns_all_when_everything_is_readable()
    {
        var reader = new FakeReader(Rec, "A", "B");
        Assert.Equal(2, new WindowsDeviceSource(reader).GetRecordsStrict().Count);
    }

    [Fact]
    public void GetRecords_lenient_keeps_empty_port_name()
    {
        var reader = new FakeReader(id => Rec(id) with { PortName = "" }, "A");
        Assert.Single(new WindowsDeviceSource(reader).GetRecords());
    }

    [Fact]
    public void Record_is_null_when_present_state_is_unreadable()
    {
        Assert.Null(SetupApiReader.Compose("X", null, "COM1", null, null, null, null, Array.Empty<string>()));
        var ok = SetupApiReader.Compose("X", false, "COM1", null, null, null, null, Array.Empty<string>());
        Assert.NotNull(ok);
        Assert.False(ok!.Present);
    }

    [Fact]
    public void Real_strict_read_works_on_this_machine()
    {
        var records = new WindowsDeviceSource().GetRecordsStrict();
        output.WriteLine($"strictTotal={records.Count}");
    }

    // ---- 레지스트리 가짜 ----
    private sealed class FakeNode : IRegistryNode
    {
        public Dictionary<string, object> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, FakeNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> DeniedChildren { get; } = new(StringComparer.OrdinalIgnoreCase);
        // 목록에는 보이지만 여는 시점에 사라진 키 (OpenSubKey가 null)
        public HashSet<string> Vanished { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool DeniedValues { get; set; }
        public string[] GetSubKeyNames() => Children.Keys.Concat(DeniedChildren).Concat(Vanished).Distinct().ToArray();
        public string[] GetValueNames() => Values.Keys.ToArray();
        public object? GetValue(string name)
        {
            if (DeniedValues) throw new UnauthorizedAccessException();
            return Values.GetValueOrDefault(name);
        }
        public IRegistryNode? OpenSubKey(string name)
        {
            if (DeniedChildren.Contains(name)) throw new System.Security.SecurityException();
            return Children.GetValueOrDefault(name);
        }
        public void Dispose() { }
    }

    private sealed class FakeView : IRegistryView
    {
        public Dictionary<string, FakeNode> Roots { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Denied { get; } = new(StringComparer.OrdinalIgnoreCase);
        public IRegistryNode? Open(string path)
        {
            if (Denied.Contains(path)) throw new UnauthorizedAccessException();
            return Roots.GetValueOrDefault(path);
        }
    }

    private const string SerialComm = @"HARDWARE\DEVICEMAP\SERIALCOMM";
    private const string EnumPath = @"SYSTEM\CurrentControlSet\Enum";
    private const string ModemPath = @"SYSTEM\CurrentControlSet\Control\Class\{4D36E96D-E325-11CE-BFC1-08002BE10318}";
    private const string ComDbPath = @"SYSTEM\CurrentControlSet\Control\COM Name Arbiter";

    [Fact]
    public void ReadOtherHolders_collects_serialcomm_numbers()
    {
        var v = new FakeView();
        var n = new FakeNode();
        n.Values["Serial0"] = "COM3";
        n.Values["x"] = "garbage";
        v.Roots[SerialComm] = n;
        v.Roots[EnumPath] = new FakeNode();
        Assert.Equal(new[] { 3 }, ComRegistry.ReadOtherHolders(v).ToArray());
    }

    [Fact]
    public void ReadOtherHolders_missing_keys_give_empty_set()
    {
        var v = new FakeView();
        v.Roots[EnumPath] = new FakeNode();
        Assert.Empty(ComRegistry.ReadOtherHolders(v));
    }

    [Fact]
    public void ReadOtherHolders_throws_when_enum_is_denied()
    {
        var v = new FakeView();
        v.Denied.Add(EnumPath);
        Assert.ThrowsAny<Exception>(() => ComRegistry.ReadOtherHolders(v));
    }

    [Fact]
    public void ReadOtherHolders_throws_when_enum_missing()
    {
        Assert.Throws<InvalidOperationException>(() => ComRegistry.ReadOtherHolders(new FakeView()));
    }

    [Fact]
    public void ReadOtherHolders_ignores_modem_properties_and_collects_attachedto()
    {
        var v = new FakeView();
        v.Roots[EnumPath] = new FakeNode();
        var modem = new FakeNode();
        var m0 = new FakeNode();
        m0.Values["AttachedTo"] = "COM7";
        modem.Children["0000"] = m0;
        modem.DeniedChildren.Add("Properties");
        v.Roots[ModemPath] = modem;
        Assert.Equal(new[] { 7 }, ComRegistry.ReadOtherHolders(v).ToArray());
    }

    [Fact]
    public void ReadOtherHolders_enum_collects_non_ports_and_skips_vanished_keys()
    {
        var v = new FakeView();
        var root = new FakeNode();
        var bus = new FakeNode();
        var dev = new FakeNode();
        var inst = new FakeNode();
        var prm = new FakeNode();
        prm.Values["PortName"] = "COM11";
        inst.Children["Device Parameters"] = prm;
        dev.Children["0"] = inst;
        var portsInst = new FakeNode();
        portsInst.Values["ClassGUID"] = "{4D36E978-E325-11CE-BFC1-08002BE10318}";
        var pprm = new FakeNode();
        pprm.Values["PortName"] = "COM12";
        portsInst.Children["Device Parameters"] = pprm;
        dev.Children["1"] = portsInst;
        bus.Children["D"] = dev;
        root.Children["B"] = bus;
        root.Vanished.Add("GONE");
        bus.Vanished.Add("GONE2");
        v.Roots[EnumPath] = root;
        Assert.Equal(new[] { 11 }, ComRegistry.ReadOtherHolders(v).ToArray());
    }

    private static FakeView EnumWith(Action<FakeNode, FakeNode, FakeNode, FakeNode> setup)
    {
        // root > bus > dev > inst > Device Parameters
        var v = new FakeView();
        var root = new FakeNode(); var bus = new FakeNode(); var dev = new FakeNode(); var inst = new FakeNode();
        root.Children["B"] = bus; bus.Children["D"] = dev; dev.Children["0"] = inst;
        setup(root, bus, dev, inst);
        v.Roots[EnumPath] = root;
        return v;
    }

    [Fact]
    public void ReadOtherHolders_throws_when_enum_bus_level_is_denied()
        => Assert.ThrowsAny<Exception>(() => ComRegistry.ReadOtherHolders(EnumWith((r, b, d, i) => r.DeniedChildren.Add("X"))));

    [Fact]
    public void ReadOtherHolders_throws_when_enum_device_level_is_denied()
        => Assert.ThrowsAny<Exception>(() => ComRegistry.ReadOtherHolders(EnumWith((r, b, d, i) => b.DeniedChildren.Add("X"))));

    [Fact]
    public void ReadOtherHolders_throws_when_enum_instance_level_is_denied()
        => Assert.ThrowsAny<Exception>(() => ComRegistry.ReadOtherHolders(EnumWith((r, b, d, i) => d.DeniedChildren.Add("X"))));

    [Fact]
    public void ReadOtherHolders_throws_when_device_parameters_is_denied()
        => Assert.ThrowsAny<Exception>(() => ComRegistry.ReadOtherHolders(EnumWith((r, b, d, i) => i.DeniedChildren.Add("Device Parameters"))));

    [Fact]
    public void ReadOtherHolders_throws_when_instance_value_read_fails()
        => Assert.ThrowsAny<Exception>(() => ComRegistry.ReadOtherHolders(EnumWith((r, b, d, i) => i.DeniedValues = true)));

    [Fact]
    public void ReadOtherHolders_throws_when_serialcomm_is_denied()
    {
        var v = new FakeView();
        v.Roots[EnumPath] = new FakeNode();
        v.Denied.Add(SerialComm);
        Assert.ThrowsAny<Exception>(() => ComRegistry.ReadOtherHolders(v));
    }

    [Fact]
    public void ReadOtherHolders_throws_when_serialcomm_value_read_fails()
    {
        var v = new FakeView();
        v.Roots[EnumPath] = new FakeNode();
        var n = new FakeNode { DeniedValues = true };
        n.Values["Serial0"] = "COM3";
        v.Roots[SerialComm] = n;
        Assert.ThrowsAny<Exception>(() => ComRegistry.ReadOtherHolders(v));
    }

    [Fact]
    public void ReadOtherHolders_throws_when_numeric_modem_key_is_denied()
    {
        var v = new FakeView();
        v.Roots[EnumPath] = new FakeNode();
        var modem = new FakeNode();
        modem.DeniedChildren.Add("0001");
        v.Roots[ModemPath] = modem;
        Assert.ThrowsAny<Exception>(() => ComRegistry.ReadOtherHolders(v));
    }

    [Fact]
    public void ReadOtherHolders_throws_when_modem_attachedto_read_fails()
    {
        var v = new FakeView();
        v.Roots[EnumPath] = new FakeNode();
        var modem = new FakeNode();
        modem.Children["0000"] = new FakeNode { DeniedValues = true };
        v.Roots[ModemPath] = modem;
        Assert.ThrowsAny<Exception>(() => ComRegistry.ReadOtherHolders(v));
    }

    [Fact]
    public void ReadComDb_throws_when_value_is_not_binary()
    {
        var v = new FakeView();
        var n = new FakeNode();
        n.Values["ComDB"] = "text";
        v.Roots[ComDbPath] = n;
        Assert.Throws<InvalidDataException>(() => ComRegistry.ReadComDb(v));
    }

    [Fact]
    public void RealNode_GetValue_throws_when_value_exists_but_handle_cannot_read_it()
    {
        // EnumerateSubKeys 권한만 가진 읽기 전용 핸들. RegistryKey.GetValue는 이때 예외 없이 null을 돌려준다.
        const string path = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        using var full = Registry.LocalMachine.OpenSubKey(path, writable: false)!;
        Assert.NotNull(full.GetValue("ProductName")); // 전제: 값이 실제로 있다
        using var reduced = Registry.LocalMachine.OpenSubKey(path, RegistryKeyPermissionCheck.ReadSubTree, RegistryRights.EnumerateSubKeys);
        Assert.NotNull(reduced);
        using var node = new RealNode(reduced!);
        Assert.ThrowsAny<Exception>(() => node.GetValue("ProductName"));
    }

    [Fact]
    public void RealNode_GetValue_returns_null_for_missing_value()
    {
        using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", writable: false)!;
        using var node = new RealNode(k);
        Assert.Null(node.GetValue("NoSuchValueForThisTest"));
        Assert.NotNull(node.GetValue("ProductName"));
    }

    [Fact]
    public void RealNode_OpenSubKey_returns_null_only_for_missing_keys()
    {
        using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT", writable: false)!;
        using var node = new RealNode(k);
        Assert.Null(node.OpenSubKey("NoSuchSubKeyForThisTest"));
        Assert.Null(node.OpenSubKey(@"NoSuchA\NoSuchB"));
        using var ok = node.OpenSubKey("CurrentVersion");
        Assert.NotNull(ok);
        Assert.Null(new RealRegistryView().Open(@"SOFTWARE\NoSuchKeyForThisTest\X"));
    }

    [Fact]
    public void RealNode_OpenSubKey_throws_on_invalid_handle()
    {
        // 존재하지 않는 핸들 값. RegistryKey.OpenSubKey라면 null을 돌려줄 상황이다.
        using var bad = RegistryKey.FromHandle(new Microsoft.Win32.SafeHandles.SafeRegistryHandle(new IntPtr(0x7FFF1234), ownsHandle: false));
        using var node = new RealNode(bad);
        Assert.ThrowsAny<Exception>(() => node.OpenSubKey("Anything"));
    }

    // ---- 상위 장치 사슬 ----
    private const string Child = @"USB\VID_1111&PID_2222&MI_00\9&AAAA&0&0000";
    private const string Composite = @"USB\VID_1111&PID_2222\FAKESERIAL1";
    private const string Hub = @"USB\VID_8087&PID_0033\5&BBBB&0&1";

    [Fact]
    public void WalkAncestors_follows_same_vidpid_chain_and_stops_at_first_different_one()
    {
        var read = new List<string>();
        string? Parent(string id)
        {
            read.Add(id);
            return id switch { Child => Composite, Composite => Hub, _ => throw new InvalidOperationException("must not be read: " + id) };
        }
        var chain = SetupApiReader.WalkAncestors(Child, Parent, strict: true);
        Assert.Equal(new[] { Composite, Hub }, chain.Ids);
        Assert.False(chain.Incomplete);
        Assert.DoesNotContain(Hub, read); // 허브의 상위는 읽지 않는다
    }

    [Fact]
    public void WalkAncestors_does_not_read_anything_for_record_without_vidpid()
    {
        var chain = SetupApiReader.WalkAncestors(@"ROOT\COM0COM\0000", _ => throw new InvalidOperationException("must not be read"), strict: true);
        Assert.Empty(chain.Ids);
        Assert.False(chain.Incomplete);
    }

    [Fact]
    public void WalkAncestors_stops_at_root()
    {
        var chain = SetupApiReader.WalkAncestors(Child, id => id == Child ? Composite : @"ROOT\X", strict: true);
        Assert.Equal(new[] { Composite }, chain.Ids);
        Assert.False(chain.Incomplete);
    }

    [Fact]
    public void WalkAncestors_stops_when_no_parent_property_in_both_modes()
    {
        string? Parent(string id) => id == Child ? Composite : null;
        foreach (var strict in new[] { true, false })
        {
            var chain = SetupApiReader.WalkAncestors(Child, Parent, strict);
            Assert.Equal(new[] { Composite }, chain.Ids);
            Assert.False(chain.Incomplete);
        }
    }

    [Fact]
    public void WalkAncestors_missing_ancestor_ends_chain_as_incomplete_in_both_modes()
    {
        string? Parent(string id) => id == Child ? Composite : throw new AncestorMissingException(id);
        foreach (var strict in new[] { true, false })
        {
            var chain = SetupApiReader.WalkAncestors(Child, Parent, strict);
            Assert.Equal(new[] { Composite }, chain.Ids);
            Assert.True(chain.Incomplete);
        }
    }

    [Fact]
    public void WalkAncestors_strict_throws_naming_the_instance_and_lenient_marks_incomplete_on_read_failure()
    {
        string? Parent(string id) => id == Child ? Composite : throw new InvalidOperationException("denied");
        var ex = Assert.Throws<InvalidOperationException>(() => SetupApiReader.WalkAncestors(Child, Parent, strict: true));
        Assert.Contains(Composite, ex.Message);
        Assert.Contains(Child, ex.Message);
        Assert.Contains("denied", ex.Message);
        var lenient = SetupApiReader.WalkAncestors(Child, Parent, strict: false);
        Assert.Equal(new[] { Composite }, lenient.Ids);
        Assert.True(lenient.Incomplete);
    }

    [Fact]
    public void WalkAncestors_limits_depth_to_eight()
    {
        // 같은 VID/PID가 끝없이 이어져도 8단계에서 멈춘다
        var n = 0;
        var chain = SetupApiReader.WalkAncestors(Child, _ => $@"USB\VID_1111&PID_2222\S{n++}", strict: true);
        Assert.Equal(8, chain.Ids.Count);
    }

    [Fact]
    public void Strict_read_error_message_names_the_instance()
    {
        var src = new WindowsDeviceSource(new FakeReader(_ => null, @"USB\VID_1&PID_2\FAKE9"));
        var ex = Assert.Throws<InvalidOperationException>(() => src.GetRecordsStrict());
        Assert.Contains("FAKE9", ex.Message);
    }

    [Fact]
    public void ReadComDb_returns_bytes_and_empty_when_missing()
    {
        var v = new FakeView();
        Assert.Empty(ComRegistry.ReadComDb(v));
        var n = new FakeNode();
        v.Roots[ComDbPath] = n;
        Assert.Empty(ComRegistry.ReadComDb(v)); // 값 없음
        n.Values["ComDB"] = new byte[] { 1, 2, 3 };
        Assert.Equal(new byte[] { 1, 2, 3 }, ComRegistry.ReadComDb(v));
    }

    [Fact]
    public void ReadComDb_throws_on_read_failure()
    {
        var v = new FakeView();
        v.Denied.Add(ComDbPath);
        Assert.ThrowsAny<Exception>(() => ComRegistry.ReadComDb(v));
        var v2 = new FakeView();
        v2.Roots[ComDbPath] = new FakeNode { DeniedValues = true };
        Assert.ThrowsAny<Exception>(() => ComRegistry.ReadComDb(v2));
    }

    [Fact]
    public void ReadComDb_real_does_not_throw_on_this_machine()
    {
        Assert.NotNull(ComRegistry.ReadComDb());
    }

    [Fact]
    public void ReadOtherHolders_real_does_not_throw_on_this_machine()
    {
        output.WriteLine($"otherHolders={ComRegistry.ReadOtherHolders().Count}");
    }
}
