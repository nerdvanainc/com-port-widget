using ComPortWidget.Core.Devices;
using ComPortWidget.Core.Listing;

namespace ComPortWidget.Core.Tests;

public class ObservedUpdaterTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyDictionary<string, ObservedTimes> Empty = new Dictionary<string, ObservedTimes>();

    private static DeviceRecord Rec(string id, bool present)
        => new(id, "COM3", present, null, null, null, null, Array.Empty<string>());

    private static ScanState State(string[] present, string[] absent)
        => new(new HashSet<string>(present), new HashSet<string>(absent));

    private static ObservedUpdate Step(ObservedUpdate? prev, DateTimeOffset now, params DeviceRecord[] scan)
        => ObservedUpdater.Update(prev?.Observed ?? Empty, prev?.State ?? ScanState.Empty(), scan, now);

    [Fact]
    public void First_scan_records_nothing_and_remembers_what_it_saw()
    {
        var r = Step(null, Now, Rec("A", true), Rec("B", false));
        Assert.Empty(r.Observed);
        Assert.Equal(new[] { "A" }, r.State.Present);
        Assert.Equal(new[] { "B" }, r.State.Absent);
    }

    [Fact]
    public void Absent_to_present_records_arrival()
    {
        var r = Step(Step(null, Now, Rec("A", false)), Now.AddSeconds(30), Rec("A", true));
        Assert.Equal(Now.AddSeconds(30), r.Observed["A"].Arrival);
        Assert.Null(r.Observed["A"].Removal);
    }

    [Fact]
    public void Present_absent_present_records_removal_then_arrival()
    {
        var a = Step(null, Now, Rec("A", true));
        var b = Step(a, Now.AddMinutes(1), Rec("A", false));
        Assert.Equal(Now.AddMinutes(1), b.Observed["A"].Removal);
        Assert.Null(b.Observed["A"].Arrival);
        var c = Step(b, Now.AddMinutes(2), Rec("A", true));
        Assert.Equal(Now.AddMinutes(2), c.Observed["A"].Arrival);
        Assert.Equal(Now.AddMinutes(1), c.Observed["A"].Removal);
    }

    [Fact]
    public void Present_to_absent_keeps_stored_arrival()
    {
        var earlier = Now.AddHours(-1);
        var stored = new Dictionary<string, ObservedTimes> { ["A"] = new(earlier, null) };
        var r = ObservedUpdater.Update(stored, State(new[] { "A" }, Array.Empty<string>()), new[] { Rec("A", false) }, Now);
        Assert.Equal(earlier, r.Observed["A"].Arrival);
        Assert.Equal(Now, r.Observed["A"].Removal);
    }

    [Fact]
    public void Unchanged_state_keeps_stored_values()
    {
        var stored = new Dictionary<string, ObservedTimes> { ["A"] = new(Now.AddHours(-2), Now.AddHours(-1)) };
        var r = ObservedUpdater.Update(stored, State(Array.Empty<string>(), new[] { "A" }), new[] { Rec("A", false) }, Now);
        Assert.Equal(stored["A"], r.Observed["A"]);
    }

    [Fact]
    public void Empty_first_scan_then_normal_scan_records_nothing()
    {
        var first = Step(null, Now);
        var second = Step(first, Now.AddSeconds(30), Rec("A", true), Rec("B", true));
        Assert.Empty(second.Observed);
    }

    [Fact]
    public void Record_missing_from_first_scan_then_present_records_nothing()
    {
        var first = Step(null, Now, Rec("B", true));
        var second = Step(first, Now.AddSeconds(30), Rec("A", true), Rec("B", true));
        Assert.Empty(second.Observed);
    }

    [Fact]
    public void Empty_scan_mid_session_then_normal_scan_marks_nothing()
    {
        var a = Step(null, Now, Rec("A", true), Rec("B", true));
        var empty = Step(a, Now.AddSeconds(30));
        var back = Step(empty, Now.AddSeconds(60), Rec("A", true), Rec("B", true));
        Assert.Empty(back.Observed);
    }

    [Fact]
    public void Record_skipped_once_gets_no_false_arrival_and_keeps_removal()
    {
        var removal = Now.AddHours(-3);
        var stored = new Dictionary<string, ObservedTimes> { ["A"] = new(null, removal) };
        var skipped = ObservedUpdater.Update(stored, State(new[] { "A" }, Array.Empty<string>()), new[] { Rec("B", true) }, Now);
        var back = Step(skipped, Now.AddSeconds(30), Rec("A", true), Rec("B", true));
        Assert.Equal(new ObservedTimes(null, removal), back.Observed["A"]);
    }

    [Fact]
    public void Record_skipped_then_seen_absent_records_removal()
    {
        var skipped = Step(Step(null, Now, Rec("A", true), Rec("B", true)), Now.AddSeconds(30), Rec("B", true));
        var gone = Step(skipped, Now.AddSeconds(60), Rec("A", false), Rec("B", true));
        Assert.Equal(Now.AddSeconds(60), gone.Observed["A"].Removal);
        Assert.DoesNotContain("A", gone.State.Present);
    }

    [Fact]
    public void Entries_for_records_missing_from_scan_are_kept()
    {
        var stored = new Dictionary<string, ObservedTimes> { ["GONE"] = new(Now.AddDays(-1), null), ["A"] = new(Now, null) };
        var r = ObservedUpdater.Update(stored, ScanState.Empty(), new[] { Rec("A", true) }, Now);
        Assert.True(r.Observed.ContainsKey("GONE"));
        Assert.True(r.Observed.ContainsKey("A"));
    }

    [Fact]
    public void Entries_without_times_newer_than_180_days_are_dropped()
    {
        var stored = new Dictionary<string, ObservedTimes>
        {
            ["OLD"] = new(Now.AddDays(-181), Now.AddDays(-200)),
            ["EDGE"] = new(Now.AddDays(-179), null),
            ["REMOVAL_NEW"] = new(Now.AddDays(-400), Now.AddDays(-10)),
            ["EMPTY"] = new(null, null),
        };
        var r = ObservedUpdater.Update(stored, ScanState.Empty(), Array.Empty<DeviceRecord>(), Now);
        Assert.False(r.Observed.ContainsKey("OLD"));
        Assert.False(r.Observed.ContainsKey("EMPTY"));
        Assert.True(r.Observed.ContainsKey("EDGE"));
        Assert.True(r.Observed.ContainsKey("REMOVAL_NEW"));
    }

    [Fact]
    public void Does_not_modify_inputs()
    {
        var stored = new Dictionary<string, ObservedTimes>();
        var state = ScanState.Empty();
        ObservedUpdater.Update(stored, state, new[] { Rec("A", true) }, Now);
        Assert.Empty(stored);
        Assert.Empty(state.Present);
        Assert.Empty(state.Absent);
    }
}
