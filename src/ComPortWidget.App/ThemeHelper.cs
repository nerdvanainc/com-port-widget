using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ComPortWidget.App;

/// <summary>창 제목 표시줄을 어둡게 한다(Windows 10 2004 이상과 11). 지원하지 않거나 실패하면 그냥 둔다.</summary>
internal static class ThemeHelper
{
    private const int DwmaUseImmersiveDarkModeOld = 19; // Windows 10 1903~1909
    private const int DwmaUseImmersiveDarkMode = 20;    // Windows 10 2004 이상, 11

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>앱 시작 때 한 번 부른다. 이후 열리는 모든 창의 Loaded에서 제목 표시줄을 어둡게 한다.</summary>
    public static void RegisterForAllWindows()
        => EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => { if (sender is Window w) ApplyDarkTitleBar(w); }));

    public static void ApplyDarkTitleBar(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            var on = 1;
            if (DwmSetWindowAttribute(handle, DwmaUseImmersiveDarkMode, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, DwmaUseImmersiveDarkModeOld, ref on, sizeof(int));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            // 어두운 제목 표시줄은 꾸밈일 뿐이다
        }
    }
}
