namespace ComPortWidget.Core.Devices;

/// <summary>--demo 실행용 가짜 장치 목록. 시리얼은 모두 DEMO로 시작하는 가짜 값이다.</summary>
public sealed class DemoDeviceSource : IDeviceSource
{
    private static readonly IReadOnlyList<string> NoAncestors = Array.Empty<string>();

    /// <summary>화면 확인용 가짜 메모. 키는 포트 이름이다.</summary>
    public static IReadOnlyDictionary<string, string> MemosByPort { get; } = new Dictionary<string, string>
    {
        ["COM52"] = "ESP32 개발보드",
        ["COM20"] = "ESP32-S3 디버그",
        ["COM48"] = "RS485 변환기",
    };

    public IReadOnlyList<DeviceRecord> GetRecords()
    {
        var now = DateTimeOffset.Now;
        var h = (char)92; // 인스턴스 ID 구분자
        string Id(string vid, string pid, string serial) => $"USB{h}VID_{vid}&PID_{pid}{h}{serial}";
        return new[]
        {
            new DeviceRecord(Id("10C4", "EA60", "DEMO0001A"), "COM52", true,
                "CP2102N USB to UART Bridge Controller", null, now.AddSeconds(-30), null, NoAncestors),
            new DeviceRecord(Id("303A", "1001", "DEMO0002B"), "COM20", true,
                "USB JTAG/serial debug unit", null, now.AddMinutes(-12), null, NoAncestors),
            new DeviceRecord(Id("0403", "6001", "DEMO0003C"), "COM48", true,
                "USB Serial Port", null, now.AddHours(-3), null, NoAncestors),
            new DeviceRecord(Id("1A86", "7523", "DEMO0004D"), "COM7", false,
                "USB-SERIAL CH340", null, now.AddDays(-2), now.AddDays(-1), NoAncestors),
            new DeviceRecord(Id("2341", "0043", "DEMO0005E"), "COM9", false,
                "Arduino Uno", null, now.AddDays(-9), null, NoAncestors),
        };
    }
}
