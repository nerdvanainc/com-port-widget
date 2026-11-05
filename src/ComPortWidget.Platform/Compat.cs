using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ComPortWidget.Platform;

/// <summary>.NET Framework 4.8에 없는 API를 같은 동작으로 대신하는 작은 도구 모음.</summary>
internal static class Compat
{
    /// <summary>실행 중인 exe의 전체 경로(Environment.ProcessPath 대용). 알 수 없으면 null.</summary>
    public static string? ProcessPath
    {
        get
        {
            try
            {
                using var p = Process.GetCurrentProcess();
                return p.MainModule?.FileName;
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                return null;
            }
        }
    }

    [DllImport("kernel32.dll")]
    private static extern ulong GetTickCount64();

    /// <summary>부팅 후 경과 밀리초(Environment.TickCount64 대용).</summary>
    public static long TickCount64 => (long)GetTickCount64();

    /// <summary>
    /// Path.IsPathFullyQualified와 같은 규칙(Windows). \서버, \?\, \.\ 로 시작하거나 "드라이브:\"(또는 "/")로 시작하면 참.
    /// "C:", "C:폴더", "\폴더", 상대 경로는 거짓이다.
    /// </summary>
    public static bool IsPathFullyQualified(string path)
    {
        if (path is null) throw new ArgumentNullException(nameof(path));
        if (path.Length < 2) return false;
        if (IsSep(path[0])) return path[1] == '?' || IsSep(path[1]);
        return path.Length >= 3 && path[1] == ':' && IsSep(path[2]) && IsAsciiLetter(path[0]);
    }

    private static bool IsSep(char c) => c == '\\' || c == '/';
    public static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
    public static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';

    /// <summary>CommandLineToArgvW 규칙으로 인자 하나를 따옴표 처리한다. 공백, 따옴표, 빈 문자열이 있으면 감싸고 역슬래시를 올바르게 두 배로 한다.</summary>
    public static string QuoteArgument(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0) return arg;
        var sb = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\') { backslashes++; continue; }
            if (c == '"') { sb.Append('\\', backslashes * 2 + 1).Append('"'); }
            else { sb.Append('\\', backslashes).Append(c); }
            backslashes = 0;
        }
        sb.Append('\\', backslashes * 2).Append('"');
        return sb.ToString();
    }

    /// <summary>인자 목록을 ProcessStartInfo.Arguments 한 줄로 만든다(ArgumentList 대용).</summary>
    public static string JoinArguments(IEnumerable<string> args) => string.Join(" ", args.Select(QuoteArgument));
}
