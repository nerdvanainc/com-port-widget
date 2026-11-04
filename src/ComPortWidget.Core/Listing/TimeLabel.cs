using System.Globalization;
using ComPortWidget.Core.Text;

namespace ComPortWidget.Core.Listing;

/// <summary>목록 아랫줄에 쓰는 시간 문구.</summary>
public static class TimeLabel
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>방금 전 / N분 전 / 오늘 HH:mm / 어제 HH:mm / MM/dd 순으로 바뀐다. 미래 시각은 방금 전이다.</summary>
    public static string Format(DateTimeOffset? time, DateTimeOffset now, string language)
    {
        if (time is null)
            return Strings.Get("time.unknown", language);

        var local = time.Value.ToOffset(now.Offset); // 날짜 비교는 now의 오프셋 기준
        var diff = now - local;
        if (diff < TimeSpan.FromMinutes(1))
            return Strings.Get("time.just_now", language);
        if (diff < TimeSpan.FromHours(1))
            return string.Format(Inv, Strings.Get("time.minutes_ago", language), (int)diff.TotalMinutes);

        var today = now.Date;
        if (local.Date == today)
            return string.Format(Inv, Strings.Get("time.today", language), local.ToString("HH:mm", Inv));
        if (local.Date == today.AddDays(-1))
            return string.Format(Inv, Strings.Get("time.yesterday", language), local.ToString("HH:mm", Inv));
        return local.ToString("MM/dd", Inv);
    }

    /// <summary>한 줄에 표시할 문구. 부팅 인식과 미연결 줄은 별도 문구를 쓴다.</summary>
    public static string ForRow(PortRow row, DateTimeOffset now, string language)
    {
        if (row.IsBootArrival)
            return Strings.Get("time.boot", language);

        if (!row.Record.Present && row.Time is { } t)
        {
            var date = t.ToOffset(now.Offset).ToString("MM/dd", Inv);
            var key = row.HasRemovalTime ? "time.removed" : "time.last_seen";
            return string.Format(Inv, Strings.Get(key, language), date);
        }
        return Format(row.Time, now, language);
    }
}
