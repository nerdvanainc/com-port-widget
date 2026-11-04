using System.Runtime.InteropServices;
using System.Windows.Interop;
using ComPortWidget.Core.Text;

namespace ComPortWidget.App;

/// <summary>RegisterHotKey 래퍼. 메시지 전용 창으로 WM_HOTKEY를 받는다. UI 스레드에서 쓴다.</summary>
internal sealed class GlobalHotkey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;
    private const int HotkeyId = 0x4350;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private readonly HwndSource _source;
    private bool _registered;

    public event Action? Pressed;

    public GlobalHotkey()
    {
        _source = new HwndSource(new HwndSourceParameters("ComPortWidgetHotkey") { ParentWindow = HWND_MESSAGE });
        _source.AddHook(WndProc);
    }

    /// <summary>"Ctrl+Alt+Shift+C" 형식의 문자열로 등록한다. 해석하지 못하거나 다른 프로그램이 쓰는 중이면 false.</summary>
    public bool Register(string text)
    {
        Unregister();
        if (!HotkeyParser.TryParse(text, out var mods, out var vk)) return false;
        _registered = RegisterHotKey(_source.Handle, HotkeyId, mods | MOD_NOREPEAT, vk);
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered) return;
        UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke();
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
