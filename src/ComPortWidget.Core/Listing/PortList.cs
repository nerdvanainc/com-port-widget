using System.Text.RegularExpressions;
using ComPortWidget.Core.Devices;

namespace ComPortWidget.Core.Listing;

/// <summary>위젯이 직접 관측한 인식·해제 시각. 인식 시간은 Windows 값과 이 값 중 늦은 쪽을 쓴다(해제 시각은 Windows 값이 우선).</summary>
public sealed record ObservedTimes(DateTimeOffset? Arrival, DateTimeOffset? Removal);

/// <summary>목록의 한 줄. Time은 연결 장비면 인식 시간, 미연결이면 해제 시간(없으면 인식 시간)이다.</summary>
public sealed record PortRow(
    DeviceRecord Record,
    DeviceKey Key,
    string? Memo,
    string Vendor,
    string Description,
    DateTimeOffset? Time,
    bool IsHighlighted,
    bool IsBootArrival,
    bool HasRemovalTime);

public sealed record PortList(IReadOnlyList<PortRow> Connected, IReadOnlyList<PortRow> Disconnected);

/// <summary>기록 목록에서 정렬·그룹·하이라이트가 적용된 표시용 목록을 만든다.</summary>
public static class PortListBuilder
{
    private static readonly TimeSpan BootGrace = TimeSpan.FromSeconds(90);
    // 부팅 시각 기록과 장치 인식 시각이 몇 초 어긋나는 일이 있어, 기준보다 이 정도 이른 인식도 부팅 시 인식으로 본다
    private static readonly TimeSpan BootEarlyTolerance = TimeSpan.FromSeconds(5);
    private static readonly Regex ComName = new(
        @"^COM(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <param name="memos">키는 DeviceKey.Value</param>
    /// <param name="observed">키는 InstanceId</param>
    /// <param name="baseline">마지막 부팅 시각과 전원 복귀 시각 중 늦은 값</param>
    public static PortList Build(
        IEnumerable<DeviceRecord> records,
        IReadOnlyDictionary<string, string> memos,
        IReadOnlyDictionary<string, ObservedTimes> observed,
        DateTimeOffset now,
        DateTimeOffset baseline,
        TimeSpan highlightWindow)
    {
        var connected = new List<PortRow>();
        var disconnected = new List<PortRow>();

        foreach (var r in records)
        {
            if (!ComName.IsMatch(r.PortName))
                continue; // CNCA0 같은 가상 포트 쌍 이름은 숨긴다

            observed.TryGetValue(r.InstanceId, out var obs);
            // 인식 시간은 Windows 값과 위젯 관측값 중 늦은 쪽이다 (실행 중 연결 이벤트 반영)
            var arrival = Later(r.LastArrival, obs?.Arrival);
            var removal = r.LastRemoval ?? obs?.Removal;
            var key = DeviceIdentity.Resolve(r);
            memos.TryGetValue(key.Value, out var memo);

            bool isBoot = false, highlighted = false, hasRemoval = false;
            DateTimeOffset? time;
            if (r.Present)
            {
                time = arrival;
                // 기준 시각 5초 전부터 90초 뒤까지의 인식은 부팅 시 인식이다
                isBoot = arrival is { } a && a >= baseline - BootEarlyTolerance && a <= baseline + BootGrace;
                highlighted = !isBoot && arrival is { } h && now - h <= highlightWindow;
            }
            else
            {
                hasRemoval = removal is not null;
                time = removal ?? arrival;
            }

            var row = new PortRow(r, key, memo, VendorTable.DisplayVendor(r), VendorTable.DisplayDescription(r),
                time, highlighted, isBoot, hasRemoval);
            (r.Present ? connected : disconnected).Add(row);
        }

        return new PortList(Sort(connected), Sort(disconnected));
    }

    // 시간 내림차순(없으면 맨 뒤), 같으면 COM 번호 오름차순
    private static IReadOnlyList<PortRow> Sort(List<PortRow> rows)
        => rows.OrderBy(r => r.Time is null)
               .ThenByDescending(r => r.Time ?? DateTimeOffset.MinValue)
               .ThenBy(r => ComNumber(r.Record.PortName))
               .ToList();

    private static DateTimeOffset? Later(DateTimeOffset? a, DateTimeOffset? b)
        => a is null ? b : b is null ? a : (a >= b ? a : b);

    private static int ComNumber(string portName)
        => int.TryParse(ComName.Match(portName).Groups[1].Value, out var n) ? n : int.MaxValue;
}
