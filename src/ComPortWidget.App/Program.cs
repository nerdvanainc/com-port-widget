using System.Runtime.CompilerServices;
using ComPortWidget.Platform;

namespace ComPortWidget.App;

/// <summary>
/// 진입점. 정리 전용 모드(--reset-plan)는 WPF Application을 만들기 전에 처리해서,
/// 관리자 권한 프로세스가 WPF 네이티브 라이브러리를 읽지 않게 한다.
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // 무엇보다 먼저: 시스템 DLL을 System32에서만 읽게 한다
        SystemDlls.Install(typeof(SystemDlls).Assembly, typeof(Program).Assembly);

        if (ResetRunner.IsResetMode(args))
            return ResetRunner.Run(args);
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
