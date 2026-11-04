using System.Reflection;
using System.Runtime.InteropServices;

namespace ComPortWidget.Platform;

/// <summary>
/// 시스템 DLL을 항상 System32의 절대경로로 읽게 한다. exe와 같은 폴더에 심어 둔 DLL(DLL planting)이
/// 관리자 권한 프로세스에 로드되는 것을 막는다. 허용 목록에 없는 이름은 거부한다.
/// </summary>
public static class SystemDlls
{
    private const uint LoadLibrarySearchSystem32 = 0x00000800;

    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "kernel32", "user32", "advapi32", "setupapi", "cfgmgr32", "msports",
    };

    /// <summary>프로세스 시작 직후에 부른다. P/Invoke가 있는 어셈블리를 모두 넘긴다.</summary>
    public static void Install(params Assembly[] assemblies)
    {
        foreach (var asm in assemblies)
        {
            try { NativeLibrary.SetDllImportResolver(asm, Resolve); }
            catch (InvalidOperationException) { /* 이미 등록됨 */ }
        }
        // 이후의 모든 LoadLibrary 검색을 System32로 제한한다
        SetDefaultDllDirectories(LoadLibrarySearchSystem32);
    }

    /// <summary>라이브러리 이름을 System32의 절대경로로 바꾼다. 허용 목록에 없거나 경로 모양이면 예외.</summary>
    internal static string PathFor(string libraryName)
    {
        var name = libraryName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? libraryName[..^4]
            : libraryName;
        if (!Allowed.Contains(name))
            throw new DllNotFoundException("Library is not allowed: " + libraryName);
        return Path.Combine(Environment.SystemDirectory, name + ".dll");
    }

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        => NativeLibrary.Load(PathFor(libraryName));

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDefaultDllDirectories(uint directoryFlags);
}
