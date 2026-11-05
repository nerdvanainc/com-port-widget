using System.Runtime.InteropServices;

// 이 어셈블리(릴리스에서는 exe 하나)의 모든 DllImport는 System32에서만 DLL을 찾는다.
// exe 옆에 심어 둔 DLL을 P/Invoke가 읽지 않게 하는 실제 보호 장치가 이 선언이다.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace ComPortWidget.Platform;

/// <summary>
/// 시작 직후 프로세스 전체의 네이티브 DLL 검색 경로를 System32로 제한한다.
/// 보호는 두 가지다. (1) 위의 어셈블리 수준 DefaultDllImportSearchPaths 선언, (2) 이 클래스의 SetDefaultDllDirectories.
/// 그 밖의 경로, 예를 들어 CLR이 시작할 때 읽는 exe 옆의 .config 파일은 막지 못한다(NOTICE 참조).
/// </summary>
public static class SystemDlls
{
    private const uint LoadLibrarySearchSystem32 = 0x00000800;

    /// <summary>
    /// 프로세스 시작 직후에 부른다. 성공하면 true. 실패하면(이 API가 없는 오래된 Windows 등) false를 돌려줄 뿐
    /// 다른 보정은 하지 않는다. 어셈블리 수준 선언이 P/Invoke를 계속 System32로 고정한다.
    /// </summary>
    public static bool Install()
    {
        try { return SetDefaultDllDirectories(LoadLibrarySearchSystem32); }
        catch (EntryPointNotFoundException) { return false; }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDefaultDllDirectories(uint directoryFlags);
}
