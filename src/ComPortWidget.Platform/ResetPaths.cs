namespace ComPortWidget.Platform;

/// <summary>
/// 정리 전용 모드(관리자 권한)가 받아들이는 파일 위치 규칙.
/// 계획과 결과는 같은 폴더의 정해진 이름만 허용하고, 경로 어디에도 재분석 지점(심볼릭 링크, 접합 지점)이 없어야 한다.
/// </summary>
public static class ResetPaths
{
    public const string PlanName = "reset-plan.json";
    public const string ResultName = "reset-result.json";

    /// <summary>
    /// 경로 검사. .NET Framework의 Path 함수는 잘못된 문자(| " &lt; * :), 대체 데이터 스트림 모양, 너무 긴 경로, NUL에 예외를 던지므로
    /// 어떤 입력이든 예외 없이 실패(거짓)로 돌려준다.
    /// </summary>
    public static bool TryValidate(string? planPath, string? resultPath, out string folder, out string error)
    {
        try
        {
            return TryValidateCore(planPath, resultPath, out folder, out error);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException
                                       or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            folder = "";
            error = "Path is not valid: " + ex.GetType().Name;
            return false;
        }
    }

    private static bool TryValidateCore(string? planPath, string? resultPath, out string folder, out string error)
    {
        folder = "";
        error = "";
        if (string.IsNullOrEmpty(planPath) || string.IsNullOrEmpty(resultPath))
            return Fail("Plan and result paths are required.", out error);
        if (!IsPlainAbsolute(planPath!) || !IsPlainAbsolute(resultPath!))
            return Fail("Paths must be absolute local paths without relative segments.", out error);
        if (!string.Equals(Path.GetFileName(planPath), PlanName, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetFileName(resultPath), ResultName, StringComparison.OrdinalIgnoreCase))
            return Fail("File names must be " + PlanName + " and " + ResultName + ".", out error);

        var dir = Path.GetDirectoryName(planPath)!;
        if (!string.Equals(dir, Path.GetDirectoryName(resultPath), StringComparison.OrdinalIgnoreCase))
            return Fail("Plan and result must be in the same folder.", out error);
        if (!Directory.Exists(dir))
            return Fail("Folder does not exist.", out error);

        try
        {
            for (FileSystemInfo? d = new DirectoryInfo(dir); d is not null; d = ((DirectoryInfo)d).Parent)
                if (d.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    return Fail("A path component is a reparse point.", out error);
            foreach (var f in new[] { planPath, resultPath })
                if (File.Exists(f) && File.GetAttributes(f).HasFlag(FileAttributes.ReparsePoint))
                    return Fail("A file is a reparse point.", out error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail("Path could not be inspected: " + ex.Message, out error);
        }

        folder = dir;
        return true;
    }

    // 드라이브 문자 경로만 받는다(UNC, \\?\ 접두사 제외). ".." 같은 정리되지 않은 경로도 거부한다
    private static bool IsPlainAbsolute(string path)
        => Compat.IsPathFullyQualified(path)
           && path.Length > 3 && Compat.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\'
           && string.Equals(Path.GetFullPath(path), path, StringComparison.OrdinalIgnoreCase);

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }
}
