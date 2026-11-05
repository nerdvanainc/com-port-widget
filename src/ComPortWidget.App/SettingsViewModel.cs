using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using ComPortWidget.Core.Text;

namespace ComPortWidget.App;

/// <summary>설정 창의 저장된 메모 한 줄.</summary>
public sealed record MemoItem(string Key, string Memo);

/// <summary>
/// 설정 창의 상태와 규칙. 창에 의존하지 않으므로 단위 시험이 가능하다.
/// 바꾼 값은 바로 위젯에 적용하고 기존 저장 경로(WidgetViewModel.Persist)로 저장한다.
/// </summary>
public sealed class SettingsViewModel : INotifyPropertyChanged, IDisposable
{
    public const int MinHighlight = 1, MaxHighlight = 120;

    private readonly WidgetViewModel _vm;
    private readonly Func<string, bool>? _registerHotkey;
    private readonly Func<bool> _readAutoStart;
    private readonly Func<bool, bool> _writeAutoStart;
    private string _highlightText;
    private string _hotkeyText;
    private string? _hotkeyErrorKey, _autoStartErrorKey;
    private bool _autoStartEnabled;

    /// <param name="registerHotkey">단축키를 등록하고 성공 여부를 돌려준다. null이면 등록 없이 성공으로 본다(--demo).</param>
    /// <param name="readAutoStart">HKCU Run의 실제 등록 여부.</param>
    /// <param name="writeAutoStart">등록하거나 해제하고 성공 여부를 돌려준다.</param>
    public SettingsViewModel(WidgetViewModel vm, Func<string, bool>? registerHotkey,
        Func<bool> readAutoStart, Func<bool, bool> writeAutoStart)
    {
        _vm = vm;
        _registerHotkey = registerHotkey;
        _readAutoStart = readAutoStart;
        _writeAutoStart = writeAutoStart;
        _highlightText = vm.Data.Settings.HighlightMinutes.ToString(CultureInfo.InvariantCulture);
        _hotkeyText = vm.Data.Settings.Hotkey;
        _autoStartEnabled = readAutoStart();
        // 시작할 때 단축키 충돌이 있었으면 설정 창을 열자마자 알린다
        if (vm.HotkeyFailed) _hotkeyErrorKey = "settings.hotkey_conflict";
        ReloadMemos();
        _vm.PropertyChanged += OnWidgetChanged;
    }

    private void OnWidgetChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WidgetViewModel.Language))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DeleteText)));
    }

    public void Dispose() => _vm.PropertyChanged -= OnWidgetChanged;

    public WidgetViewModel Widget => _vm;
    public ObservableCollection<MemoItem> Memos { get; } = new();

    public string HighlightText { get => _highlightText; private set => Set(ref _highlightText, value); }
    public string HotkeyText { get => _hotkeyText; private set => Set(ref _hotkeyText, value); }
    public string? HotkeyErrorKey { get => _hotkeyErrorKey; private set => Set(ref _hotkeyErrorKey, value); }
    public string? AutoStartErrorKey { get => _autoStartErrorKey; private set => Set(ref _autoStartErrorKey, value); }
    public bool AutoStartEnabled { get => _autoStartEnabled; private set => Set(ref _autoStartEnabled, value); }

    /// <summary>삭제 버튼 문구. 목록 항목 서식에서 바인딩한다.</summary>
    public string DeleteText => Strings.Get("settings.delete", _vm.Language);

    public bool AlwaysOnTop => _vm.Data.Settings.AlwaysOnTop;
    public string LanguageSetting => _vm.Data.Settings.Language;

    public void SetAlwaysOnTop(bool value) => _vm.SetAlwaysOnTop(value);

    public void SetLanguage(string setting) => _vm.SetLanguageSetting(setting);

    /// <summary>입력한 분을 1~120으로 맞춰 적용한다. 숫자가 아니면 현재 값으로 되돌린다.</summary>
    public void CommitHighlight(string text)
    {
        var current = _vm.Data.Settings.HighlightMinutes;
        if (int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            current = Num.Clamp(n, MinHighlight, MaxHighlight);
            _vm.SetHighlightMinutes(current);
        }
        HighlightText = current.ToString(CultureInfo.InvariantCulture);
        // 값이 그대로여도 창이 입력칸을 되돌릴 수 있게 항상 알린다
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HighlightText)));
    }

    /// <summary>
    /// 단축키 문자열을 검사하고 등록한다. 형식이 틀리거나 등록에 실패하면 이전 단축키를 유지한다.
    /// 등록 실패 시 이전 단축키를 다시 등록해 위젯이 계속 호출되게 한다.
    /// </summary>
    public void CommitHotkey(string text)
    {
        text = (text ?? "").Trim();
        var old = _vm.Data.Settings.Hotkey;
        HotkeyErrorKey = null;
        if (!HotkeyParser.TryParse(text, out _, out _))
        {
            HotkeyText = old;
            HotkeyErrorKey = "settings.hotkey_invalid";
            return;
        }
        // 등록이 실패한 상태에서는 같은 키로도 다시 시도할 수 있어야 한다
        if (text == old && !_vm.HotkeyFailed)
        {
            HotkeyText = old;
            return;
        }
        if (_registerHotkey is null || _registerHotkey(text))
        {
            _vm.Data.Settings.Hotkey = text;
            HotkeyText = text;
            _vm.HotkeyFailed = false;
            _vm.Persist();
            return;
        }
        HotkeyText = old;
        if (text == old)
        {
            _vm.HotkeyFailed = true;
            HotkeyErrorKey = "settings.hotkey_conflict";
            return;
        }
        var restored = _registerHotkey(old);
        _vm.HotkeyFailed = !restored;
        HotkeyErrorKey = restored ? "settings.hotkey_conflict" : "settings.hotkey_restore_failed";
    }

    /// <summary>레지스트리에서 실제 자동 실행 상태를 다시 읽는다.</summary>
    public void ReloadAutoStart() => AutoStartEnabled = _readAutoStart();

    /// <summary>자동 실행을 바꾼다. 쓰기에 실패하면 실제 상태로 되돌리고 오류를 남긴다.</summary>
    public void SetAutoStart(bool enabled)
    {
        if (_writeAutoStart(enabled))
        {
            AutoStartErrorKey = null;
            _vm.AutoStartFailed = false;
            AutoStartEnabled = enabled;
        }
        else
        {
            AutoStartErrorKey = "settings.autostart_error";
            AutoStartEnabled = _readAutoStart();
        }
    }

    /// <summary>저장된 메모 목록을 다시 채운다. 위젯에서 메모를 고쳤을 때를 위해 창을 다시 활성화할 때도 부른다.</summary>
    public void ReloadMemos()
    {
        var wanted = _vm.Data.Memos.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => new MemoItem(p.Key, p.Value)).ToList();
        SyncMemos(Memos, wanted);
    }

    /// <summary>
    /// 목록을 제자리에서 맞춘다. 바뀐 항목만 넣고 빼므로 그대로인 줄은 같은 인스턴스(같은 화면 컨테이너)를 유지한다.
    /// Clear 후 다시 채우면 창을 활성화하는 클릭 직전에 줄이 교체되어 첫 클릭이 사라진다.
    /// </summary>
    public static void SyncMemos(ObservableCollection<MemoItem> target, IReadOnlyList<MemoItem> wanted)
    {
        for (var i = target.Count - 1; i >= 0; i--)
            if (!wanted.Contains(target[i])) target.RemoveAt(i);
        for (var i = 0; i < wanted.Count; i++)
        {
            if (i < target.Count && target[i] == wanted[i]) continue;
            var at = target.IndexOf(wanted[i]);
            if (at < 0) target.Insert(i, wanted[i]);
            else target.Move(at, i);
        }
    }

    public void DeleteMemo(MemoItem item)
    {
        _vm.DeleteMemo(item.Key);
        Memos.Remove(item);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
