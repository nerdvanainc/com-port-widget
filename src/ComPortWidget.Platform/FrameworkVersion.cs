using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ComPortWidget.Platform;

/// <summary>설치된 .NET Framework가 4.8 이상인지 확인한다. 낮으면 이상하게 실패하는 대신 이유를 알려 준다.</summary>
public static class FrameworkVersion
{
    /// <summary>NDP\v4\Full의 Release 값에서 .NET Framework 4.8의 최솟값(Windows 10 1903 기준 528040).</summary>
    public const int Minimum48 = 528040;

    public static bool IsSupported(int? release) => release is int r && r >= Minimum48;

    public static int? ReadRelease()
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full");
            return key?.GetValue("Release") is int r ? r : (int?)null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 시작 때 부른다. 읽을 수 없는 경우는 막지 않는다(이미 .NET Framework 위에서 실행 중이므로).
    /// 값이 읽히는데 4.8보다 낮으면 안내 창을 띄우고 false를 돌려준다.
    /// </summary>
    public static bool CheckAtStartup()
    {
        var release = ReadRelease();
        if (release is null || IsSupported(release)) return true;
        var korean = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko";
        var text = korean
            ? "COM Port Widget에는 .NET Framework 4.8이 필요합니다. Windows 10 버전 1903 이상에는 기본으로 들어 있습니다. Windows를 업데이트한 뒤 다시 실행하십시오."
            : "COM Port Widget needs .NET Framework 4.8, which is included in Windows 10 version 1903 and later. Update Windows and run it again.";
        MessageBoxW(IntPtr.Zero, text, "COM Port Widget", 0x10 /* MB_ICONERROR */);
        return false;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
