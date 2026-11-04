using System.Text.RegularExpressions;

namespace ComPortWidget.Core.Devices;

/// <summary>VID로 제조사 이름을, 장치 기록으로 화면에 보일 제조사와 설명을 만든다.</summary>
public static class VendorTable
{
    private static readonly Dictionary<string, string> Vendors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["0403"] = "FTDI",
        ["10C4"] = "Silicon Labs",
        ["303A"] = "Espressif",
        ["1A86"] = "WCH",
        ["239A"] = "Adafruit",
        ["0483"] = "STMicroelectronics",
        ["067B"] = "Prolific",
        ["2341"] = "Arduino",
        ["1366"] = "SEGGER",
        ["0D28"] = "Arm",
    };

    // 여러 제품이 나눠 쓰는 VID: 제조사를 표시하지 않는다
    private static readonly HashSet<string> SharedVids = new(StringComparer.OrdinalIgnoreCase)
    {
        "16D0", "1209",
    };

    // 이름 끝의 "(COM5)" 꼬리표
    private static readonly Regex ComSuffix = new(
        @"\s*\(COM\d+\)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>VID(대소문자 무관)에 해당하는 제조사 이름. 모르면 null.</summary>
    public static string? Lookup(string vid)
        => Vendors.TryGetValue(vid, out var name) ? name : null;

    public static string DisplayVendor(DeviceRecord r)
    {
        if (!DeviceIdentity.TryParseVidPid(r, out var vid, out _))
            return string.Empty;
        if (SharedVids.Contains(vid))
            return string.Empty;
        return Lookup(vid) ?? $"VID {vid}";
    }

    public static string DisplayDescription(DeviceRecord r)
    {
        if (!string.IsNullOrWhiteSpace(r.BusDescription))
            return r.BusDescription.Trim();
        if (!string.IsNullOrWhiteSpace(r.FriendlyName))
            return ComSuffix.Replace(r.FriendlyName, string.Empty).Trim();
        return string.Empty;
    }
}
