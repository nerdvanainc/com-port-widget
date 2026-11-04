using ComPortWidget.App;
using ComPortWidget.Core.Devices;

namespace ComPortWidget.App.Tests;

public class SettingsViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);

    private static DeviceRecord Rec(string serial, string port)
        => new(@"USB\VID_10C4&PID_EA60\" + serial, port, true, "테스트 장치", null, Now.AddMinutes(-3), null, Array.Empty<string>());

    private sealed class Fake
    {
        public List<string> Registered = new();
        public HashSet<string> Conflicts = new();
        public bool AutoStartState;
        public bool AutoStartWriteOk = true;

        public bool Register(string text)
        {
            if (Conflicts.Contains(text)) return false;
            Registered.Add(text);
            return true;
        }
        public bool ReadAuto() => AutoStartState;
        public bool WriteAuto(bool v)
        {
            if (!AutoStartWriteOk) return false;
            AutoStartState = v;
            return true;
        }
    }

    private static (WidgetViewModel Vm, SettingsViewModel Sv, Fake F) Create()
    {
        var vm = new WidgetViewModel(new DemoDeviceSource(), null, () => Now.AddHours(-5), () => Now);
        vm.Apply(new[] { Rec("DEMOA", "COM3"), Rec("DEMOB", "COM4") }, Now.AddHours(-5));
        var f = new Fake();
        var sv = new SettingsViewModel(vm, f.Register, f.ReadAuto, f.WriteAuto);
        return (vm, sv, f);
    }

    [Fact]
    public void Deleting_a_memo_removes_it_and_clears_the_visible_row()
    {
        var (vm, sv, _) = Create();
        var a = vm.Connected.Single(r => r.PortName == "COM3");
        vm.SetMemo(a.Key, "시험 메모");
        sv.ReloadMemos();
        Assert.Single(sv.Memos);
        Assert.Equal("시험 메모", vm.Connected.Single(r => r.PortName == "COM3").Memo);

        sv.DeleteMemo(sv.Memos[0]);

        Assert.Empty(sv.Memos);
        Assert.False(vm.Data.Memos.ContainsKey(a.Key.Value));
        Assert.Equal("", vm.Connected.Single(r => r.PortName == "COM3").Memo);
    }

    [Theory]
    [InlineData("0", 1)]
    [InlineData("-5", 1)]
    [InlineData("1", 1)]
    [InlineData("45", 45)]
    [InlineData("120", 120)]
    [InlineData("500", 120)]
    public void Highlight_minutes_are_clamped_to_1_through_120(string text, int expected)
    {
        var (vm, sv, _) = Create();
        sv.CommitHighlight(text);
        Assert.Equal(expected, vm.Data.Settings.HighlightMinutes);
        Assert.Equal(expected.ToString(), sv.HighlightText);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("3.5")]
    public void Invalid_highlight_text_reverts_to_current_value(string text)
    {
        var (vm, sv, _) = Create();
        sv.CommitHighlight("7");
        sv.CommitHighlight(text);
        Assert.Equal(7, vm.Data.Settings.HighlightMinutes);
        Assert.Equal("7", sv.HighlightText);
    }

    [Fact]
    public void Highlight_change_recalculates_the_list_at_once()
    {
        var (vm, sv, _) = Create();
        // 3분 전에 인식되었고 기본 5분 창 안이므로 하이라이트된다
        Assert.True(vm.Connected.Single(r => r.PortName == "COM3").IsHighlighted);
        sv.CommitHighlight("2");
        Assert.False(vm.Connected.Single(r => r.PortName == "COM3").IsHighlighted);
    }

    [Fact]
    public void Invalid_hotkey_text_keeps_the_old_hotkey_and_shows_error()
    {
        var (vm, sv, f) = Create();
        var old = vm.Data.Settings.Hotkey;
        sv.CommitHotkey("Ctrl+가");
        Assert.Equal(old, vm.Data.Settings.Hotkey);
        Assert.Equal(old, sv.HotkeyText);
        Assert.Equal("settings.hotkey_invalid", sv.HotkeyErrorKey);
        Assert.Empty(f.Registered); // 형식이 틀리면 등록을 시도하지 않는다
    }

    [Fact]
    public void Valid_hotkey_is_registered_and_saved_in_settings()
    {
        var (vm, sv, f) = Create();
        sv.CommitHotkey(" ctrl+alt+k ");
        Assert.Equal("ctrl+alt+k", vm.Data.Settings.Hotkey);
        Assert.Equal(new[] { "ctrl+alt+k" }, f.Registered);
        Assert.Null(sv.HotkeyErrorKey);
        Assert.False(vm.HotkeyFailed);
    }

    [Fact]
    public void Hotkey_conflict_restores_previous_hotkey_and_reports_it()
    {
        var (vm, sv, f) = Create();
        var old = vm.Data.Settings.Hotkey;
        f.Conflicts.Add("Ctrl+Alt+K");
        sv.CommitHotkey("Ctrl+Alt+K");
        Assert.Equal(old, vm.Data.Settings.Hotkey);
        Assert.Equal(old, sv.HotkeyText);
        Assert.Equal(new[] { old }, f.Registered); // 이전 단축키를 다시 등록했다
        Assert.Equal("settings.hotkey_conflict", sv.HotkeyErrorKey);
        Assert.False(vm.HotkeyFailed);
    }

    [Fact]
    public void Hotkey_conflict_with_failed_restore_marks_header_warning()
    {
        var (vm, sv, f) = Create();
        f.Conflicts.Add("Ctrl+Alt+K");
        f.Conflicts.Add(vm.Data.Settings.Hotkey);
        sv.CommitHotkey("Ctrl+Alt+K");
        Assert.True(vm.HotkeyFailed);
        Assert.Equal("settings.hotkey_restore_failed", sv.HotkeyErrorKey);
    }

    [Fact]
    public void Autostart_reads_actual_state_and_returns_to_it_when_write_fails()
    {
        var (_, sv, f) = Create();
        f.AutoStartState = true;
        sv.ReloadAutoStart();
        Assert.True(sv.AutoStartEnabled);

        f.AutoStartWriteOk = false;
        sv.SetAutoStart(false);
        Assert.True(sv.AutoStartEnabled);
        Assert.Equal("settings.autostart_error", sv.AutoStartErrorKey);

        f.AutoStartWriteOk = true;
        sv.SetAutoStart(false);
        Assert.False(sv.AutoStartEnabled);
        Assert.Null(sv.AutoStartErrorKey);
    }

    [Fact]
    public void Language_change_updates_widget_texts_at_once()
    {
        var (vm, sv, _) = Create();
        sv.SetLanguage("en");
        Assert.Equal("en", vm.Language);
        Assert.Equal("COM ports · 2 connected", vm.HeaderText);
        Assert.Equal("Add note…", vm.Connected[0].AddMemoText);
        sv.SetLanguage("ko");
        Assert.Equal("ko", vm.Language);
        Assert.Equal("COM 포트 · 연결 2", vm.HeaderText);
        Assert.Equal("메모 추가…", vm.Connected[0].AddMemoText);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("500")]
    public void Highlight_commit_notifies_even_when_value_is_unchanged(string text)
    {
        var (_, sv, _) = Create();
        sv.CommitHighlight("120");
        var raised = 0;
        sv.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SettingsViewModel.HighlightText)) raised++; };
        sv.CommitHighlight(text); // 현재 값이 120이라 값은 그대로이다
        Assert.Equal("120", sv.HighlightText);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Highlight_boundary_value_with_invalid_text_keeps_boundary()
    {
        var (vm, sv, _) = Create();
        sv.CommitHighlight("1");
        sv.CommitHighlight("-3");
        Assert.Equal(1, vm.Data.Settings.HighlightMinutes);
        Assert.Equal("1", sv.HighlightText);
    }

    [Fact]
    public void Startup_hotkey_conflict_is_shown_when_settings_open()
    {
        var vm = new WidgetViewModel(new DemoDeviceSource(), null, () => Now.AddHours(-5), () => Now);
        vm.HotkeyFailed = true;
        var sv = new SettingsViewModel(vm, new Fake().Register, () => false, _ => true);
        Assert.Equal("settings.hotkey_conflict", sv.HotkeyErrorKey);
    }

    [Fact]
    public void Retrying_the_same_hotkey_after_startup_failure_registers_it_and_clears_the_warning()
    {
        var (vm, sv, f) = Create();
        var key = vm.Data.Settings.Hotkey;
        vm.HotkeyFailed = true;
        f.Conflicts.Add(key);
        sv.CommitHotkey(key);                 // 아직 충돌 중
        Assert.True(vm.HotkeyFailed);
        Assert.Equal("settings.hotkey_conflict", sv.HotkeyErrorKey);

        f.Conflicts.Clear();                  // 다른 프로그램이 키를 놓았다
        sv.CommitHotkey(key);
        Assert.False(vm.HotkeyFailed);
        Assert.Null(sv.HotkeyErrorKey);
        Assert.Equal(new[] { key }, f.Registered);
    }

    [Fact]
    public void Failed_restore_reports_the_restore_failed_message_in_both_languages()
    {
        var (vm, sv, f) = Create();
        f.Conflicts.Add("Ctrl+Alt+K");
        f.Conflicts.Add(vm.Data.Settings.Hotkey);
        sv.CommitHotkey("Ctrl+Alt+K");
        Assert.Equal("settings.hotkey_restore_failed", sv.HotkeyErrorKey);
        Assert.NotEqual(ComPortWidget.Core.Text.Strings.Get(sv.HotkeyErrorKey!, "ko"),
            ComPortWidget.Core.Text.Strings.Get(sv.HotkeyErrorKey!, "en"));
        Assert.NotEqual(ComPortWidget.Core.Text.Strings.Get("settings.hotkey_conflict", "ko"),
            ComPortWidget.Core.Text.Strings.Get(sv.HotkeyErrorKey!, "ko"));
    }

    [Fact]
    public void Deleting_a_memo_while_its_row_is_being_edited_does_not_bring_it_back()
    {
        var (vm, sv, _) = Create();
        var a = vm.Connected.Single(r => r.PortName == "COM3");
        vm.SetMemo(a.Key, "원래 메모");
        a = vm.Connected.Single(r => r.PortName == "COM3");
        sv.ReloadMemos();

        vm.BeginEdit(a);
        a.EditText = "고치는 중";
        sv.DeleteMemo(sv.Memos[0]);
        vm.CommitEdit(a);                     // 이미 취소되었으므로 아무 일도 하지 않는다

        Assert.False(vm.Data.Memos.ContainsKey(a.Key.Value));
        Assert.False(a.IsEditing);
        Assert.Equal("", vm.Connected.Single(r => r.PortName == "COM3").Memo);
    }
}
