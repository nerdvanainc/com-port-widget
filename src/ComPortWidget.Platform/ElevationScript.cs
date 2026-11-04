using System.Text;

namespace ComPortWidget.Platform;

/// <summary>
/// 승격 실행용 PowerShell 스크립트를 만든다(순수 함수).
/// 단일 exe는 시작 전에 exe 옆의 DLL을 읽을 수 있으므로, 관리자 전용 임시 폴더로 exe를 복사해 검증한 뒤 거기서 실행한다.
/// 준비 단계 실패의 종료 코드: 10 폴더가 이미 있거나 만들 수 없음, 11 복사 실패, 12 해시 불일치(또는 해시 계산 실패).
/// 스크립트는 .NET API만 쓴다(cmdlet이나 New-Object 같은 명령은 하나도 없다). 사용자가 쓸 수 있는 PSModulePath의 모듈이 cmdlet을 가로챌 수 있기 때문이다.
/// </summary>
public static class ElevationScript
{
    public const int ExitFolderFailed = 10;
    public const int ExitCopyFailed = 11;
    public const int ExitHashMismatch = 12;

    /// <param name="exePath">복사해서 실행할 exe(보통 Environment.ProcessPath)</param>
    /// <param name="expectedSha256">exePath의 SHA-256(16진수). 복사본이 다르면 실행하지 않는다</param>
    /// <param name="guid">임시 폴더 이름에 쓰는 GUID</param>
    /// <param name="rootOverride">시험용: 폴더를 만들 위치. 기본은 Program Files</param>
    public static string Build(string exePath, string expectedSha256, string planPath, string resultPath,
        string guid, bool dryRun = false, string? rootOverride = null)
    {
        if (!Guid.TryParse(guid, out var parsed)) throw new ArgumentException("guid is not a GUID.", nameof(guid));
        if (expectedSha256.Length != 64 || !expectedSha256.All(Uri.IsHexDigit))
            throw new ArgumentException("expectedSha256 must be 64 hex digits.", nameof(expectedSha256));
        var folderName = "ComPortWidget-Cleanup-" + parsed.ToString("N");
        var root = rootOverride is null ? "[Environment]::GetFolderPath('ProgramFiles')" : Q(rootOverride);
        var dryPart = dryRun ? " + ' --dry-run'" : "";

        // 자식 프로세스 인자는 Arguments 문자열로 만든다(Windows PowerShell 5.1의 .NET Framework에는 ArgumentList가 없다)
        return $@"$ErrorActionPreference = 'Stop'
$exe = {Q(exePath)}
$expected = {Q(expectedSha256)}
$plan = {Q(planPath)}
$result = {Q(resultPath)}
$root = {root}
$dir = [IO.Path]::Combine($root, {Q(folderName)})
$code = 1
$fail = 1
$created = $false
try {{
    $fail = 10
    if ([IO.Directory]::Exists($dir) -or [IO.File]::Exists($dir)) {{ throw 'Folder already exists.' }}
    [void][IO.Directory]::CreateDirectory($dir)
    $created = $true
    $fail = 11
    $copy = [IO.Path]::Combine($dir, [IO.Path]::GetFileName($exe))
    [IO.File]::Copy($exe, $copy)
    $fail = 12
    $sha = [Security.Cryptography.SHA256]::Create()
    $fs = [IO.File]::OpenRead($copy)
    try {{ $actual = [BitConverter]::ToString($sha.ComputeHash($fs)).Replace('-', '') }} finally {{ $fs.Dispose(); $sha.Dispose() }}
    if ($actual -ne $expected) {{ throw 'Hash mismatch.' }}
    $extract = [IO.Path]::Combine($dir, 'extract')
    [void][IO.Directory]::CreateDirectory($extract)
    $fail = 1
    $psi = [Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $copy
    $psi.Arguments = '--reset-plan ""' + $plan + '"" --reset-result ""' + $result + '""'{dryPart}
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.WorkingDirectory = $dir
    foreach ($k in @($psi.EnvironmentVariables.Keys)) {{
        foreach ($p in @('DOTNET_', 'CORECLR_', 'COMPlus_', 'COR_', 'COREHOST_')) {{
            if ($k.StartsWith($p, [StringComparison]::OrdinalIgnoreCase)) {{ $psi.EnvironmentVariables.Remove($k); break }}
        }}
    }}
    $psi.EnvironmentVariables['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = $extract
    $proc = [Diagnostics.Process]::Start($psi)
    $proc.WaitForExit()
    $code = $proc.ExitCode
}} catch {{
    $code = $fail
}} finally {{
    if ($created) {{
        for ($i = 0; $i -lt 5; $i++) {{
            try {{ [IO.Directory]::Delete($dir, $true); break }} catch {{ [Threading.Thread]::Sleep(500) }}
        }}
    }}
}}
exit $code
";
    }

    /// <summary>-EncodedCommand 인자: UTF-16LE 바이트의 Base64.</summary>
    public static string Encode(string script) => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

    // PowerShell 작은따옴표 문자열: ' 와 그 모양의 유니코드 따옴표(‘ ’ ‚ ‛)는 두 번 쓴다
    internal static string Q(string value)
    {
        if (value.Contains('\0')) throw new ArgumentException("Value contains a NUL character.");
        var sb = new StringBuilder("'");
        foreach (var ch in value)
        {
            sb.Append(ch);
            if (ch is '\'' or '‘' or '’' or '‚' or '‛') sb.Append(ch);
        }
        return sb.Append('\'').ToString();
    }
}
