using System.Collections.ObjectModel;
using System.Globalization;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ComPortWidget.Core.Devices;
using ComPortWidget.Core.Listing;
using ComPortWidget.Core.Storage;
using ComPortWidget.Core.Text;
using ComPortWidget.Platform;

namespace ComPortWidget.App;

/// <summary>목록의 카드 한 장. 윗줄은 포트와 메모, 아랫줄은 제조사·설명·시간이다.</summary>
public sealed class RowViewModel : INotifyPropertyChanged
{
    private string _memo;
    private bool _isEditing;
    private string _editText = "";

    public RowViewModel(PortRow row, string line2, string addMemoText, string newText)
    {
        Row = row;
        _memo = row.Memo ?? "";
        Line2 = line2;
        AddMemoText = addMemoText;
        NewText = newText;
    }

    public PortRow Row { get; }
    public DeviceKey Key => Row.Key;
    public string PortName => Row.Record.PortName;
    public string Memo => _memo;
    public bool HasMemo => !string.IsNullOrEmpty(_memo);

    /// <summary>저장 직후 이 줄에 보이는 메모를 바꾼다. 목록 갱신이 미뤄져도 옛 글이 남지 않게 한다.</summary>
    public void ShowMemo(string memo)
    {
        if (_memo == memo) return;
        _memo = memo;
        Raise(nameof(Memo));
        Raise(nameof(HasMemo));
    }
    public string Line2 { get; }
    public string AddMemoText { get; }
    public string NewText { get; }
    public bool IsHighlighted => Row.IsHighlighted;
    public bool IsConnected => Row.Record.Present;

    /// <summary>화면에 보이는 내용이 같은지. 같으면 기존 줄을 그대로 둔다.</summary>
    public static bool SameDisplay(RowViewModel a, RowViewModel b)
        => a.Row.Record.InstanceId == b.Row.Record.InstanceId
           && a.Row.Record.PortName == b.Row.Record.PortName
           && a.Row.Record.Present == b.Row.Record.Present
           && a.Row.Record.LastArrival == b.Row.Record.LastArrival
           && a.Row.Record.LastRemoval == b.Row.Record.LastRemoval
           && a.Key == b.Key
           && a.Memo == b.Memo
           && a.Line2 == b.Line2
           && a.AddMemoText == b.AddMemoText
           && a.NewText == b.NewText
           && a.IsHighlighted == b.IsHighlighted;

    public bool IsEditing
    {
        get => _isEditing;
        set { if (_isEditing != value) { _isEditing = value; Raise(); } }
    }

    public string EditText
    {
        get => _editText;
        set { if (_editText != value) { _editText = value; Raise(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// 위젯 화면의 상태. 조회는 UI 스레드 밖에서 하고 결과는 UI 스레드에서 적용한다.
/// Refresh와 나머지 공개 멤버는 UI 스레드에서만 부른다.
/// </summary>
public sealed class WidgetViewModel : INotifyPropertyChanged
{
    public const int MemoMaxLength = MemoText.MaxLength;

    private readonly IDeviceSource _source;
    private readonly JsonStore? _store;
    private readonly Func<DateTimeOffset> _baseline;
    private readonly Func<DateTimeOffset> _clock;

    private IReadOnlyList<DeviceRecord>? _records; // 직전 조회 결과. null이면 아직 조회 전
    private ScanState _scanState = ScanState.Empty(); // 이번 실행 중 연결/미연결로 직접 본 InstanceId. 조회에서 빠진 기록도 유지한다
    private bool _emptyRecheck;                     // 연결 기록이 있었는데 빈 조회가 나와 한 번 더 확인하는 중
    private DateTimeOffset _baselineValue = DateTimeOffset.MinValue;
    private readonly RebuildGate _gate = new();
    private readonly EditTracker<RowViewModel> _editTracker = new();
    private bool _scanning, _queued;
    private bool _scanFailed, _saveFailed, _hotkeyFailed, _autoStartFailed, _dataRecovered;
    private string _statusText = "";
    private string _headerText = "";
    private string _disconnectedText = "";
    private bool _hasDisconnected;

    /// <param name="store">null이면 저장하지 않는다(--demo).</param>
    /// <param name="baseline">부팅·전원 복귀 기준 시각. 조회 스레드에서 매번 부른다.</param>
    public WidgetViewModel(IDeviceSource source, JsonStore? store,
        Func<DateTimeOffset>? baseline = null, Func<DateTimeOffset>? clock = null)
    {
        _source = source;
        _store = store;
        _baseline = baseline ?? DefaultBaseline;
        _clock = clock ?? (() => DateTimeOffset.Now);
        Data = store?.Load() ?? new AppData();
        _saveFailed = store?.LastLoadFailed ?? false;
        _dataRecovered = store?.LastLoadRecovered ?? false;
        _language = Strings.ResolveLanguage(Data.Settings.Language, CultureInfo.CurrentUICulture);
        UpdateStatus();
        UpdateTexts(0, 0);
    }

    public AppData Data { get; }
    private string _language;
    public string Language => _language;
    public ObservableCollection<RowViewModel> Connected { get; } = new();
    public ObservableCollection<RowViewModel> Disconnected { get; } = new();

    public string HeaderText { get => _headerText; private set => Set(ref _headerText, value); }
    public string DisconnectedText { get => _disconnectedText; private set => Set(ref _disconnectedText, value); }
    public bool HasDisconnected { get => _hasDisconnected; private set => Set(ref _hasDisconnected, value); }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public bool HasConnected => Connected.Count > 0;
    public string EmptyText => Strings.Get("widget.empty", Language);

    public bool AlwaysOnTop => Data.Settings.AlwaysOnTop;

    public bool DisconnectedExpanded
    {
        get => Data.Settings.DisconnectedExpanded;
        set
        {
            if (Data.Settings.DisconnectedExpanded == value) return;
            Data.Settings.DisconnectedExpanded = value;
            Raise();
            Persist();
        }
    }

    /// <summary>저장 파일을 읽지 못했는지. 이때는 첫 실행 질문의 답을 남길 수 없다.</summary>
    public bool LoadFailed => _store?.LastLoadFailed ?? false;

    /// <summary>자동 실행 등록에 실패했는지.</summary>
    public bool AutoStartFailed
    {
        get => _autoStartFailed;
        set { _autoStartFailed = value; UpdateStatus(); }
    }

    /// <summary>전역 단축키 등록에 실패했는지. 설정 화면에서 충돌을 알리는 데 쓴다.</summary>
    public bool HotkeyFailed
    {
        get => _hotkeyFailed;
        set { _hotkeyFailed = value; UpdateStatus(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // 기준 시각: 마지막 부팅과 마지막 전원 복귀 중 늦은 쪽. 로그 기록이 이벤트보다 늦을 수 있어 조회 때마다 다시 읽는다.
    private static DateTimeOffset DefaultBaseline()
    {
        var boot = PowerBaseline.GetBootTime();
        DateTimeOffset? resume = null;
        try { resume = PowerBaseline.GetLastResumeTime(); } catch (Exception) { }
        return resume is { } r && r > boot ? r : boot;
    }

    /// <summary>장치 목록을 다시 읽는다. 조회 중이면 한 번 더 하도록 표시만 남긴다.</summary>
    public void Refresh()
    {
        if (_scanning)
        {
            _queued = true;
            return;
        }
        _scanning = true;
        var ui = TaskScheduler.FromCurrentSynchronizationContext();
        Task.Run(() => (Records: _source.GetRecords(), Baseline: _baseline()))
            .ContinueWith(OnScanDone, CancellationToken.None, TaskContinuationOptions.None, ui);
    }

    private void OnScanDone(Task<(IReadOnlyList<DeviceRecord> Records, DateTimeOffset Baseline)> task)
    {
        _scanning = false;
        try
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                _ = task.Exception; // 예외를 관찰해 두고, 마지막 목록은 그대로 둔다
                _scanFailed = true;
            }
            else
            {
                // 빈 조회를 보류했으면 한 번 더 조회해서 확인한다
                if (!Apply(task.Result.Records, task.Result.Baseline)) _queued = true;
                _scanFailed = false;
            }
        }
        catch (Exception)
        {
            _scanFailed = true;
        }
        UpdateStatus();
        if (_queued)
        {
            _queued = false;
            Refresh();
        }
    }

    /// <summary>
    /// 조회 결과를 반영한다. 연결 기록이 있었는데 빈 목록이 오면 반영하지 않고 false를 돌려준다.
    /// 호출한 쪽이 한 번 더 조회하고, 그 결과도 비어 있으면 그때 반영한다.
    /// </summary>
    internal bool Apply(IReadOnlyList<DeviceRecord> records, DateTimeOffset baseline)
    {
        if (records.Count == 0 && !_emptyRecheck && _records is { } prev && prev.Any(r => r.Present))
        {
            _emptyRecheck = true;
            return false;
        }
        _emptyRecheck = false;

        var now = _clock();
        var update = ObservedUpdater.Update(Data.Observed, _scanState, records, now);
        _scanState = update.State;
        var changed = !SameObserved(Data.Observed, update.Observed);
        if (changed) Data.Observed = update.Observed;
        _records = records;
        _baselineValue = baseline;
        Rebuild();
        if (changed) Persist();
        return true;
    }

    private static bool SameObserved(Dictionary<string, ObservedTimes> a, Dictionary<string, ObservedTimes> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var (k, v) in a)
            if (!b.TryGetValue(k, out var w) || v != w) return false;
        return true;
    }

    /// <summary>
    /// 직전 조회 결과로 표시를 다시 만든다. 1분마다 시간 표기와 하이라이트를 갱신할 때도 쓴다.
    /// 내용이 바뀐 줄만 교체하고, 바뀐 것이 없으면 목록을 건드리지 않는다.
    /// 편집 중이거나 우클릭 메뉴가 열려 있으면 미루었다가 풀릴 때 다시 만든다.
    /// </summary>
    public void Rebuild()
    {
        if (_records is null) return;
        if (!_gate.TryEnter()) return;

        var now = _clock();
        var window = TimeSpan.FromMinutes(Math.Max(0, Data.Settings.HighlightMinutes));
        var list = PortListBuilder.Build(_records, Data.Memos, Data.Observed, now, _baselineValue, window);
        Sync(Connected, list.Connected, now);
        Sync(Disconnected, list.Disconnected, now);
        UpdateTexts(list.Connected.Count, list.Disconnected.Count);
        Raise(nameof(HasConnected));
    }

    private void Sync(ObservableCollection<RowViewModel> target, IReadOnlyList<PortRow> rows, DateTimeOffset now)
    {
        var addMemo = Strings.Get("widget.add_memo", Language);
        var newText = Strings.Get("widget.new", Language);
        var desired = rows.Select(r =>
        {
            var parts = new[] { r.Vendor, r.Description, TimeLabel.ForRow(r, now, Language) }
                .Where(p => !string.IsNullOrWhiteSpace(p));
            return new RowViewModel(r, string.Join(" · ", parts), addMemo, newText);
        }).ToList();

        var merged = ListMerge.Merge(target, desired, RowViewModel.SameDisplay);
        if (ListMerge.SameInstances(target, merged)) return;
        for (var i = 0; i < merged.Count; i++)
        {
            if (i < target.Count)
            {
                if (!ReferenceEquals(target[i], merged[i])) target[i] = merged[i];
            }
            else target.Add(merged[i]);
        }
        while (target.Count > merged.Count) target.RemoveAt(target.Count - 1);
    }

    private void UpdateTexts(int connected, int disconnected)
    {
        HeaderText = string.Format(Strings.Get("widget.header", Language), connected);
        DisconnectedText = string.Format(Strings.Get("widget.disconnected", Language), disconnected);
        HasDisconnected = disconnected > 0;
    }

    /// <summary>메모를 바꾼다. 줄바꿈은 공백으로, 길이는 500자로 맞추고 빈 메모는 지운다.</summary>
    public void SetMemo(DeviceKey key, string memo)
    {
        var text = MemoText.Normalize(memo);
        if (text.Length == 0) Data.Memos.Remove(key.Value);
        else Data.Memos[key.Value] = text;
        Persist(); // 저장에 실패해도 메모는 메모리에 남아 이번 실행 동안 화면에 유지된다
        Rebuild();
    }

    /// <summary>저장된 메모를 지운다. 저장하고, 화면에 보이는 줄도 바로 비운다.</summary>
    public void DeleteMemo(string key)
    {
        // 이 키의 줄을 편집 중이면 먼저 취소한다. 그렇지 않으면 편집 확정이 지운 메모를 되살린다
        foreach (var row in Connected.Concat(Disconnected).ToList())
            if (row.IsEditing && row.Key.Value == key) CancelEdit(row);
        if (!Data.Memos.Remove(key)) return;
        Persist();
        foreach (var row in Connected.Concat(Disconnected))
            if (row.Key.Value == key) row.ShowMemo("");
        Rebuild();
    }

    /// <summary>화면 언어 설정을 바꾼다("auto", "ko", "en"). 목록과 헤더 문구가 바로 바뀐다.</summary>
    public void SetLanguageSetting(string setting)
    {
        if (setting is not ("ko" or "en")) setting = "auto";
        Data.Settings.Language = setting;
        var lang = Strings.ResolveLanguage(setting, CultureInfo.CurrentUICulture);
        Persist();
        if (lang == _language) return;
        _language = lang;
        Raise(nameof(Language));
        Raise(nameof(EmptyText));
        UpdateStatus();
        UpdateTexts(Connected.Count, Disconnected.Count);
        Rebuild();
    }

    /// <summary>하이라이트 시간(분)을 바꾸고 목록을 바로 다시 계산한다.</summary>
    public void SetHighlightMinutes(int minutes)
    {
        if (Data.Settings.HighlightMinutes == minutes) return;
        Data.Settings.HighlightMinutes = minutes;
        Persist();
        Rebuild();
    }

    public void SetAlwaysOnTop(bool value)
    {
        if (Data.Settings.AlwaysOnTop == value) return;
        Data.Settings.AlwaysOnTop = value;
        Raise(nameof(AlwaysOnTop));
        Persist();
    }

    private string StoredMemo(DeviceKey key) => Data.Memos.TryGetValue(key.Value, out var m) ? m : "";

    /// <summary>우클릭 메뉴가 열려 있는 동안 목록 갱신을 미룬다.</summary>
    public void SetMenuOpen(bool open)
    {
        if (_gate.SetMenuOpen(open)) Rebuild();
    }

    /// <summary>row의 편집을 시작한다. 다른 줄이 편집 중이었으면 먼저 저장한다.</summary>
    public void BeginEdit(RowViewModel row)
    {
        if (row.IsEditing) return;
        var previous = _editTracker.Begin(row);
        _gate.SetEditing(true);
        if (previous is { IsEditing: true })
        {
            previous.IsEditing = false;
            SetMemo(previous.Key, previous.EditText);
            previous.ShowMemo(StoredMemo(previous.Key));
        }
        row.EditText = StoredMemo(row.Key); // 저장된 값이 기준이다. 줄에 보이는 값은 갱신이 미뤄져 옛 값일 수 있다
        row.IsEditing = true;
    }

    public void CommitEdit(RowViewModel row)
    {
        if (!row.IsEditing) return;
        row.IsEditing = false;
        if (_editTracker.End(row)) _gate.SetEditing(false);
        SetMemo(row.Key, row.EditText);
        row.ShowMemo(StoredMemo(row.Key));
    }

    public void CancelEdit(RowViewModel row)
    {
        if (!row.IsEditing) return;
        row.IsEditing = false;
        if (_editTracker.End(row) && _gate.SetEditing(false)) Rebuild();
    }

    /// <summary>창 위치와 크기를 저장한다. 유한한 값일 때만 기록한다(NaN은 저장 전체를 실패시킨다).</summary>
    public void SaveBounds(double left, double top, double width, double height)
    {
        if (!Num.IsFinite(left) || !Num.IsFinite(top) || !Num.IsFinite(width) || !Num.IsFinite(height))
            return;
        var s = Data.Settings;
        if (s.Left == left && s.Top == top && s.Width == width && s.Height == height) return;
        s.Left = left; s.Top = top; s.Width = width; s.Height = height;
        Persist();
    }

    /// <summary>저장하고, 실패하면 헤더에 짧은 경고를 띄운다.</summary>
    public bool Persist()
    {
        if (_store is null) return true;
        var ok = _store.Save(Data);
        _saveFailed = !ok;
        UpdateStatus();
        return ok;
    }

    private void UpdateStatus()
    {
        var parts = new List<string>();
        if (_scanFailed) parts.Add(Strings.Get("widget.status.scan_failed", Language));
        if (_saveFailed) parts.Add(Strings.Get("widget.status.save_failed", Language));
        if (_dataRecovered) parts.Add(Strings.Get("widget.status.data_recovered", Language));
        if (_hotkeyFailed) parts.Add(Strings.Get("widget.status.hotkey_failed", Language));
        if (_autoStartFailed) parts.Add(Strings.Get("widget.status.autostart_failed", Language));
        StatusText = string.Join(" · ", parts);
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Raise(name);
    }

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
