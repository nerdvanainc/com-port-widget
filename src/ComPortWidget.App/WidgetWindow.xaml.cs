using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using ComPortWidget.Core.Text;

namespace ComPortWidget.App;

public partial class WidgetWindow : Window
{
    private readonly WidgetViewModel _vm;
    private ContextMenu? _rowMenu;
    private readonly DispatcherTimer _minuteTimer;
    private readonly DispatcherTimer _boundsTimer;
    private bool _allowClose;
    private RowViewModel? _menuRow;

    public WidgetWindow(WidgetViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;
        Topmost = vm.Data.Settings.AlwaysOnTop;
        InitRowMenu();
        ApplyStrings();
        ApplyBounds();
        UpdateDisconnectedVisibility();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(WidgetViewModel.HasDisconnected) or nameof(WidgetViewModel.DisconnectedExpanded))
                UpdateDisconnectedVisibility();
            else if (e.PropertyName == nameof(WidgetViewModel.AlwaysOnTop))
                Topmost = _vm.AlwaysOnTop;
            else if (e.PropertyName == nameof(WidgetViewModel.Language))
                ApplyStrings();
        };

        // 1분마다 시간 표기와 하이라이트를 다시 계산한다
        _minuteTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _minuteTimer.Tick += (_, _) => _vm.Rebuild();
        _minuteTimer.Start();

        // 창을 옮기거나 키울 때마다 저장하지 않고 잠시 모았다가 저장한다
        _boundsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _boundsTimer.Tick += (_, _) => { _boundsTimer.Stop(); SaveBounds(); };
        LocationChanged += (_, _) => RestartBoundsTimer();
        SizeChanged += (_, _) => RestartBoundsTimer();
    }

    private string Lang => _vm.Language;

    private void ApplyStrings()
    {
        Title = Strings.Get("app.name", Lang);
        GearButton.ToolTip = Strings.Get("widget.settings", Lang);
        HideButton.ToolTip = Strings.Get("widget.hide", Lang);
        var menu = _rowMenu!;
        ((MenuItem)menu.Items[0]).Header = Strings.Get("menu.copy_port", Lang);
        ((MenuItem)menu.Items[1]).Header = Strings.Get("menu.edit_memo", Lang);
        ((MenuItem)menu.Items[2]).Header = Strings.Get("menu.device_info", Lang);
    }

    // 메뉴가 열린 동안 목록 갱신을 미루고, 메뉴를 연 줄을 그 시점에 잡아 둔다. 한 번만 연결한다
    private void InitRowMenu()
    {
        var menu = _rowMenu = (ContextMenu)FindResource("RowMenu");
        menu.Opened += (_, _) =>
        {
            _menuRow = (menu.PlacementTarget as FrameworkElement)?.DataContext as RowViewModel;
            _vm.SetMenuOpen(true);
        };
        menu.Closed += (_, _) => _vm.SetMenuOpen(false);
    }

    // ---- 위치와 크기 ----

    private void ApplyBounds()
    {
        var s = _vm.Data.Settings;
        var work = SystemParameters.WorkArea;
        double w = s.Width is { } sw && double.IsFinite(sw) && sw >= MinWidth ? sw : Width;
        double h = s.Height is { } sh && double.IsFinite(sh) && sh >= MinHeight ? sh : Height;
        w = Math.Min(w, work.Width);
        h = Math.Min(h, work.Height);
        double left, top;
        if (s.Left is { } l && s.Top is { } t && double.IsFinite(l) && double.IsFinite(t) && IsOnScreen(l, t, w, h))
        {
            left = l; top = t;
        }
        else if (s.Left is not null && s.Top is not null)
        {
            // 모니터 구성이 바뀌어 화면 밖이 되었으면 주 모니터 안으로 옮긴다
            left = Math.Clamp(s.Left.Value, work.Left, Math.Max(work.Left, work.Right - w));
            top = Math.Clamp(s.Top.Value, work.Top, Math.Max(work.Top, work.Bottom - h));
            if (!double.IsFinite(left)) left = work.Right - w - 16;
            if (!double.IsFinite(top)) top = work.Top + 16;
        }
        else
        {
            left = work.Right - w - 16; // 처음에는 주 모니터 오른쪽 위
            top = work.Top + 16;
        }
        Width = w; Height = h; Left = left; Top = top;
    }

    // 제목줄 영역이 어느 모니터든 40x30 이상 보이면 화면 안으로 본다
    private static bool IsOnScreen(double l, double t, double w, double h)
    {
        var vl = SystemParameters.VirtualScreenLeft;
        var vt = SystemParameters.VirtualScreenTop;
        var vr = vl + SystemParameters.VirtualScreenWidth;
        var vb = vt + SystemParameters.VirtualScreenHeight;
        var visibleW = Math.Min(l + w, vr) - Math.Max(l, vl);
        var visibleH = Math.Min(t + 30, vb) - Math.Max(t, vt);
        return visibleW >= 40 && visibleH >= 30 - 0.5;
    }

    private void RestartBoundsTimer()
    {
        _boundsTimer.Stop();
        _boundsTimer.Start();
    }

    private void SaveBounds()
    {
        // 최소화나 숨김 상태의 좌표(-32000 등)는 저장하지 않는다
        if (!IsVisible || WindowState != WindowState.Normal) return;
        _vm.SaveBounds(Left, Top, Width, Height);
    }

    // ---- 앞으로 불러오기 / 숨기기 / 닫기 ----

    /// <summary>창을 보이고 앞으로 가져온다. Win+D로 숨겨졌거나 최소화된 상태에서도 동작한다.</summary>
    public void BringToFront()
    {
        if (!IsVisible) Show();
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        if (WindowState == WindowState.Minimized || IsIconic(hwnd))
        {
            ShowWindow(hwnd, SW_RESTORE);
            WindowState = WindowState.Normal;
        }
        else
        {
            ShowWindow(hwnd, SW_SHOW);
        }
        // 항상 맨 위 설정이 꺼져 있어도 한 번 맨 위로 올렸다가 내려서 다른 창 앞에 놓는다
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
        if (!_vm.Data.Settings.AlwaysOnTop)
            SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
        Activate();
        SetForegroundWindow(hwnd);
    }

    /// <summary>종료할 때 실제로 닫는다. 그 전까지 닫기는 숨기기이다.</summary>
    public void CloseForExit()
    {
        SaveBounds();
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            SaveBounds();
            Hide();
        }
        base.OnClosing(e);
    }

    /// <summary>톱니 버튼을 눌렀을 때. 설정 창은 App이 연다.</summary>
    public event Action? SettingsRequested;

    private void Gear_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

    private void Hide_Click(object sender, RoutedEventArgs e) => Hide();

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    // ---- 연결 안 됨 그룹 ----

    private void UpdateDisconnectedVisibility()
    {
        var expanded = _vm.DisconnectedExpanded;
        Arrow.Text = expanded ? "▾" : "▸";
        DisconnectedList.Visibility = expanded && _vm.HasDisconnected ? Visibility.Visible : Visibility.Collapsed;
    }

    private void DisconnectedToggle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => _vm.DisconnectedExpanded = !_vm.DisconnectedExpanded;

    // ---- 메모 편집 ----

    private void MemoArea_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is RowViewModel row && !row.IsEditing)
        {
            _vm.BeginEdit(row);
            e.Handled = true;
        }
    }

    private void EditBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox box && box.IsVisible)
        {
            // 보이는 즉시 포커스를 주려면 레이아웃이 끝난 뒤에 해야 한다
            Dispatcher.BeginInvoke(() =>
            {
                box.Focus();
                Keyboard.Focus(box);
                box.SelectAll();
            }, DispatcherPriority.Input);
        }
    }

    private void EditBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        // 여러 줄 붙여넣기는 줄바꿈을 공백으로 바꿔 한 줄로 넣는다
        if (!e.DataObject.GetDataPresent(DataFormats.UnicodeText)) return;
        if (e.DataObject.GetData(DataFormats.UnicodeText) is not string text) return;
        var flat = MemoText.FlattenLineBreaks(text);
        if (flat != text) e.DataObject = new DataObject(DataFormats.UnicodeText, flat);
    }

    private void EditBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not RowViewModel row) return;
        if (e.Key == Key.Enter)
        {
            _vm.CommitEdit(row);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _vm.CancelEdit(row);
            e.Handled = true;
        }
    }

    private void EditBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // 텍스트 칸 자체의 우클릭 메뉴(붙여넣기 등)로 포커스가 옮겨 갈 때는 편집을 끝내지 않는다
        if (e.NewFocus is ContextMenu || e.NewFocus is MenuItem) return;
        if (((FrameworkElement)sender).DataContext is RowViewModel row)
            _vm.CommitEdit(row);
    }

    // ---- 우클릭 메뉴 ----

    private RowViewModel? MenuRow(object sender) => _menuRow;

    private void CopyPort_Click(object sender, RoutedEventArgs e)
    {
        if (MenuRow(sender) is not { } row) return;
        try { Clipboard.SetText(row.PortName); }
        catch (COMException) { } // 다른 프로그램이 클립보드를 잡고 있으면 건너뛴다
    }

    private void EditMemo_Click(object sender, RoutedEventArgs e)
    {
        if (MenuRow(sender) is { } row) _vm.BeginEdit(row);
    }

    private void DeviceInfo_Click(object sender, RoutedEventArgs e)
    {
        if (MenuRow(sender) is not { } row) return;
        var r = row.Row;
        string T(DateTimeOffset? t) => t is { } v ? v.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : Strings.Get("info.none", Lang);
        var sb = new StringBuilder();
        sb.AppendLine($"{Strings.Get("info.port", Lang)}: {r.Record.PortName}");
        sb.AppendLine($"{Strings.Get("info.state", Lang)}: {Strings.Get(r.Record.Present ? "info.connected" : "info.not_connected", Lang)}");
        sb.AppendLine($"{Strings.Get("info.vendor", Lang)}: {(r.Vendor.Length > 0 ? r.Vendor : Strings.Get("info.none", Lang))}");
        sb.AppendLine($"{Strings.Get("info.description", Lang)}: {(r.Description.Length > 0 ? r.Description : Strings.Get("info.none", Lang))}");
        sb.AppendLine($"{Strings.Get("info.instance_id", Lang)}: {r.Record.InstanceId}");
        sb.AppendLine($"{Strings.Get("info.key", Lang)}: {r.Key.Value}");
        sb.AppendLine($"{Strings.Get("info.arrival", Lang)}: {T(r.Record.LastArrival)}");
        sb.Append($"{Strings.Get("info.removal", Lang)}: {T(r.Record.LastRemoval)}");
        MessageBox.Show(this, sb.ToString(), Strings.Get("menu.device_info", Lang), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ---- Win32 ----

    private const int SW_SHOW = 5, SW_RESTORE = 9;
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_SHOWWINDOW = 0x40;
    private static readonly IntPtr HWND_TOPMOST = new(-1), HWND_NOTOPMOST = new(-2);

    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
