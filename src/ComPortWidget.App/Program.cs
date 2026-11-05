using System.Runtime.CompilerServices;
using ComPortWidget.Platform;

namespace ComPortWidget.App;

/// <summary>
/// 진입점. 정리 전용 모드(--reset-plan)는 WPF Application을 만들기 전에 처리해서,
/// 관리자 권한 프로세스가 WPF 네이티브 라이브러리를 읽지 않게 한다.
/// </summary>
internal static class Program
{
    /// <summary>정리 전용 모드에서 DLL 검색 경로 제한을 걸지 못했을 때의 종료 코드(ResetRunner의 0, 2~4와 겹치지 않는다).</summary>
    internal const int ExitDllHardeningFailed = 5;

    [STAThread]
    public static int Main(string[] args)
    {
        // 무엇보다 먼저: 시스템 DLL을 System32에서만 읽게 한다
        var hardened = SystemDlls.Install();

        if (ResetRunner.IsResetMode(args))
        {
            // 관리자 권한 모드에서는 프로세스 전체의 DLL 검색 제한이 걸리지 않으면 시작하지 않는다
            if (!hardened) return ExitDllHardeningFailed;
            return ResetRunner.Run(args);
        }
        // 일반 모드는 어셈블리 수준 선언이 P/Invoke를 계속 지켜 주므로 계속 실행하되 기록은 남긴다
        if (!hardened)
            CrashLog.Write(CrashLog.DefaultPath, "SetDefaultDllDirectories failed; continuing with the assembly-level DLL search restriction only", null, DateTimeOffset.Now);
        if (!FrameworkVersion.CheckAtStartup()) return 1;
        return RunWidget();
    }

    // WPF 형식은 이 메서드 안에서만 쓴다(정리 모드에서 JIT되지 않게 분리)
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunWidget()
    {
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
