using ComPortWidget.Core.Devices;

namespace ComPortWidget.Core.Listing;

/// <summary>이번 실행 중 조회에서 직접 본 상태. Present는 연결로 본 InstanceId, Absent는 미연결로 본 InstanceId.</summary>
public sealed record ScanState(HashSet<string> Present, HashSet<string> Absent)
{
    public static ScanState Empty() => new(new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));
}

/// <summary>갱신된 관측값과 갱신된 조회 상태.</summary>
public sealed record ObservedUpdate(Dictionary<string, ObservedTimes> Observed, ScanState State);

/// <summary>
/// 두 번의 조회 사이에서 위젯이 직접 본 인식·해제 시각을 갱신한다.
/// 인식 시각은 "이번 실행 중 미연결로 본 기록이 연결로 바뀐" 경우에만 기록한다.
/// 한 번도 본 적 없는 기록(첫 조회가 비었거나 그 기록이 빠졌던 경우)은 새 장치와 구분할 수 없으므로 기록하지 않고 Windows 값에 맡긴다.
/// 해제 시각은 연결로 본 기록이 미연결로 바뀐 경우에만 기록한다. 조회에서 빠진 기록은 "알 수 없음"으로 두어 아무것도 바꾸지 않는다.
/// </summary>
public static class ObservedUpdater
{
    /// <summary>이 기간보다 오래 새 시각이 없는 관측값은 버린다. 파일이 끝없이 커지는 것만 막는다.</summary>
    public static readonly TimeSpan RetentionLimit = TimeSpan.FromDays(180);

    /// <param name="stored">저장된 관측값. 키는 InstanceId. 이 사전은 바꾸지 않는다.</param>
    /// <param name="state">직전까지 본 상태. 이 객체는 바꾸지 않는다.</param>
    public static ObservedUpdate Update(
        IReadOnlyDictionary<string, ObservedTimes> stored,
        ScanState state,
        IReadOnlyList<DeviceRecord> current,
        DateTimeOffset now)
    {
        var present = new HashSet<string>(state.Present, StringComparer.Ordinal);
        var absent = new HashSet<string>(state.Absent, StringComparer.Ordinal);
        var observed = stored.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        foreach (var r in current)
        {
            if (r.Present)
            {
                if (absent.Remove(r.InstanceId))
                    observed[r.InstanceId] = Get(observed, r.InstanceId) with { Arrival = now };
                present.Add(r.InstanceId);
            }
            else
            {
                if (present.Remove(r.InstanceId))
                    observed[r.InstanceId] = Get(observed, r.InstanceId) with { Removal = now };
                absent.Add(r.InstanceId);
            }
        }

        // 조회에 없는 기록은 지우지 않고, 오래된 값만 정리한다 (초기화로 지워지는 기록의 시각도 남는다)
        var cutoff = now - RetentionLimit;
        foreach (var (id, t) in observed.ToList())
        {
            var newest = Newest(t);
            if (newest is null || newest < cutoff)
                observed.Remove(id);
        }
        return new ObservedUpdate(observed, new ScanState(present, absent));
    }

    private static ObservedTimes Get(Dictionary<string, ObservedTimes> d, string id)
        => d.TryGetValue(id, out var o) ? o : new ObservedTimes(null, null);

    private static DateTimeOffset? Newest(ObservedTimes t)
        => t.Arrival is null ? t.Removal : t.Removal is null ? t.Arrival : (t.Arrival >= t.Removal ? t.Arrival : t.Removal);
}
