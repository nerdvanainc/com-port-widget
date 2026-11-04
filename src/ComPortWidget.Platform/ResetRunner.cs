using ComPortWidget.Core.Reset;

namespace ComPortWidget.Platform;

/// <summary>
/// 정리 전용 모드(--reset-plan)의 진입점. 창을 띄우지 않고 계획을 검증해 실행한 뒤 결과 파일을 쓴다.
/// 관리자 권한으로 실행되므로 인자와 파일 위치를 엄격히 검사한다(ResetPaths 참고).
/// 종료 코드: 0 정상, 2 중단(계획 오류 포함), 3 인자/경로 오류(아무것도 쓰지 않음), 4 결과 파일을 쓰지 못함.
/// </summary>
public static class ResetRunner
{
    public const int ExitOk = 0;
    public const int ExitAborted = 2;
    public const int ExitBadArgs = 3;
    public const int ExitResultWriteFailed = 4;

    public static bool IsResetMode(IReadOnlyList<string> args)
        => args.Any(a => string.Equals(a, "--reset-plan", StringComparison.Ordinal));

    public static int Run(IReadOnlyList<string> args)
        => Run(args, (dir, dry) => new WindowsResetBackend(dir, dry));

    internal static int Run(IReadOnlyList<string> args, Func<string, bool, IResetBackend> createBackend)
    {
        // 모르는 인자(--dryrun 같은 오타, --demo 포함)가 있으면 실제 실행으로 넘어가지 않도록 거부한다
        if (!TryParse(args, out var planPath, out var resultPath, out var dryRun)) return ExitBadArgs;
        if (!ResetPaths.TryValidate(planPath, resultPath, out var folder, out _)) return ExitBadArgs;

        ResetResult outcome;
        IReadOnlyList<string> actions = [];
        try
        {
            outcome = Execute(planPath, folder, dryRun, createBackend, out actions);
        }
        catch (Exception ex)
        {
            outcome = new ResetResult(true, "reset.abortUnexpected", [], ex.Message);
        }

        try
        {
            ResetFiles.WriteResult(resultPath, outcome, dryRun, actions);
        }
        catch (Exception)
        {
            return ExitResultWriteFailed;
        }
        return outcome.Aborted ? ExitAborted : ExitOk;
    }

    private static ResetResult Execute(string planPath, string folder, bool dryRun,
        Func<string, bool, IResetBackend> createBackend, out IReadOnlyList<string> actions)
    {
        actions = [];
        ResetPlan plan;
        try
        {
            plan = ResetFiles.ReadPlan(planPath);
        }
        catch (Exception ex)
        {
            return new ResetResult(true, "reset.abortPlanInvalid", [], ex.Message);
        }

        // 백업도 검증된 같은 폴더에 둔다
        var backend = createBackend(folder, dryRun);
        var result = new ResetExecutor(backend).Execute(plan);
        if (backend is IDryRunLog log) actions = log.Actions.ToList();
        return result;
    }

    // 허용: --reset-plan <값>, --reset-result <값>, --dry-run (각 한 번). 그 밖은 모두 거부
    private static bool TryParse(IReadOnlyList<string> args, out string plan, out string result, out bool dryRun)
    {
        string? p = null, r = null;
        dryRun = false;
        plan = result = "";
        var sawDry = false;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--reset-plan" when p is null && i + 1 < args.Count:
                    p = args[++i];
                    break;
                case "--reset-result" when r is null && i + 1 < args.Count:
                    r = args[++i];
                    break;
                case "--dry-run" when !sawDry:
                    sawDry = true;
                    break;
                default:
                    return false;
            }
        }
        if (p is null || r is null) return false;
        plan = p;
        result = r;
        dryRun = sawDry;
        return true;
    }
}
