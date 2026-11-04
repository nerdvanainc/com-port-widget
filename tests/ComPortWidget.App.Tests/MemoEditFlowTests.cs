using ComPortWidget.App;
using ComPortWidget.Core.Devices;

namespace ComPortWidget.App.Tests;

public class MemoEditFlowTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);

    private static DeviceRecord Rec(string serial, string port)
        => new(@"USB\VID_10C4&PID_EA60\" + serial, port, true, "테스트 장치", null, Now.AddHours(-1), null, Array.Empty<string>());

    private static WidgetViewModel Create(out RowViewModel a, out RowViewModel b)
    {
        var vm = new WidgetViewModel(new DemoDeviceSource(), null, () => Now.AddHours(-5), () => Now);
        vm.Apply(new[] { Rec("DEMOA", "COM3"), Rec("DEMOB", "COM4") }, Now.AddHours(-5));
        a = vm.Connected.Single(r => r.PortName == "COM3");
        b = vm.Connected.Single(r => r.PortName == "COM4");
        return vm;
    }

    [Fact]
    public void Begin_edit_on_other_row_saves_previous_and_updates_its_visible_memo()
    {
        var vm = Create(out var a, out var b);
        vm.BeginEdit(a);
        a.EditText = "새A";
        vm.BeginEdit(b);
        Assert.Equal("새A", vm.Data.Memos[a.Key.Value]);
        Assert.Equal("새A", a.Memo);
        Assert.True(a.HasMemo);
    }

    [Fact]
    public void Reediting_the_saved_row_starts_from_stored_memo_and_keeps_it_on_enter()
    {
        var vm = Create(out var a, out var b);
        vm.BeginEdit(a);
        a.EditText = "새A";
        vm.BeginEdit(b);          // A 저장, 갱신은 편집 중이라 미뤄진다
        vm.BeginEdit(a);          // 메뉴로 A를 다시 편집
        Assert.Equal("새A", a.EditText);
        vm.CommitEdit(a);         // Enter
        Assert.Equal("새A", vm.Data.Memos[a.Key.Value]);
    }

    [Fact]
    public void Commit_updates_visible_memo_even_while_menu_defers_rebuild()
    {
        var vm = Create(out var a, out _);
        vm.SetMenuOpen(true);
        vm.BeginEdit(a);
        a.EditText = "메모";
        vm.CommitEdit(a);
        Assert.Equal("메모", a.Memo);
        vm.SetMenuOpen(false);
        Assert.Equal("메모", vm.Connected.Single(r => r.PortName == "COM3").Memo);
    }
}
