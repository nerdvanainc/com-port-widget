using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using ComPortWidget.Core.Devices;
using ComPortWidget.Core.Listing;
using ComPortWidget.Core.Reset;
using ComPortWidget.Core.Text;
using ComPortWidget.Platform;

namespace ComPortWidget.App;

/// <summary>목록의 한 줄. 그룹 제목이거나 정리 후보 하나이다.</summary>
internal sealed class ResetRowViewModel : INotifyPropertyChanged
{
    private bool _selected;
    private readonly Action? _onChanged;

    public ResetRowViewModel(string header, ResetKind kind, string toggleText)
    {
        Text = header;
        IsHeader = true;
        GroupKind = kind;
        ToggleText = toggleText;
    }

    public ResetRowViewModel(ResetCandidate candidate, string text, string sub, string tag, Action onChanged)
    {
        Candidate = candidate;
        Text = text;
        SubText = sub;
        Tag = tag;
        _selected = candidate.DefaultSelected;
        _onChanged = onChanged;
    }

    public ResetCandidate? Candidate { get; }
    public bool IsHeader { get; }
    /// <summary>그룹 제목 줄이 가리키는 그룹. 항목 줄이면 null.</summary>
    public ResetKind? GroupKind { get; }
    public string ToggleText { get; } = "";
    public string Text { get; }
    public string SubText { get; } = "";
    public string Tag { get; } = "";

    public Visibility HeaderVisibility => IsHeader ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ItemVisibility => IsHeader ? Visibility.Collapsed : Visibility.Visible;
    public Visibility TagVisibility => Tag.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public bool IsSelected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            _onChanged?.Invoke();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>결과 목록의 한 줄. 항목 문구, 실패 원인(Error)을 따로 보여 준다.</summary>
internal sealed class ResetResultRowViewModel(string target, string status, string detail, string error)
{
    public string Target { get; } = target;
    public string StatusText { get; } = status;
    public string DetailText { get; } = detail;
    public string ErrorText { get; } = error;
    public Visibility DetailVisibility => DetailText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ErrorVisibility => ErrorText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>
/// 정리 화면. 후보는 엄격한 읽기로 불러오고, 실행은 같은 exe를 UAC로 직접 승격해 정리 전용 모드로 돌리는 방식에 맡긴다.
/// --demo에서는 승격하지 않고 가짜 백엔드로 모의 실행한다.
/// </summary>
public partial class ResetWindow : Window
{
    private readonly WidgetViewModel _vm;
    private readonly bool _demo;
    private readonly Action _onChanged;
    private readonly List<ResetRowViewModel> _rows = new();
    private bool _busy;
    private bool _loaded;
    /// <summary>정리를 실행하는 중인지. 실행 중에는 창도 앱도 닫지 않는다.</summary>
    internal bool IsBusy => _busy;
    private string? _statusKey;
    private object[] _statusArgs = [];

    public ResetWindow(WidgetViewModel vm, bool demo, Action onChanged)
    {
        _vm = vm;
        _demo = demo;
        _onChanged = onChanged;
        InitializeComponent();
        Closing += OnClosing;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WidgetViewModel.Language)) ApplyStrings();
        };
        ApplyStrings();
        Load();
    }

    private string Lang => _vm.Language;

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_busy) e.Cancel = true; // 정리 중에는 닫지 않는다
    }

    private void ApplyStrings()
    {
        Title = Strings.Get("reset.title", Lang);
        Intro.Text = Strings.Get("reset.intro", Lang);
        Note.Text = Strings.Get("reset.note", Lang);
        RetryButton.Content = Strings.Get("reset.retry", Lang);
        SelectReservedButton.Content = Strings.Get("reset.selectReserved", Lang);
        DeselectAllButton.Content = Strings.Get("reset.deselectAll", Lang);
        RunButton.Content = Strings.Get("reset.run", Lang);
        CloseButton.Content = Strings.Get("reset.close", Lang);
        ModeNote.Text = _demo ? Strings.Get("reset.demoNote", Lang) : "";
        if (_statusKey is not null) ShowStatus(_statusKey, _statusArgs);
        UpdateCount();
    }

    private void ShowStatus(string? key, params object[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        StatusText.Text = key is null ? "" : string.Format(CultureInfo.CurrentCulture, Strings.Get(key, Lang), args);
    }

    // ---- 후보 불러오기 ----

    private async void Load()
    {
        _loaded = false;
        RetryButton.Visibility = Visibility.Collapsed;
        ResultList.Visibility = Visibility.Collapsed;
        CandidateList.Visibility = Visibility.Visible;
        _rows.Clear();
        CandidateList.ItemsSource = null;
        ShowStatus("reset.loading");
        UpdateButtons();

        var memos = new Dictionary<string, string>(_vm.Data.Memos); // UI 스레드에서 복사해 둔다
        try
        {
            var candidates = await Task.Run(() => ReadCandidates(memos));
            BuildRows(candidates);
            _loaded = true;
            ShowStatus(candidates.Count == 0 ? "reset.empty" : null);
        }
        catch (Exception ex)
        {
            // 연결을 뽑는 도중에는 읽기가 일시적으로 실패할 수 있다. 어떤 예외든 다시 시도할 수 있게 한다
            ShowStatus("reset.loadFailed", Describe(ex));
            RetryButton.Visibility = Visibility.Visible;
        }
        UpdateButtons();
    }

    // 예외 메시지에 안쪽 예외 메시지를 이어 붙인다. 어느 인스턴스에서 실패했는지가 안쪽에 있을 수 있다
    private static string Describe(Exception ex)
        => ex.InnerException is { } inner && inner.Message != ex.Message ? ex.Message + " / " + inner.Message : ex.Message;

    private IReadOnlyList<ResetCandidate> ReadCandidates(Dictionary<string, string> memos)
    {
        if (_demo)
        {
            var records = ResetDemo.Records();
            return ResetPlanner.Build(records, ResetDemo.Reserved(), new HashSet<int>(), ResetDemo.Memos(records, memos));
        }
        var real = new WindowsDeviceSource().GetRecordsStrict();
        var reserved = ComDbBitmap.ToPorts(ComRegistry.ReadComDb());
        var others = ComRegistry.ReadOtherHolders();
        return ResetPlanner.Build(real, reserved, others, memos);
    }

    private static readonly (ResetKind Kind, string Header)[] Groups =
    {
        (ResetKind.UsbRecord, "reset.group.usb"),
        (ResetKind.SameDeviceConnected, "reset.group.same"),
        (ResetKind.VirtualOrBluetooth, "reset.group.virtual"),
        (ResetKind.ReservedOnly, "reset.group.reserved"),
    };

    private void BuildRows(IReadOnlyList<ResetCandidate> candidates)
    {
        _rows.Clear();
        var now = DateTimeOffset.Now;
        foreach (var (kind, headerKey) in Groups)
        {
            var group = candidates.Where(c => c.Kind == kind).ToList();
            if (group.Count == 0) continue;
            _rows.Add(new ResetRowViewModel($"{Strings.Get(headerKey, Lang)} ({group.Count})", kind,
                Strings.Get("reset.toggleGroup", Lang)));
            foreach (var c in group)
            {
                var title = "COM" + c.PortNumber + (string.IsNullOrEmpty(c.Memo) ? "" : " · " + c.Memo);
                string sub;
                if (c.InstanceId is null)
                    sub = Strings.Get("reset.reservedOnlyLine", Lang);
                else
                {
                    var seen = c.LastSeen is null ? "" : TimeLabel.Format(c.LastSeen, now, Lang) + " · ";
                    sub = seen + c.InstanceId;
                }
                var tag = c.ReleasesNumber ? "" : Strings.Get("reset.tag.keepsNumber", Lang);
                _rows.Add(new ResetRowViewModel(c, title, sub, tag, UpdateCount));
            }
        }
        CandidateList.ItemsSource = _rows;
        UpdateCount();
    }

    // ---- 선택과 버튼 ----

    private int SelectedCount => _rows.Count(r => r.Candidate is not null && r.IsSelected);

    private void UpdateCount()
    {
        CountText.Text = string.Format(CultureInfo.CurrentCulture, Strings.Get("reset.selected", Lang), SelectedCount);
        var (records, numbers) = ResetPlanner.Summarize(
            _rows.Where(r => r.Candidate is not null && r.IsSelected).Select(r => r.Candidate!));
        ExpectedText.Text = SelectedCount == 0 ? ""
            : string.Format(CultureInfo.CurrentCulture, Strings.Get("reset.expected", Lang), records, numbers);
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var hasList = _loaded && CandidateList.Visibility == Visibility.Visible && !_busy;
        RunButton.IsEnabled = hasList && SelectedCount > 0;
        SelectReservedButton.IsEnabled = hasList && _rows.Any(r => r.Candidate?.Kind == ResetKind.ReservedOnly);
        DeselectAllButton.IsEnabled = hasList && SelectedCount > 0;
        CloseButton.IsEnabled = !_busy;
        RetryButton.IsEnabled = !_busy;
    }

    private void SelectReserved_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in _rows)
            if (r.Candidate?.Kind == ResetKind.ReservedOnly) r.IsSelected = true;
    }

    private void DeselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in _rows)
            if (r.Candidate is not null) r.IsSelected = false;
    }

    // 그룹 안에 선택 안 된 항목이 하나라도 있으면 모두 선택하고, 전부 선택돼 있으면 모두 해제한다
    private void ToggleGroup_Click(object sender, RoutedEventArgs e)
    {
        if (!_loaded || _busy) return;
        if ((sender as FrameworkElement)?.DataContext is not ResetRowViewModel { GroupKind: { } kind }) return;
        var items = _rows.Where(r => r.Candidate?.Kind == kind).ToList();
        var value = items.Any(r => !r.IsSelected);
        foreach (var r in items) r.IsSelected = value;
    }

    private void Retry_Click(object sender, RoutedEventArgs e) => Load();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ---- 실행 ----

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        var selected = _rows.Where(r => r.Candidate is not null && r.IsSelected).Select(r => r.Candidate!).ToList();
        if (selected.Count == 0) return;

        if (!_demo)
        {
            var (confirmRecords, confirmNumbers) = ResetPlanner.Summarize(selected);
            // 처음 포커스와 Enter는 "아니오"에 둔다(되돌릴 수 없는 작업이므로 안전한 쪽이 기본)
            var ask = ThemedDialog.Ask(this,
                string.Format(CultureInfo.CurrentCulture, Strings.Get("reset.confirm", Lang), confirmRecords, confirmNumbers),
                Strings.Get("reset.title", Lang), Lang, defaultNo: true);
            if (!ask) return;
        }

        var plan = new ResetPlan(
            selected.Where(c => c.InstanceId is not null).Select(c => c.InstanceId!).ToList(),
            selected.Where(c => c.InstanceId is null).Select(c => c.PortNumber).ToList());

        _busy = true;
        ShowStatus("reset.running");
        UpdateButtons();
        try
        {
            if (_demo) await RunDemo(plan);
            else await RunElevated(plan);
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    // 데모: 승격하지 않고 가짜 백엔드로 같은 실행 흐름을 돌린다
    private async Task RunDemo(ResetPlan plan)
    {
        var result = await Task.Run(() => new ResetExecutor(new DemoResetBackend()).Execute(plan));
        ShowResult(result, null);
    }

    private async Task RunElevated(ResetPlan plan)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ComPortWidget");
        var planPath = Path.Combine(dir, "reset-plan.json");
        var resultPath = Path.Combine(dir, "reset-result.json");
        try
        {
            Directory.CreateDirectory(dir);
            // 승격된 프로세스가 거부할 경로(링크, 네트워크 경로)는 UAC 창을 띄우기 전에 걸러 낸다
            if (!ResetPaths.TryValidate(planPath, resultPath, out _, out var pathError))
            {
                ShowStatus("reset.pathInvalid", pathError);
                RetryButton.Visibility = Visibility.Visible;
                return;
            }
            ResetFiles.WritePlan(planPath, plan);
        }
        catch (Exception ex)
        {
            ShowStatus("reset.loadFailed", Describe(ex));
            RetryButton.Visibility = Visibility.Visible;
            return;
        }

        var itemCount = plan.InstanceIds.Count + plan.ReservedOnlyPorts.Count;
        var outcome = await Task.Run(() => new ResetLauncher(new ShellProcessStarter()).Run(planPath, resultPath, itemCount));
        try { File.Delete(planPath); } catch (Exception) { }

        if (outcome == LaunchOutcome.Cancelled)
        {
            ShowStatus("reset.cancelled");
            return;
        }
        // 중단된 실행도 결과 파일에 사유를 남기므로, 읽을 수 있으면 보여 준다
        var file = ResetFiles.ReadResult(resultPath);
        if (file is null)
        {
            // 일부가 바뀌었을 수 있으므로 목록과 위젯을 다시 읽게 하고, 다시 불러오기를 내놓는다
            ShowStatus("reset.failed");
            RetryButton.Visibility = Visibility.Visible;
            _onChanged();
            return;
        }
        ShowResult(file.ToResult(), file);
        _onChanged();
    }

    private void ShowResult(ResetResult result, ResetResultFile? file)
    {
        var rows = new List<ResetResultRowViewModel>();
        foreach (var i in result.Items)
        {
            var detail = i.Detail is not null && Strings.Keys.Contains(i.Detail) ? Strings.Get(i.Detail, Lang) : i.Detail ?? "";
            var error = string.IsNullOrEmpty(i.Error) ? "" : string.Format(CultureInfo.CurrentCulture, Strings.Get("reset.detail", Lang), i.Error);
            rows.Add(new ResetResultRowViewModel(i.Target, Strings.Get("reset.status." + i.Status, Lang), detail, error));
        }
        if (file is { DryRun: true })
            foreach (var a in file.Actions)
                rows.Add(new ResetResultRowViewModel(a, "", Strings.Get("reset.dryRunNote", Lang), ""));

        ResultList.ItemsSource = rows;
        CandidateList.Visibility = Visibility.Collapsed;
        ResultList.Visibility = Visibility.Visible;
        RetryButton.Visibility = Visibility.Collapsed;

        if (result.Aborted)
        {
            var reason = result.AbortReason is not null && Strings.Keys.Contains(result.AbortReason)
                ? Strings.Get(result.AbortReason, Lang) : result.AbortReason ?? "";
            var text = string.Format(CultureInfo.CurrentCulture, Strings.Get("reset.aborted", Lang), reason);
            if (!string.IsNullOrEmpty(result.AbortError))
                text += "\n" + string.Format(CultureInfo.CurrentCulture, Strings.Get("reset.detail", Lang), result.AbortError);
            StatusText.Text = text;
            _statusKey = null;
        }
        else
        {
            ShowStatus("reset.resultTitle");
        }
    }
}
