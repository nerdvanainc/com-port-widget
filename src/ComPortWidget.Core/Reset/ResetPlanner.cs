using ComPortWidget.Core.Devices;

namespace ComPortWidget.Core.Reset;

public enum ResetKind { UsbRecord, SameDeviceConnected, VirtualOrBluetooth, ReservedOnly }

/// <summary>정리 후보 한 건. InstanceId가 null이면 기록 없이 예약만 남은 번호이다.</summary>
public sealed record ResetCandidate(
    ResetKind Kind,
    int PortNumber,
    string? InstanceId,
    string? Memo,
    DateTimeOffset? LastSeen,
    bool DefaultSelected,
    bool ReleasesNumber);

/// <summary>사용자가 확정한 정리 대상. 승격 프로세스에서 다시 검증한다.</summary>
public sealed record ResetPlan(IReadOnlyList<string> InstanceIds, IReadOnlyList<int> ReservedOnlyPorts);

/// <summary>정리 범위 규칙. 판단이 불확실하면 기본 해제 쪽으로 분류한다.</summary>
public static class ResetPlanner
{
    public static IReadOnlyList<ResetCandidate> Build(
        IReadOnlyList<DeviceRecord> records,
        ISet<int> reserved,
        ISet<int> otherHolders,
        IReadOnlyDictionary<string, string> memos)
    {
        // 기록별 번호 (COMn이 아닌 이름은 후보에서 제외)
        var parsed = new List<(DeviceRecord Rec, int Port)>();
        foreach (var r in records)
        {
            var n = ParsePortNumber(r.PortName);
            if (n is int p)
                parsed.Add((r, p));
        }

        // 연결 중인 장비의 시리얼 기반 키 (키에 시리얼이 없으면 같은 장비 판단에 쓰지 않는다)
        var presentKeys = new HashSet<string>(StringComparer.Ordinal);
        // PortName이 COMn이 아니어도 연결 중인 장비는 연결된 것으로 본다
        foreach (var rec in records)
        {
            if (!rec.Present) continue;
            var key = DeviceIdentity.Resolve(rec);
            if (key.HasSerial)
                presentKeys.Add(key.Value);
        }

        var result = new List<ResetCandidate>();
        foreach (var (rec, port) in parsed)
        {
            if (rec.Present) continue;

            var key = DeviceIdentity.Resolve(rec);
            var kind = Classify(rec.InstanceId);
            var sameDevice = key.HasSerial && presentKeys.Contains(key.Value);
            if (sameDevice)
                kind = ResetKind.SameDeviceConnected;

            // 같은 번호를 쓰는 다른 기록(연결 여부 무관)이나 다른 장치가 있으면 번호를 해제하지 않는다
            var shared = otherHolders.Contains(port)
                || parsed.Any(o => o.Port == port && !string.Equals(o.Rec.InstanceId, rec.InstanceId, StringComparison.OrdinalIgnoreCase));

            memos.TryGetValue(key.Value, out var memo);
            result.Add(new ResetCandidate(
                kind, port, rec.InstanceId, memo, rec.LastRemoval ?? rec.LastArrival,
                // 상위 장치를 끝까지 못 읽은 기록은 같은 장비인지 확신할 수 없어 기본 선택하지 않는다
                DefaultSelected: kind == ResetKind.UsbRecord && !rec.AncestorsIncomplete,
                ReleasesNumber: !shared));
        }

        var usedPorts = parsed.Select(p => p.Port).ToHashSet();
        foreach (var port in reserved)
        {
            if (usedPorts.Contains(port) || otherHolders.Contains(port)) continue;
            result.Add(new ResetCandidate(ResetKind.ReservedOnly, port, null, null, null, false, true));
        }

        return result
            .OrderBy(c => c.PortNumber)
            .ThenBy(c => c.InstanceId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>선택한 후보로 예상 결과를 센다: 지울 기록 수와 해제할 번호 수(같은 번호는 한 번만 센다).</summary>
    public static (int Records, int Numbers) Summarize(IEnumerable<ResetCandidate> selected)
    {
        var list = selected.ToList();
        return (list.Count(c => c.InstanceId is not null),
            list.Where(c => c.ReleasesNumber).Select(c => c.PortNumber).Distinct().Count());
    }

    /// <summary>"COMn"(대소문자 무관, n은 양의 정수)만 번호로 바꾼다. 그 밖은 null.</summary>
    public static int? ParsePortNumber(string portName)
    {
        if (portName is null || portName.Length < 4
            || !portName.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
            return null;
        var digits = portName.AsSpan(3);
        foreach (var ch in digits)
            if (ch is < '0' or > '9') return null;
        return int.TryParse(digits, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var n) && n > 0 ? n : null;
    }

    // USB, FTDIBUS 열거자만 USB 기록으로 본다. 블루투스 포트도 상위가 USB일 수 있어 상위 장치는 보지 않는다.
    private static ResetKind Classify(string instanceId)
    {
        var sep = instanceId.IndexOf('\\');
        var enumerator = sep >= 0 ? instanceId[..sep] : instanceId;
        return enumerator.Equals("USB", StringComparison.OrdinalIgnoreCase)
            || enumerator.Equals("FTDIBUS", StringComparison.OrdinalIgnoreCase)
            ? ResetKind.UsbRecord
            : ResetKind.VirtualOrBluetooth;
    }
}
