using System.ComponentModel;
using System.Diagnostics;

namespace ComPortWidget.Platform;

/// <summary>BootstrapFailed: 정리 프로세스를 시작하기 전에 준비 단계(폴더, 복사, 해시 검증)에서 멈췄다. 아무것도 바뀌지 않았고 다시 시도할 수 있다.</summary>
public enum LaunchOutcome { Completed, Cancelled, Failed, BootstrapFailed }

/// <summary>프로그램을 관리자 권한(runas)으로 실행하고 종료 코드를 돌려준다. 테스트는 가짜 구현을 쓴다.</summary>
public interface IProcessStarter
{
    /// <summary>사용자가 UAC를 거부하면 Win32Exception(1223)을, 제한 시간 안에 끝나지 않으면 TimeoutException을 던진다.</summary>
    int RunElevated(string exe, IReadOnlyList<string> args, TimeSpan timeout);
}

/// <summary>
/// UAC 승격된 PowerShell로 exe를 관리자 전용 임시 폴더에 복사해 정리 전용 프로세스를 띄우고 결과 파일까지 확인한다.
/// exe를 직접 승격하지 않는다(단일 exe는 시작 전에 옆 폴더의 DLL을 읽을 수 있다).
/// </summary>
public sealed class ResetLauncher(IProcessStarter starter, string? exePath = null, Func<string, string>? hashFile = null)
{
    private const int ErrorCancelled = 1223;

    /// <summary>System32의 Windows PowerShell 절대경로.</summary>
    public static string PowerShellPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");

    private static string Sha256Of(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fs));
    }
    private const int BaseSeconds = 120;
    private const int SecondsPerItem = 70;
    /// <summary>기다리는 시간의 상한(30분). 항목이 많아도 창과 앱이 몇 시간씩 닫히지 않게 한다.</summary>
    internal const int MaxSeconds = 1800;

    internal static TimeSpan WaitFor(int itemCount)
        => TimeSpan.FromSeconds(Math.Min(MaxSeconds, BaseSeconds + (long)SecondsPerItem * Math.Max(0, itemCount)));

    /// <summary>두 경로는 모두 절대경로여야 한다(승격 계정이 달라도 같은 파일을 가리키게 하기 위해).</summary>
    /// <param name="itemCount">계획의 항목 수. 기다리는 시간은 120초에 항목당 70초를 더하고 30분을 넘기지 않는다.</param>
    public LaunchOutcome Run(string planPath, string resultPath, int itemCount = 0, bool dryRun = false)
    {
        if (!Path.IsPathFullyQualified(planPath) || !Path.IsPathFullyQualified(resultPath))
            throw new ArgumentException("Plan and result paths must be absolute.");

        // 이전 실행이 남긴 결과를 이번 결과로 오인하지 않게 먼저 지운다
        try { File.Delete(resultPath); }
        catch (Exception) { return LaunchOutcome.Failed; }

        int exit;
        try
        {
            var exe = exePath ?? Environment.ProcessPath
                ?? throw new InvalidOperationException("Process path is unknown.");
            var hash = (hashFile ?? Sha256Of)(exe);
            var script = ElevationScript.Build(exe, hash, planPath, resultPath, Guid.NewGuid().ToString("D"), dryRun);
            exit = starter.RunElevated(PowerShellPath,
                ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", ElevationScript.Encode(script)],
                WaitFor(itemCount));
        }
        catch (Win32Exception e) when (e.NativeErrorCode == ErrorCancelled)
        {
            return LaunchOutcome.Cancelled;
        }
        catch (Exception)
        {
            return LaunchOutcome.Failed;
        }

        if (exit is ElevationScript.ExitFolderFailed or ElevationScript.ExitCopyFailed or ElevationScript.ExitHashMismatch)
            return LaunchOutcome.BootstrapFailed;
        if (exit != 0) return LaunchOutcome.Failed;
        return ResetFiles.ReadResult(resultPath) is null ? LaunchOutcome.Failed : LaunchOutcome.Completed;
    }
}

/// <summary>ShellExecute의 runas 동사로 승격 실행하고 끝날 때까지 기다린다.</summary>
public sealed class ShellProcessStarter : IProcessStarter
{
    public int RunElevated(string exe, IReadOnlyList<string> args, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Process did not start.");
        // 승격된 프로세스는 이쪽에서 끝낼 수 없으므로 기다리기를 멈추고 호출부가 실패로 처리한다
        if (!p.WaitForExit(timeout))
            throw new TimeoutException("The elevated process did not finish in time.");
        return p.ExitCode;
    }
}
