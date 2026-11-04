using System.Diagnostics.Eventing.Reader;

namespace ComPortWidget.Platform;

/// <summary>부팅 시각과 마지막 재개(절전·최대 절전·빠른 시작 복귀) 시각을 구한다. 읽기 전용이다.</summary>
public static class PowerBaseline
{
    /// <summary>조회 범위. 이 안에 재개 기록이 없으면 null로 본다.</summary>
    private static readonly TimeSpan LookBack = TimeSpan.FromDays(14);

    public static DateTimeOffset GetBootTime() =>
        DateTimeOffset.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);

    /// <summary>
    /// System 로그에서 Power-Troubleshooter ID 1(절전 복귀)과 Kernel-Boot ID 27(BootType 1 = 빠른 시작, 2 = 최대 절전 복귀)의
    /// 가장 최근 시각을 돌려준다. ID 1은 깨어난 뒤에 기록되므로 TimeCreated 대신 WakeTime을 쓰고,
    /// 값이 없거나 이상하면 TimeCreated로 되돌아간다. 로그를 읽지 못하거나 기록이 없으면 null이며 예외를 던지지 않는다.
    /// </summary>
    public static DateTimeOffset? GetLastResumeTime()
    {
        try
        {
            var ms = (long)LookBack.TotalMilliseconds;
            var a = NewestTime(
                $"*[System[Provider[@Name='Microsoft-Windows-Power-Troubleshooter'] and EventID=1 and TimeCreated[timediff(@SystemTime) <= {ms}]]]",
                static (e, created) => ResolveWakeTime(e.Properties.Count > 1 ? e.Properties[1].Value : null, created, DateTimeOffset.Now));
            var b = NewestTime(
                $"*[System[Provider[@Name='Microsoft-Windows-Kernel-Boot'] and EventID=27 and TimeCreated[timediff(@SystemTime) <= {ms}]]]",
                static (e, created) => IsFastStartup(e) ? created : null);
            if (a is null) return b;
            if (b is null) return a;
            return a > b ? a : b;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// WakeTime(FILETIME)을 시각으로 바꾼다. 없거나 0이거나 미래이거나 createdAt보다 늦으면 createdAt을 돌려준다.
    /// </summary>
    internal static DateTimeOffset ResolveWakeTime(object? wakeValue, DateTimeOffset createdAt, DateTimeOffset now)
    {
        DateTimeOffset? wake = null;
        try
        {
            switch (wakeValue)
            {
                case DateTime dt: wake = new DateTimeOffset(dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt); break;
                case DateTimeOffset dto: wake = dto; break;
                case long l when l > 0: wake = DateTimeOffset.FromFileTime(l); break;
                case ulong ul when ul > 0 && ul <= long.MaxValue: wake = DateTimeOffset.FromFileTime((long)ul); break;
            }
        }
        catch (Exception)
        {
            wake = null;
        }
        if (wake is null) return createdAt;
        // 0(1601년)이나 비정상적으로 오래된 값은 거른다
        if (wake.Value.Year < 2000) return createdAt;
        if (wake.Value > now || wake.Value > createdAt) return createdAt;
        return wake.Value;
    }

    /// <summary>BootType: 0 = 일반 부팅, 1 = 빠른 시작, 2 = 최대 절전 복귀. 1과 2를 재개로 본다. 빠른 시작 판별은 실기 검증 항목이다.</summary>
    private static bool IsFastStartup(EventRecord e) =>
        e.Properties.Count > 0 && Convert.ToInt64(e.Properties[0].Value) is 1 or 2;

    private static DateTimeOffset? NewestTime(string xpath, Func<EventRecord, DateTimeOffset, DateTimeOffset?> select)
    {
        try
        {
            var query = new EventLogQuery("System", PathType.LogName, xpath) { ReverseDirection = true };
            using var reader = new EventLogReader(query);
            for (EventRecord? rec = reader.ReadEvent(); rec is not null; rec = reader.ReadEvent())
            {
                using (rec)
                {
                    if (rec.TimeCreated is not DateTime t) continue;
                    DateTimeOffset? r;
                    try { r = select(rec, new DateTimeOffset(t)); } catch (Exception) { r = null; }
                    if (r is not null) return r;
                }
            }
        }
        catch (Exception)
        {
            // 이 로그를 못 읽으면 다른 로그만으로 판단한다
        }
        return null;
    }
}
