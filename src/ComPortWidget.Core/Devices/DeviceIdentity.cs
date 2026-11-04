using System.Text.RegularExpressions;

namespace ComPortWidget.Core.Devices;

/// <summary>메모를 붙일 장비 키. HasSerial은 시리얼 기반 키인지 여부이다.</summary>
public sealed record DeviceKey(string Value, bool HasSerial);

/// <summary>장비 식별 규칙. 순수 함수로만 구성한다.</summary>
public static class DeviceIdentity
{
    // 여러 장비가 같은 값을 쓰는 것으로 알려진 기본 시리얼 (규칙 7)
    private static readonly HashSet<string> DefaultSerials = new(StringComparer.OrdinalIgnoreCase)
    {
        "0001", "0000", "1", "123456", "12345678", "ABCDEF", "000000000000",
    };

    private static readonly Regex VidPidRegex = new(
        @"VID_([0-9A-F]{4})[&+]PID_([0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex MiRegex = new(
        @"&MI_([0-9A-F]{2})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex FtdiRegex = new(
        @"^FTDIBUS\\VID_[0-9A-F]{4}\+PID_[0-9A-F]{4}\+([^\\]+)\\[^\\]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex MacRegex = new(
        @"^([0-9A-F]{2}:){5}[0-9A-F]{2}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool TryParseVidPid(DeviceRecord record, out string vid, out string pid)
        => TryParseVidPid(record.InstanceId, out vid, out pid);

    public static bool TryParseVidPid(string instanceId, out string vid, out string pid)
    {
        var m = VidPidRegex.Match(instanceId);
        if (!m.Success)
        {
            vid = pid = string.Empty;
            return false;
        }
        vid = m.Groups[1].Value.ToUpperInvariant();
        pid = m.Groups[2].Value.ToUpperInvariant();
        return true;
    }

    public static DeviceKey Resolve(DeviceRecord record)
    {
        var fallback = new DeviceKey(record.InstanceId, false);
        if (!TryParseVidPid(record.InstanceId, out var vid, out var pid))
            return fallback;

        // 규칙 2: VID/PID가 같은 상위 장치까지만 후보로 삼는다
        var candidates = new List<string> { record.InstanceId };
        foreach (var ancestor in record.Ancestors)
        {
            if (!TryParseVidPid(ancestor, out var v, out var p) || v != vid || p != pid)
                break;
            candidates.Add(ancestor);
        }

        // 규칙 4: 인터페이스 번호는 가장 가까운 후보의 값
        string? mi = null;
        foreach (var c in candidates)
        {
            var m = MiRegex.Match(c);
            if (m.Success)
            {
                mi = m.Groups[1].Value.ToUpperInvariant();
                break;
            }
        }

        // 규칙 1, 3: USB 열거자 시리얼을 우선하고, 없으면 FTDIBUS의 + 구분 시리얼
        var serial = candidates.Select(UsbSerial).FirstOrDefault(s => s is not null);
        if (serial is null && !candidates.Any(HasDefaultUsbSerial))
            serial = FtdiSerial(record.InstanceId); // 상위 USB가 기본 시리얼이면 FTDI 값도 믿지 않는다 (규칙 7)
        if (serial is null)
            return fallback; // 규칙 8

        serial = serial.ToUpperInvariant();
        var suffix = mi is null ? string.Empty : ":" + mi;
        // 규칙 6: MAC 형식이면 VID/PID를 키에서 뺀다
        if (MacRegex.IsMatch(serial))
            return new DeviceKey($"MAC:{serial}{suffix}", true);
        return new DeviceKey($"{vid}:{pid}:{serial}{suffix}", true);
    }

    private static string? UsbSerial(string instanceId)
    {
        if (!instanceId.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase))
            return null;
        var last = instanceId[(instanceId.LastIndexOf('\\') + 1)..];
        return IsUsableSerial(last) ? last : null;
    }

    private static string? FtdiSerial(string instanceId)
    {
        var m = FtdiRegex.Match(instanceId);
        if (!m.Success)
            return null;
        var s = m.Groups[1].Value;
        if (!IsUsableSerial(s))
            return null;
        // 끝의 채널 문자(A-D)를 뗀 값이 기본 시리얼이어도 제외한다
        if (s.Length > 1 && "ABCDabcd".Contains(s[^1]) && DefaultSerials.Contains(s[..^1]))
            return null;
        return s;
    }

    private static bool HasDefaultUsbSerial(string instanceId)
    {
        if (!instanceId.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase))
            return false;
        var last = instanceId[(instanceId.LastIndexOf('\\') + 1)..];
        return DefaultSerials.Contains(last);
    }

    // 규칙 1: '&'가 있으면 Windows가 만든 값이고, 규칙 7: 기본 시리얼은 제외
    private static bool IsUsableSerial(string s)
        => s.Length > 0 && !s.Contains('&') && !DefaultSerials.Contains(s);
}
