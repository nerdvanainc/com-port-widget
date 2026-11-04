using System.Runtime.InteropServices;
using System.Windows.Forms;
using ComPortWidget.Core.Devices;

namespace ComPortWidget.Platform;

/// <summary>
/// 장치 연결·제거와 전원 재개를 감지한다. Start는 메시지 루프가 도는 UI 스레드에서 부르며,
/// Changed와 PowerResumed도 그 스레드에서 발생한다.
/// 화면에 보이지 않는 최상위 창을 쓴다. HWND_MESSAGE 창은 DBT_DEVNODES_CHANGED 브로드캐스트를 받지 못하기 때문이다.
/// </summary>
public sealed class DeviceWatcher : IDisposable
{
    private const int WM_DEVICECHANGE = 0x0219;
    private const int WM_POWERBROADCAST = 0x0218;
    private const int PBT_APMRESUMEAUTOMATIC = 0x0012;
    private const int DBT_DEVTYP_DEVICEINTERFACE = 5;
    private const int DEVICE_NOTIFY_WINDOW_HANDLE = 0;
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private static readonly Guid GuidDevInterfaceComPort = new("86E0D1E0-8089-11D0-9CE4-08003E301F73");

    private Sink? _window;
    private IntPtr _notifyHandle;
    private FormsDelayTimer? _delayTimer;
    private Debouncer? _debouncer;
    private System.Windows.Forms.Timer? _pollTimer;
    private SynchronizationContext? _context;
    private bool _started;
    private bool _disposed;

    public event Action? Changed;
    public event Action? PowerResumed;

    public void Start()
    {
        if (_started || _disposed) return;
        _started = true;
        _context = SynchronizationContext.Current; // WPF Dispatcher 문맥. 구독자 예외가 WPF로 전달되게 한다

        _delayTimer = new FormsDelayTimer();
        _debouncer = new Debouncer(DebounceDelay, RaiseChanged, _delayTimer);
        _window = new Sink(this);

        // DEV_BROADCAST_DEVICEINTERFACE_W: size, devicetype, reserved, classguid, name[1] (정렬 포함 32바이트)
        const int size = 32;
        var buf = Marshal.AllocHGlobal(size);
        try
        {
            for (var i = 0; i < size; i++) Marshal.WriteByte(buf, i, 0);
            Marshal.WriteInt32(buf, 0, size);
            Marshal.WriteInt32(buf, 4, DBT_DEVTYP_DEVICEINTERFACE);
            Marshal.Copy(GuidDevInterfaceComPort.ToByteArray(), 0, buf + 12, 16);
            _notifyHandle = RegisterDeviceNotificationW(_window.Handle, buf, DEVICE_NOTIFY_WINDOW_HANDLE);
            // 등록에 실패해도 DBT_DEVNODES_CHANGED 브로드캐스트와 30초 주기 확인이 남아 있다
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }

        _pollTimer = new System.Windows.Forms.Timer { Interval = (int)PollInterval.TotalMilliseconds };
        _pollTimer.Tick += (_, _) => RaiseChanged();
        _pollTimer.Start();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_notifyHandle != IntPtr.Zero)
        {
            UnregisterDeviceNotification(_notifyHandle);
            _notifyHandle = IntPtr.Zero;
        }
        _pollTimer?.Stop();
        _pollTimer?.Dispose();
        _pollTimer = null;
        _delayTimer?.Dispose();
        _delayTimer = null;
        _window?.DestroyHandle();
        _window = null;
    }

    private void OnMessage(int msg, IntPtr wParam)
    {
        if (_disposed) return;
        if (msg == WM_DEVICECHANGE) _debouncer?.Trigger();
        else if (msg == WM_POWERBROADCAST && wParam.ToInt64() == PBT_APMRESUMEAUTOMATIC) Raise(() => PowerResumed?.Invoke());
    }

    private void RaiseChanged() => Raise(() => Changed?.Invoke());

    /// <summary>캡처한 동기화 문맥으로 이벤트를 올린다. 실행 시점에 이미 해제됐으면 올리지 않는다.</summary>
    private void Raise(Action fire)
    {
        void Run()
        {
            if (_disposed) return;
            fire();
        }
        var ctx = _context;
        if (ctx is null) Run();
        else ctx.Post(_ => Run(), null);
    }

    /// <summary>테스트용. 감시 창 핸들.</summary>
    internal IntPtr WindowHandle => _window?.Handle ?? IntPtr.Zero;

    /// <summary>부모 없는 최상위 창. 보이지 않지만 메시지 전용 창이 아니다.</summary>
    private sealed class Sink : NativeWindow
    {
        private readonly DeviceWatcher _owner;

        public Sink(DeviceWatcher owner)
        {
            _owner = owner;
            CreateHandle(new CreateParams { Caption = "ComPortWidget.DeviceWatcher", ClassName = "STATIC", Style = 0 });
        }

        protected override void WndProc(ref Message m)
        {
            _owner.OnMessage(m.Msg, m.WParam);
            base.WndProc(ref m);
        }
    }

    private sealed class FormsDelayTimer : IDelayTimer, IDisposable
    {
        private readonly System.Windows.Forms.Timer _timer = new();
        private Action? _callback;

        public FormsDelayTimer()
        {
            _timer.Tick += (_, _) =>
            {
                _timer.Stop();
                var cb = _callback;
                _callback = null;
                cb?.Invoke();
            };
        }

        public void Restart(TimeSpan delay, Action callback)
        {
            _timer.Stop();
            _callback = callback;
            _timer.Interval = Math.Max(1, (int)delay.TotalMilliseconds);
            _timer.Start();
        }

        public void Dispose()
        {
            _timer.Stop();
            _timer.Dispose();
        }
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr RegisterDeviceNotificationW(IntPtr hRecipient, IntPtr notificationFilter, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterDeviceNotification(IntPtr handle);
}
