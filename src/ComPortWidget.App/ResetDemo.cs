using ComPortWidget.Core.Devices;
using ComPortWidget.Core.Reset;

namespace ComPortWidget.App;

/// <summary>--demo용 정리 데이터와 가짜 백엔드. 시스템을 읽거나 바꾸지 않는다. 시리얼은 모두 가짜 값이다.</summary>
internal static class ResetDemo
{
    private static readonly IReadOnlyList<string> NoAncestors = Array.Empty<string>();
    private const string Sep = "\\";

    /// <summary>위젯 데모 장치에 정리 화면 확인용 기록(같은 장비, 가상, 블루투스)을 더한 목록.</summary>
    public static List<DeviceRecord> Records()
    {
        var now = DateTimeOffset.Now;
        var list = new DemoDeviceSource().GetRecords().ToList();
        list.Add(new DeviceRecord($"USB{Sep}VID_303A&PID_1001{Sep}AA:BB:CC:00:00:01", "COM30", true,
            "USB JTAG/serial debug unit", null, now.AddMinutes(-5), null, NoAncestors));
        list.Add(new DeviceRecord($"USB{Sep}VID_303A&PID_0002{Sep}AA:BB:CC:00:00:01", "COM31", false,
            "ESP32-S3 ROM", null, now.AddDays(-3), now.AddDays(-3), NoAncestors));
        list.Add(new DeviceRecord($"ROOT{Sep}COM0COM{Sep}0000", "COM40", false,
            "Virtual serial port", null, now.AddDays(-20), null, NoAncestors));
        list.Add(new DeviceRecord($"BTHENUM{Sep}DEV_001122334455{Sep}7&1&0&BLUETOOTH", "COM11", false,
            "Bluetooth serial", null, now.AddDays(-40), null, NoAncestors));
        return list;
    }

    /// <summary>예약 목록. 기록이 있는 번호 외에 기록 없이 남은 번호(23~25)를 포함한다.</summary>
    public static HashSet<int> Reserved() => new() { 7, 9, 11, 20, 23, 24, 25, 30, 31, 40, 48, 52 };

    public static Dictionary<string, string> Memos(IEnumerable<DeviceRecord> records, IDictionary<string, string> baseMemos)
    {
        var memos = new Dictionary<string, string>(baseMemos);
        foreach (var r in records)
            if (r.InstanceId.Contains("AA:BB:CC:00:00:01"))
                memos[DeviceIdentity.Resolve(r).Value] = "ESP32-S3 보드";
        return memos;
    }
}

/// <summary>메모리 안의 가짜 백엔드. 실제 장치와 ComDB를 건드리지 않는다.</summary>
internal sealed class DemoResetBackend : IResetBackend
{
    private readonly List<DeviceRecord> _records = ResetDemo.Records();
    private readonly HashSet<int> _reserved = ResetDemo.Reserved();

    public IReadOnlyList<DeviceRecord> GetRecords() => _records.ToList();
    public ISet<int> GetReservedPorts() => new HashSet<int>(_reserved);
    public ISet<int> GetOtherHolders() => new HashSet<int>();
    public void WriteBackup(ResetPlan plan, IReadOnlyList<DeviceRecord> targets) { }

    public bool IsPresent(string instanceId)
        => _records.First(r => string.Equals(r.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase)).Present;

    public bool RemoveDevice(string instanceId)
    {
        _records.RemoveAll(r => string.Equals(r.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));
        return false;
    }

    public void ReleasePort(int number) => _reserved.Remove(number);
}
