using ComPortWidget.Core.Devices;
using ComPortWidget.Core.Reset;
using ComPortWidget.Core.Text;

namespace ComPortWidget.Core.Tests;

public class ResetExecutorTests
{
    private sealed class FakeBackend : IResetBackend
    {
        public List<DeviceRecord> Records = new();
        public HashSet<int> Reserved = new();
        public HashSet<int> Others = new();
        public List<string> Calls = new();
        public bool BackupFails;
        public bool PresentThrows;
        public HashSet<string> BecomePresentOnCheck = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> RemoveFails = new(StringComparer.OrdinalIgnoreCase);
        public bool ReleaseFails;
        public HashSet<string> RebootPending = new(StringComparer.OrdinalIgnoreCase);
        public bool ReadThrows;

        public IReadOnlyList<DeviceRecord> GetRecords()
        {
            if (ReadThrows) throw new InvalidOperationException("read");
            return Records.ToList();
        }
        public ISet<int> GetReservedPorts() => Reserved;
        public ISet<int> GetOtherHolders() => Others;
        public void WriteBackup(ResetPlan plan, IReadOnlyList<DeviceRecord> targets)
        {
            Calls.Add("backup");
            if (BackupFails) throw new IOException("disk full");
        }
        public bool IsPresent(string id)
        {
            Calls.Add("present:" + id);
            if (PresentThrows) throw new InvalidOperationException("present");
            return BecomePresentOnCheck.Contains(id);
        }
        public bool RemoveDevice(string id)
        {
            Calls.Add("remove:" + id);
            if (RemoveFails.Contains(id)) throw new InvalidOperationException("fail");
            if (RebootPending.Contains(id)) return true; // 재부팅 후에 삭제가 끝난다. 기록은 남는다
            Records.RemoveAll(r => r.InstanceId.Equals(id, StringComparison.OrdinalIgnoreCase));
            return false;
        }
        public void ReleasePort(int n)
        {
            Calls.Add("release:" + n);
            if (ReleaseFails) throw new InvalidOperationException("fail");
        }
    }

    private static DeviceRecord Rec(string id, string port, bool present = false) =>
        new(id, port, present, null, null, null, null, []);

    private static ResetPlan Plan(string[]? ids = null, int[]? ports = null) =>
        new(ids ?? [], ports ?? []);

    [Fact]
    public void Execute_writes_backup_before_any_change()
    {
        var b = new FakeBackend { Records = { Rec("USB\\A", "COM5") }, Reserved = { 5 } };
        new ResetExecutor(b).Execute(Plan(["USB\\A"]));
        Assert.Equal("backup", b.Calls[0]);
        Assert.True(b.Calls.IndexOf("backup") < b.Calls.IndexOf("remove:USB\\A"));
    }

    [Fact]
    public void Execute_aborts_without_changes_when_backup_fails()
    {
        var b = new FakeBackend { Records = { Rec("USB\\A", "COM5") }, Reserved = { 5 }, BackupFails = true };
        var r = new ResetExecutor(b).Execute(Plan(["USB\\A"]));
        Assert.True(r.Aborted);
        Assert.Equal("reset.abortBackupFailed", r.AbortReason);
        Assert.Equal("disk full", r.AbortError);
        Assert.Empty(r.Items);
        Assert.Equal(["backup"], b.Calls);
    }

    [Fact]
    public void Execute_skips_record_that_became_present()
    {
        var b = new FakeBackend { Records = { Rec("USB\\A", "COM5", present: true) }, Reserved = { 5 } };
        var r = new ResetExecutor(b).Execute(Plan(["USB\\A"]));
        Assert.False(r.Aborted);
        Assert.Equal(ResetItemStatus.SkippedNowConnected, r.Items[0].Status);
        Assert.Equal("reset.nowConnected", r.Items[0].Detail);
        Assert.DoesNotContain(b.Calls, c => c.StartsWith("remove:"));
        Assert.DoesNotContain(b.Calls, c => c.StartsWith("release:"));
    }

    [Fact]
    public void Execute_rejects_instance_not_in_ports_records()
    {
        var b = new FakeBackend { Records = { Rec("USB\\A", "COM5") }, Reserved = { 5 } };
        var r = new ResetExecutor(b).Execute(Plan(["ROOT\\ANYTHING", "USB\\NOPORT"]));
        Assert.All(r.Items, i => Assert.Equal(ResetItemStatus.SkippedNotEligible, i.Status));
        Assert.All(r.Items, i => Assert.Equal("reset.notEligible", i.Detail));
        Assert.DoesNotContain(b.Calls, c => c.StartsWith("remove:"));
    }

    [Fact]
    public void Execute_rejects_record_whose_port_name_is_not_com()
    {
        var b = new FakeBackend { Records = { Rec("USB\\A", "LPT1") } };
        var r = new ResetExecutor(b).Execute(Plan(["USB\\A"]));
        Assert.Equal(ResetItemStatus.SkippedNotEligible, r.Items[0].Status);
        Assert.DoesNotContain(b.Calls, c => c.StartsWith("remove:"));
    }

    [Fact]
    public void Execute_aborts_when_present_at_removal_step()
    {
        var b = new FakeBackend
        {
            Records = { Rec("USB\\A", "COM5"), Rec("USB\\B", "COM6"), Rec("USB\\C", "COM7") },
            Reserved = { 5, 6, 7 },
            BecomePresentOnCheck = { "USB\\B" },
        };
        var r = new ResetExecutor(b).Execute(Plan(["USB\\A", "USB\\B", "USB\\C"]));
        Assert.True(r.Aborted);
        Assert.Equal("reset.abortPresent", r.AbortReason);
        Assert.Equal("USB\\B", r.AbortError);
        Assert.Single(r.Items);
        Assert.Equal(ResetItemStatus.Done, r.Items[0].Status);
        Assert.DoesNotContain("remove:USB\\B", b.Calls);
        Assert.DoesNotContain("remove:USB\\C", b.Calls);
        Assert.DoesNotContain("release:6", b.Calls);
    }

    [Fact]
    public void Execute_aborts_when_is_present_throws()
    {
        var b = new FakeBackend { Records = { Rec("USB\\A", "COM5"), Rec("USB\\B", "COM6") }, Reserved = { 5, 6 }, PresentThrows = true };
        var r = new ResetExecutor(b).Execute(Plan(["USB\\A", "USB\\B"]));
        Assert.True(r.Aborted);
        Assert.Equal("reset.abortPresenceUnknown", r.AbortReason);
        Assert.Contains("USB\\A", r.AbortError);
        Assert.Contains("present", r.AbortError);
        Assert.Empty(r.Items);
        Assert.DoesNotContain(b.Calls, c => c.StartsWith("remove:"));
        Assert.DoesNotContain("present:USB\\B", b.Calls);
    }

    [Fact]
    public void Execute_removes_device_then_releases_number()
    {
        var b = new FakeBackend { Records = { Rec("USB\\A", "COM5") }, Reserved = { 5 } };
        var r = new ResetExecutor(b).Execute(Plan(["USB\\A"]));
        Assert.Equal(ResetItemStatus.Done, r.Items[0].Status);
        Assert.Equal("reset.doneNumberReleased", r.Items[0].Detail);
        Assert.Equal(["backup", "present:USB\\A", "remove:USB\\A", "release:5"], b.Calls);
    }

    [Fact]
    public void Execute_matches_instance_id_case_insensitively_and_once()
    {
        var b = new FakeBackend { Records = { Rec("USB\\A", "COM5") }, Reserved = { 5 } };
        var r = new ResetExecutor(b).Execute(Plan(["usb\\a", "USB\\A"]));
        Assert.Single(r.Items);
        Assert.Equal(1, b.Calls.Count(c => c.StartsWith("remove:")));
    }

    [Fact]
    public void Execute_keeps_reservation_when_another_record_holds_number()
    {
        var b = new FakeBackend { Records = { Rec("USB\\A", "COM5"), Rec("USB\\B", "COM5", present: true) }, Reserved = { 5 } };
        var r = new ResetExecutor(b).Execute(Plan(["USB\\A"]));
        Assert.Equal(ResetItemStatus.Done, r.Items[0].Status);
        Assert.Equal("reset.doneNumberKept", r.Items[0].Detail);
        Assert.Contains("remove:USB\\A", b.Calls);
        Assert.DoesNotContain("release:5", b.Calls);
    }

    [Fact]
    public void Execute_keeps_reservation_when_other_holder_has_number()
    {
        var b = new FakeBackend { Records = { Rec("USB\\A", "COM5") }, Reserved = { 5 }, Others = { 5 } };
        new ResetExecutor(b).Execute(Plan(["USB\\A"]));
        Assert.DoesNotContain("release:5", b.Calls);
    }

    [Fact]
    public void Execute_releases_number_when_all_holders_removed()
    {
        var b = new FakeBackend { Records = { Rec("USB\\A", "COM5"), Rec("USB\\B", "COM5") }, Reserved = { 5 } };
        var r = new ResetExecutor(b).Execute(Plan(["USB\\A", "USB\\B"]));
        Assert.All(r.Items, i => Assert.Equal(ResetItemStatus.Done, i.Status));
        Assert.Equal(1, b.Calls.Count(c => c == "release:5"));
        Assert.True(b.Calls.IndexOf("release:5") > b.Calls.IndexOf("remove:USB\\B"));
    }

    [Fact]
    public void Execute_removes_record_without_release_when_number_not_reserved()
    {
        var b = new FakeBackend { Records = { Rec("USB\\A", "COM5") } };
        var r = new ResetExecutor(b).Execute(Plan(["USB\\A"]));
        Assert.Equal(ResetItemStatus.Done, r.Items[0].Status);
        Assert.DoesNotContain("release:5", b.Calls);
    }

    [Fact]
    public void Execute_releases_reserved_only_port()
    {
        var b = new FakeBackend { Reserved = { 9 } };
        var r = new ResetExecutor(b).Execute(Plan(ports: [9, 9]));
        Assert.Single(r.Items);
        Assert.Equal(ResetItemStatus.Done, r.Items[0].Status);
        Assert.Equal("reset.doneReleasedOnly", r.Items[0].Detail);
        Assert.Equal(["backup", "release:9"], b.Calls);
    }

    [Fact]
    public void Execute_reports_release_failure_for_reserved_only_port()
    {
        var b = new FakeBackend { Reserved = { 9 }, ReleaseFails = true };
        var r = new ResetExecutor(b).Execute(Plan(ports: [9]));
        Assert.Equal(ResetItemStatus.Failed, r.Items[0].Status);
        Assert.Equal("reset.releaseFailed", r.Items[0].Detail);
        Assert.Equal("fail", r.Items[0].Error);
    }

    [Fact]
    public void Execute_result_keys_exist_in_both_languages()
    {
        var keys = Strings.Keys.Where(k => k.StartsWith("reset.")).ToList();
        Assert.NotEmpty(keys);
        foreach (var k in keys)
        {
            Assert.False(string.IsNullOrWhiteSpace(Strings.Get(k, "ko")));
            Assert.False(string.IsNullOrWhiteSpace(Strings.Get(k, "en")));
        }
    }

    [Fact]
    public void Execute_rejects_reserved_port_not_reserved_anymore()
    {
        var b = new FakeBackend();
        var r = new ResetExecutor(b).Execute(Plan(ports: [9]));
        Assert.Equal(ResetItemStatus.SkippedNotEligible, r.Items[0].Status);
        Assert.Equal("reset.reservedNotEligible", r.Items[0].Detail);
        Assert.DoesNotContain("release:9", b.Calls);
    }

    [Fact]
    public void Execute_rejects_reserved_port_held_by_record()
    {
        var b = new FakeBackend { Records = { Rec("USB\\A", "COM9", present: true) }, Reserved = { 9 } };
        var r = new ResetExecutor(b).Execute(Plan(ports: [9]));
        Assert.Equal(ResetItemStatus.SkippedNotEligible, r.Items[0].Status);
        Assert.DoesNotContain("release:9", b.Calls);
    }

    [Fact]
    public void Execute_rejects_reserved_port_held_by_other_holder()
    {
        var b = new FakeBackend { Reserved = { 9 }, Others = { 9 } };
        var r = new ResetExecutor(b).Execute(Plan(ports: [9]));
        Assert.Equal(ResetItemStatus.SkippedNotEligible, r.Items[0].Status);
        Assert.DoesNotContain("release:9", b.Calls);
    }

    [Fact]
    public void Execute_continues_after_single_item_failure()
    {
        var b = new FakeBackend
        {
            Records = { Rec("USB\\A", "COM5"), Rec("USB\\B", "COM6") },
            Reserved = { 5, 6 },
            RemoveFails = { "USB\\A" },
        };
        var r = new ResetExecutor(b).Execute(Plan(["USB\\A", "USB\\B"]));
        Assert.False(r.Aborted);
        Assert.Equal(ResetItemStatus.Failed, r.Items[0].Status);
        Assert.Equal("reset.removeFailed", r.Items[0].Detail);
        Assert.Equal("fail", r.Items[0].Error);
        Assert.Equal(ResetItemStatus.Done, r.Items[1].Status);
    }

    [Fact]
    public void Execute_does_not_release_number_when_removal_failed()
    {
        var b = new FakeBackend { Records = { Rec("USB\\A", "COM5") }, Reserved = { 5 }, RemoveFails = { "USB\\A" } };
        new ResetExecutor(b).Execute(Plan(["USB\\A"]));
        Assert.DoesNotContain("release:5", b.Calls);
    }

    [Fact]
    public void Execute_marks_failed_when_release_throws_after_removal()
    {
        var b = new FakeBackend
        {
            Records = { Rec("USB\\A", "COM5"), Rec("USB\\B", "COM6") },
            Reserved = { 5, 6 },
            ReleaseFails = true,
        };
        var r = new ResetExecutor(b).Execute(Plan(["USB\\A", "USB\\B"]));
        Assert.False(r.Aborted);
        Assert.All(r.Items, i => Assert.Equal(ResetItemStatus.Failed, i.Status));
        Assert.All(r.Items, i => Assert.Equal("reset.releaseFailedAfterRemoval", i.Detail));
        Assert.Equal("fail", r.Items[0].Error);
        Assert.Contains("remove:USB\\A", b.Calls);
        Assert.Contains("remove:USB\\B", b.Calls);
    }

    [Fact]
    public void Execute_aborts_with_no_changes_when_reading_state_throws()
    {
        var b = new FakeBackend { ReadThrows = true };
        var r = new ResetExecutor(b).Execute(Plan(["USB\\A"]));
        Assert.True(r.Aborted);
        Assert.Equal("reset.abortReadState", r.AbortReason);
        Assert.Equal("read", r.AbortError);
        Assert.Empty(r.Items);
        Assert.Empty(b.Calls);
    }

    [Fact]
    public void Execute_reports_pending_reboot_and_keeps_the_number_reserved()
    {
        var b = new FakeBackend
        {
            Records = { Rec("USB\\A", "COM5") },
            Reserved = { 5 },
            RebootPending = { "USB\\A" },
        };
        var r = new ResetExecutor(b).Execute(Plan(["USB\\A"]));
        Assert.False(r.Aborted);
        var item = Assert.Single(r.Items);
        Assert.Equal(ResetItemStatus.DonePendingReboot, item.Status);
        Assert.Equal("reset.donePendingReboot", item.Detail);
        Assert.DoesNotContain("release:5", b.Calls);
    }

    [Fact]
    public void Execute_pending_reboot_does_not_block_other_items()
    {
        var b = new FakeBackend
        {
            Records = { Rec("USB\\A", "COM5"), Rec("USB\\B", "COM6") },
            Reserved = { 5, 6 },
            RebootPending = { "USB\\A" },
        };
        var r = new ResetExecutor(b).Execute(Plan(["USB\\A", "USB\\B"]));
        Assert.Equal(ResetItemStatus.DonePendingReboot, r.Items[0].Status);
        Assert.Equal(ResetItemStatus.Done, r.Items[1].Status);
        Assert.Contains("release:6", b.Calls);
    }

    [Fact]
    public void Execute_pending_reboot_on_a_shared_number_keeps_it_and_releases_nothing()
    {
        var b = new FakeBackend
        {
            Records = { Rec("USB\\A", "COM5"), Rec("USB\\B", "COM5") },
            Reserved = { 5 },
            Others = { 5 },
            RebootPending = { "USB\\A" },
        };
        var r = new ResetExecutor(b).Execute(Plan(["USB\\A"]));
        Assert.Equal(ResetItemStatus.DonePendingReboot, Assert.Single(r.Items).Status);
        Assert.DoesNotContain("release:5", b.Calls);
    }
}
