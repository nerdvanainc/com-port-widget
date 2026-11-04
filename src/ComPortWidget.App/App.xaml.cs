using System.IO;
using System.Windows;
using System.Windows.Threading;
using ComPortWidget.Core.Devices;
using ComPortWidget.Core.Storage;
using ComPortWidget.Core.Text;
using ComPortWidget.Platform;

namespace ComPortWidget.App;

public partial class App : Application
{
    private const string MutexName = @"Local\ComPortWidget.SingleInstance";
    private const string ActivateEventName = @"Local\ComPortWidget.Activate";

    private bool _demo;
    private bool _demoAutoStart;
    private Mutex? _mutex;
    private EventWaitHandle? _activateEvent;
    private WidgetViewModel? _vm;
    private WidgetWindow? _window;
    private SettingsWindow? _settingsWindow;
    private ResetWindow? _resetWindow;
    private TrayIcon? _tray;
    private GlobalHotkey? _hotkey;
    private DeviceWatcher? _watcher;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _demo = e.Args.Contains("--demo");
        // --demo는 아무것도 파일에 쓰지 않으므로 오류 기록도 설치하지 않는다
        if (!_demo) InstallCrashLogging();

        // --demo는 단일 실행 잠금, 저장 파일, 첫 실행 질문을 모두 건너뛴다
        if (!_demo && !AcquireSingleInstance())
        {
            Shutdown();
            return;
        }

        IDeviceSource source = _demo ? new DemoDeviceSource() : new WindowsDeviceSource();
        JsonStore? store = _demo ? null : new JsonStore(DataFilePath());
        _vm = new WidgetViewModel(source, store);
        if (_demo)
        {
            // 데모는 저장하지 않는 메모를 메모리에만 채운다
            foreach (var r in source.GetRecords())
                if (DemoDeviceSource.MemosByPort.TryGetValue(r.PortName, out var memo))
                    _vm.Data.Memos[DeviceIdentity.Resolve(r).Value] = memo;
        }
        _window = new WidgetWindow(_vm);
        _tray = new TrayIcon(_vm.Language);
        _tray.ShowRequested += ShowWidget;
        _tray.ResetRequested += OpenReset;
        _tray.SettingsRequested += OpenSettings;
        _tray.ExitRequested += ExitApp;
        _window.SettingsRequested += OpenSettings;
        // 언어가 바뀌면 트레이 메뉴도 바로 바꾼다. 위젯과 설정 창은 각자 같은 알림을 받는다
        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(WidgetViewModel.Language)) _tray.SetLanguage(_vm.Language);
        };
        _window.Show();

        // 메시지 루프가 돌기 시작한 뒤에 감시와 첫 조회를 시작한다
        Dispatcher.BeginInvoke(StartRuntime, DispatcherPriority.Loaded);
    }

    private int _handledCrashes;
    private bool _runtimeStarted; // StartRuntime이 끝까지 실행됐는지

    // 처리되지 않은 예외를 crash.log에 남긴다. 화면 스레드 예외는 가능하면 앱을 살려 둔다
    private void InstallCrashLogging()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            CrashLog.Write(CrashLog.DefaultPath, "DispatcherUnhandledException", args.Exception, DateTimeOffset.Now);
            // 시작이 끝나기 전의 예외와, 같은 오류가 끝없이 반복되는 경우는 살리지 않고 종료한다
            args.Handled = CrashLog.ShouldKeepAlive(_runtimeStarted, ++_handledCrashes);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            CrashLog.Write(CrashLog.DefaultPath, "AppDomain.UnhandledException", args.ExceptionObject as Exception, DateTimeOffset.Now);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            CrashLog.Write(CrashLog.DefaultPath, "TaskScheduler.UnobservedTaskException", args.Exception, DateTimeOffset.Now);
            args.SetObserved();
        };
    }

    private static string DataFilePath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ComPortWidget", "data.json");

    // 이미 실행 중이면 그쪽 창을 앞으로 부르게 알리고 false를 돌려준다
    private bool AcquireSingleInstance()
    {
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        _mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            _activateEvent.Set();
            return false;
        }
        var listener = new Thread(() =>
        {
            while (_activateEvent.WaitOne())
                Dispatcher.BeginInvoke(ShowWidget);
        }) { IsBackground = true, Name = "ActivateListener" };
        listener.Start();
        return true;
    }

    private void StartRuntime()
    {
        var vm = _vm!;
        if (!_demo)
        {
            _hotkey = new GlobalHotkey();
            _hotkey.Pressed += ShowWidget;
            // 등록에 실패해도 창은 이미 떠 있다. 충돌은 헤더 상태와 HotkeyFailed로 알린다
            vm.HotkeyFailed = !_hotkey.Register(vm.Data.Settings.Hotkey);

            // 조회는 연결 이벤트, 전원 복귀, 30초 주기 확인마다 한다
            _watcher = new DeviceWatcher();
            _watcher.Changed += vm.Refresh;
            _watcher.PowerResumed += vm.Refresh;
            _watcher.Start();
        }
        vm.Refresh();
        // 창, 트레이, 감시가 모두 갖춰졌다. 아래 질문 창은 메시지 루프를 돌리므로 그 전에 표시한다
        _runtimeStarted = true;

        if (!_demo && !vm.LoadFailed && !vm.Data.Settings.AutoStartAsked)
            AskAutoStart(vm);
    }

    // 첫 실행 때만 묻는다. 저장 파일을 읽지 못한 상태에서는 답을 남길 수 없으므로 묻지 않는다
    private void AskAutoStart(WidgetViewModel vm)
    {
        var lang = vm.Language;
        var answer = MessageBox.Show(_window!, Strings.Get("autostart.prompt", lang), Strings.Get("app.name", lang),
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes && !AutoStart.Set(true))
        {
            // 등록에 실패하면 헤더에 알리고, 답을 남기지 않아 다음 실행에 다시 묻는다
            vm.AutoStartFailed = true;
            return;
        }
        vm.Data.Settings.AutoStartAsked = true;
        vm.Persist();
    }

    // 설정 창은 하나만 연다. 이미 열려 있으면 앞으로 가져온다
    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.BringToFront();
            return;
        }
        var sv = new SettingsViewModel(_vm!,
            _demo ? null : text => _hotkey?.Register(text) ?? true,
            // --demo는 실제 레지스트리를 읽거나 쓰지 않는다
            _demo ? () => _demoAutoStart : AutoStart.IsEnabled,
            _demo ? v => { _demoAutoStart = v; return true; } : AutoStart.Set);
        _settingsWindow = new SettingsWindow(sv, _demo);
        _settingsWindow.ResetRequested += OpenReset;
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.BringToFront();
    }

    // 정리 창은 하나만 연다. 실행이 끝나면 위젯 목록을 다시 읽는다
    private void OpenReset()
    {
        if (_resetWindow is not null)
        {
            _resetWindow.Activate();
            return;
        }
        _resetWindow = new ResetWindow(_vm!, _demo, () => _vm!.Refresh());
        _resetWindow.Closed += (_, _) => _resetWindow = null;
        _resetWindow.Show();
        _resetWindow.Activate();
    }

    private void ShowWidget() => _window?.BringToFront();

    private void ExitApp()
    {
        // 정리를 실행하는 중에 종료하면 결과를 받지 못하고 승격 프로세스만 남는다
        if (_resetWindow is { IsBusy: true })
        {
            var lang = _vm!.Language;
            MessageBox.Show(_resetWindow, Strings.Get("reset.exitBlocked", lang), Strings.Get("app.name", lang),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _watcher?.Dispose();
        _settingsWindow?.Close();
        _resetWindow?.Close();
        _hotkey?.Dispose();
        _tray?.Dispose();
        _window?.CloseForExit();
        try { _mutex?.ReleaseMutex(); } catch (ApplicationException) { }
        Shutdown();
    }
}
