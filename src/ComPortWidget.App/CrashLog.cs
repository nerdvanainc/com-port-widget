using System.IO;
using System.Text;

namespace ComPortWidget.App;

/// <summary>
/// 처리되지 않은 예외를 %APPDATA%\ComPortWidget\crash.log 에 덧붙여 쓴다. 예외 텍스트만 기록한다.
/// 로그를 쓰다 실패해도 앱을 더 망가뜨리지 않도록 예외는 삼킨다.
/// </summary>
internal static class CrashLog
{
    /// <summary>이 크기를 넘으면 새로 쓰기 전에 파일을 비운다.</summary>
    internal const long MaxBytes = 256 * 1024;

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ComPortWidget", "crash.log");

    internal static string Format(string source, Exception? ex, DateTimeOffset now)
        => $"[{now:yyyy-MM-dd HH:mm:ss zzz}] {source}{Environment.NewLine}{ex?.ToString() ?? "(no exception object)"}{Environment.NewLine}{Environment.NewLine}";

    /// <summary>한 번에 살려 두는 화면 스레드 예외의 상한. 같은 오류가 끝없이 반복되면 종료한다.</summary>
    internal const int MaxKeepAlive = 20;

    /// <summary>
    /// 화면 스레드 예외를 처리됨으로 표시해 앱을 살려 둘지.
    /// 시작이 끝나기 전의 예외는 살리지 않는다. 살리면 창도 트레이도 없는 채로 프로세스와 단일 실행 잠금만 남아 이후 실행이 모두 그냥 끝난다.
    /// </summary>
    internal static bool ShouldKeepAlive(bool runtimeStarted, int handledCount)
        => runtimeStarted && handledCount <= MaxKeepAlive;

    /// <summary>기록에 성공하면 true.</summary>
    internal static bool Write(string path, string source, Exception? ex, DateTimeOffset now)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path) && new FileInfo(path).Length > MaxBytes) File.Delete(path);
            File.AppendAllText(path, Format(source, ex, now), new UTF8Encoding(false));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
