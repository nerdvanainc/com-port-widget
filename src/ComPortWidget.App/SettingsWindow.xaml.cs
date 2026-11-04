using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ComPortWidget.Core.Text;

namespace ComPortWidget.App;

/// <summary>설정 창. 값은 바꾸는 즉시 위젯에 적용된다. 규칙은 SettingsViewModel에 있다.</summary>
public partial class SettingsWindow : Window
{
    private static readonly string[] LanguageSettings = { "auto", "ko", "en" };

    /// <summary>"COM 포트 정리" 버튼을 눌렀을 때. 정리 창은 앱이 연다.</summary>
    public event Action? ResetRequested;

    private readonly SettingsViewModel _sv;
    private readonly bool _demo;
    private bool _loading;

    public SettingsWindow(SettingsViewModel sv, bool demo)
    {
        _sv = sv;
        _demo = demo;
        InitializeComponent();
        DataContext = sv;
        MemoList.ItemsSource = sv.Memos;
        sv.Memos.CollectionChanged += (_, _) => UpdateMemosEmpty();
        sv.PropertyChanged += OnViewModelChanged;
        sv.Widget.PropertyChanged += OnWidgetChanged;
        Closed += (_, _) =>
        {
            sv.Widget.PropertyChanged -= OnWidgetChanged;
            sv.Dispose();
        };
        // 닫기, 트레이 종료, 다른 창으로 전환할 때는 LostFocus가 오지 않을 수 있어 직접 확정한다
        Closing += (_, _) => CommitInputs();
        Deactivated += (_, _) => CommitInputs();
        Activated += (_, _) => Reload();
        ApplyStrings();
        Reload();
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => ResetRequested?.Invoke();

    private void OnWidgetChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WidgetViewModel.Language)) ApplyStrings();
    }

    private void CommitInputs()
    {
        CommitHighlightText();
        CommitHotkeyText();
    }

    private string Lang => _sv.Widget.Language;

    // 언어를 바꾸면 이 창의 문구도 바로 바뀐다
    private void ApplyStrings()
    {
        _loading = true;
        Title = Strings.Get("settings.title", Lang);
        AlwaysOnTopBox.Content = Strings.Get("settings.always_on_top", Lang);
        HighlightLabel.Text = Strings.Get("settings.highlight", Lang);
        LanguageLabel.Text = Strings.Get("settings.language", Lang);
        AutoStartBox.Content = Strings.Get("settings.autostart", Lang);
        HotkeyLabel.Text = Strings.Get("settings.hotkey", Lang);
        MemosLabel.Text = Strings.Get("settings.memos", Lang);
        MemosEmpty.Text = Strings.Get("settings.memos_empty", Lang);
        ResetButton.Content = Strings.Get("settings.reset", Lang);
        Note.Text = _demo ? Strings.Get("settings.demo_note", Lang) : "";
        var index = LanguageBox.SelectedIndex;
        LanguageBox.Items.Clear();
        foreach (var key in new[] { "settings.language.auto", "settings.language.ko", "settings.language.en" })
            LanguageBox.Items.Add(Strings.Get(key, Lang));
        LanguageBox.SelectedIndex = index >= 0 ? index : Math.Max(0, Array.IndexOf(LanguageSettings, _sv.LanguageSetting));
        ShowErrors();
        _loading = false;
    }

    /// <summary>열 때와 다시 활성화될 때 실제 상태(자동 실행, 저장된 메모)를 읽어 온다.</summary>
    private void Reload()
    {
        _sv.ReloadAutoStart();
        _sv.ReloadMemos();
        _loading = true;
        AlwaysOnTopBox.IsChecked = _sv.AlwaysOnTop;
        AutoStartBox.IsChecked = _sv.AutoStartEnabled;
        // 입력 중인 칸은 덮어쓰지 않는다
        if (!HighlightBox.IsKeyboardFocusWithin) HighlightBox.Text = _sv.HighlightText;
        if (!HotkeyBox.IsKeyboardFocusWithin) HotkeyBox.Text = _sv.HotkeyText;
        _loading = false;
        UpdateMemosEmpty();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SettingsViewModel.HighlightText): HighlightBox.Text = _sv.HighlightText; break;
            case nameof(SettingsViewModel.HotkeyText): HotkeyBox.Text = _sv.HotkeyText; break;
            case nameof(SettingsViewModel.AutoStartEnabled): AutoStartBox.IsChecked = _sv.AutoStartEnabled; break;
            case nameof(SettingsViewModel.HotkeyErrorKey):
            case nameof(SettingsViewModel.AutoStartErrorKey): ShowErrors(); break;
        }
    }

    private void ShowErrors()
    {
        Show(HotkeyError, _sv.HotkeyErrorKey);
        Show(AutoStartError, _sv.AutoStartErrorKey);
    }

    private void Show(TextBlock box, string? key)
    {
        box.Text = key is null ? "" : Strings.Get(key, Lang);
        box.Visibility = key is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateMemosEmpty()
        => MemosEmpty.Visibility = _sv.Memos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>이미 열려 있을 때 앞으로 가져온다.</summary>
    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
        Topmost = true; // 다른 창 뒤에 있어도 앞으로 올리고 곧바로 해제한다
        Topmost = false;
        Focus();
    }

    private void AlwaysOnTop_Click(object sender, RoutedEventArgs e)
        => _sv.SetAlwaysOnTop(AlwaysOnTopBox.IsChecked == true);

    private void Highlight_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { CommitHighlightText(); HighlightBox.SelectAll(); }
    }

    private void Highlight_LostFocus(object sender, RoutedEventArgs e) => CommitHighlightText();

    // 값이 그대로여도 입력칸을 확정된 값으로 되돌린다("abc", "500" 등)
    private void CommitHighlightText()
    {
        _sv.CommitHighlight(HighlightBox.Text);
        HighlightBox.Text = _sv.HighlightText;
    }

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LanguageBox.SelectedIndex < 0) return;
        _sv.SetLanguage(LanguageSettings[LanguageBox.SelectedIndex]);
    }

    private void AutoStart_Click(object sender, RoutedEventArgs e)
    {
        _sv.SetAutoStart(AutoStartBox.IsChecked == true);
        AutoStartBox.IsChecked = _sv.AutoStartEnabled; // 실패하면 실제 상태로 돌아간다
    }

    private void Hotkey_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CommitHotkeyText(retry: true);
    }

    private void Hotkey_LostFocus(object sender, RoutedEventArgs e) => CommitHotkeyText();

    // 고치지 않았으면 다시 검사하지 않는다. 직전 오류 표시를 포커스 이동만으로 지우지 않기 위해서이다
    private void CommitHotkeyText(bool retry = false)
    {
        if (HotkeyBox.Text.Trim() == _sv.HotkeyText && !(retry && _sv.Widget.HotkeyFailed)) return;
        _sv.CommitHotkey(HotkeyBox.Text);
        HotkeyBox.Text = _sv.HotkeyText;
    }

    private void DeleteMemo_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is MemoItem item) _sv.DeleteMemo(item);
    }
}
