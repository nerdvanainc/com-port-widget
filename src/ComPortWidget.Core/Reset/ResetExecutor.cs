using ComPortWidget.Core.Devices;

namespace ComPortWidget.Core.Reset;

/// <summary>정리 실행에 필요한 시스템 접근. 테스트는 가짜 구현을 쓴다.</summary>
public interface IResetBackend
{
    IReadOnlyList<DeviceRecord> GetRecords();
    ISet<int> GetReservedPorts();
    ISet<int> GetOtherHolders();
    void WriteBackup(ResetPlan plan, IReadOnlyList<DeviceRecord> targets);
    bool IsPresent(string instanceId);
    /// <summary>장치 기록을 삭제한다. 재부팅 후에 끝나는 경우 true(기록이 아직 남아 있을 수 있다).</summary>
    bool RemoveDevice(string instanceId);
    void ReleasePort(int number);
}

public enum ResetItemStatus { Done, SkippedNowConnected, SkippedNotEligible, Failed, DonePendingReboot }

public sealed record ResetItemResult(string Target, ResetItemStatus Status, string? Detail, string? Error = null);

public sealed record ResetResult(bool Aborted, string? AbortReason, IReadOnlyList<ResetItemResult> Items, string? AbortError = null);

/// <summary>
/// 안전장치를 강제하는 정리 실행 흐름.
/// 계획 파일을 믿지 않고 현재 상태로 다시 검증하며, 연결된 장치는 삭제하지 않는다.
/// </summary>
public sealed class ResetExecutor(IResetBackend backend)
{
    public ResetResult Execute(ResetPlan plan)
    {
        List<DeviceRecord> working;
        ISet<int> reserved, otherHolders;
        try
        {
            working = backend.GetRecords().ToList();
            reserved = backend.GetReservedPorts();
            otherHolders = backend.GetOtherHolders();
        }
        catch (Exception ex)
        {
            return Abort("reset.abortReadState", ex.Message, []);
        }

        // 검증은 시작 시점의 스냅샷 기준이고, 번호 해제 판단은 삭제에 따라 줄어드는 working 기준이다.
        var snapshot = working.ToList();
        var instanceIds = plan.InstanceIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var ports = plan.ReservedOnlyPorts.Distinct().ToList();

        var targets = new List<DeviceRecord>();
        foreach (var id in instanceIds)
        {
            var rec = FindEligible(snapshot, id);
            if (rec is not null && !rec.Present) targets.Add(rec);
        }

        try
        {
            backend.WriteBackup(plan, targets);
        }
        catch (Exception ex)
        {
            return Abort("reset.abortBackupFailed", ex.Message, []);
        }

        var items = new List<ResetItemResult>();

        foreach (var id in instanceIds)
        {
            var rec = FindEligible(snapshot, id);
            if (rec is null)
            {
                items.Add(new(id, ResetItemStatus.SkippedNotEligible, "reset.notEligible"));
                continue;
            }
            if (rec.Present)
            {
                items.Add(new(id, ResetItemStatus.SkippedNowConnected, "reset.nowConnected"));
                continue;
            }

            // 삭제 직전 재확인. 확인 자체가 실패하면 삭제하지 않고 별도 사유로 중단한다.
            bool present;
            try { present = backend.IsPresent(rec.InstanceId); }
            catch (Exception ex)
            {
                return Abort("reset.abortPresenceUnknown", id + ": " + ex.Message, items);
            }
            if (present)
                return Abort("reset.abortPresent", id, items);

            bool pendingReboot;
            try
            {
                pendingReboot = backend.RemoveDevice(rec.InstanceId);
            }
            catch (Exception ex)
            {
                items.Add(new(id, ResetItemStatus.Failed, "reset.removeFailed", ex.Message));
                continue;
            }

            if (pendingReboot)
            {
                // 기록이 재부팅 전까지 남을 수 있으므로 번호는 예약한 채로 둔다. 재부팅 후 다시 정리해야 한다
                items.Add(new(id, ResetItemStatus.DonePendingReboot, "reset.donePendingReboot"));
                continue;
            }

            working.RemoveAll(r => string.Equals(r.InstanceId, rec.InstanceId, StringComparison.OrdinalIgnoreCase));
            var number = ResetPlanner.ParsePortNumber(rec.PortName)!.Value;

            var held = otherHolders.Contains(number) || HeldByRecord(working, number);
            if (held || !reserved.Contains(number))
            {
                items.Add(new(id, ResetItemStatus.Done,
                    held ? "reset.doneNumberKept" : "reset.doneRecordRemoved"));
                continue;
            }
            try
            {
                backend.ReleasePort(number);
                items.Add(new(id, ResetItemStatus.Done, "reset.doneNumberReleased"));
            }
            catch (Exception ex)
            {
                items.Add(new(id, ResetItemStatus.Failed,
                    "reset.releaseFailedAfterRemoval", ex.Message));
            }
        }

        foreach (var number in ports)
        {
            var target = "COM" + number;
            if (!reserved.Contains(number) || otherHolders.Contains(number) || HeldByRecord(snapshot, number))
            {
                items.Add(new(target, ResetItemStatus.SkippedNotEligible, "reset.reservedNotEligible"));
                continue;
            }
            try
            {
                backend.ReleasePort(number);
                items.Add(new(target, ResetItemStatus.Done, "reset.doneReleasedOnly"));
            }
            catch (Exception ex)
            {
                items.Add(new(target, ResetItemStatus.Failed, "reset.releaseFailed", ex.Message));
            }
        }

        return new ResetResult(false, null, items);
    }

    private static ResetResult Abort(string reasonKey, string? error, List<ResetItemResult> items) =>
        new(true, reasonKey, items, error);

    // PortName이 COMn인 기록만 대상이다. 연결 여부는 호출부가 판단한다.
    private static DeviceRecord? FindEligible(List<DeviceRecord> records, string id) =>
        records.FirstOrDefault(r =>
            string.Equals(r.InstanceId, id, StringComparison.OrdinalIgnoreCase)
            && ResetPlanner.ParsePortNumber(r.PortName) is not null);

    private static bool HeldByRecord(List<DeviceRecord> records, int number) =>
        records.Any(r => ResetPlanner.ParsePortNumber(r.PortName) == number);
}
