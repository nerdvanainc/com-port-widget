using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ComPortWidget.Platform;

namespace ComPortWidget.Platform.Tests;

public class ElevationScriptTests : IDisposable
{
    private const string Guid1 = "0f8fad5b-d9cb-469f-a165-70867728950e";
    private static readonly string Hash = new('A', 64);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cpw-es-" + System.Guid.NewGuid().ToString("N"));

    public ElevationScriptTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string Folder => Path.Combine(_dir, "root", "ComPortWidget-Cleanup-" + Guid1.Replace("-", ""));

    // ---- 순수 함수 시험 ----

    [Fact]
    public void Script_has_no_commands_and_no_parse_errors_in_windows_powershell_51()
    {
        foreach (var dry in new[] { false, true })
        {
            var script = ElevationScript.Build(@"C:\x\it's\ComPortWidget.exe", Hash, @"C:\p\reset-plan.json", @"C:\p\reset-result.json", Guid1, dry);
            var file = Path.Combine(_dir, "script.ps1");
            File.WriteAllText(file, script);
            // Windows PowerShell 5.1의 파서로 구문 오류와 CommandAst(cmdlet, 함수, 외부 명령 호출) 수를 센다
            const string probe = "$e=$null;$t=$null;$ast=[System.Management.Automation.Language.Parser]::ParseInput([IO.File]::ReadAllText($env:CPW_SCRIPT),[ref]$t,[ref]$e);"
                + "$c=@($ast.FindAll({param($n) $n -is [System.Management.Automation.Language.CommandAst]},$true));"
                + "'errors=' + @($e).Count + ' commands=' + $c.Count + ' ' + (($c | ForEach-Object { $_.Extent.Text }) -join '|')";
            var r = RunPs(probe, psi => psi.Environment["CPW_SCRIPT"] = file);
            Assert.Equal("errors=0 commands=0", r.Output.Trim().TrimEnd());
        }
    }

    private Run RunPs(string command, Action<ProcessStartInfo>? tweak = null)
    {
        var psi = new ProcessStartInfo(ResetLauncher.PowerShellPath)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
        };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", ElevationScript.Encode(command) })
            psi.ArgumentList.Add(a);
        tweak?.Invoke(psi);
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEndAsync();
        var e = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return new Run(p.ExitCode, o.Result + e.Result);
    }

    [Fact]
    public void Paths_are_quoted_for_single_quoted_strings_including_apostrophes()
    {
        var s = ElevationScript.Build(@"C:\it's\a.exe", Hash, @"C:\o'neil\reset-plan.json", @"C:\o'neil\reset-result.json", Guid1);
        Assert.Contains(@"$exe = 'C:\it''s\a.exe'", s);
        Assert.Contains(@"$plan = 'C:\o''neil\reset-plan.json'", s);
        Assert.Equal("'a‘‘b’’c'", ElevationScript.Q("a‘b’c")); // 유니코드 따옴표도 같은 방식으로 두 번 쓴다
        Assert.Equal("'x''; calc; ''y'", ElevationScript.Q("x'; calc; 'y"));
    }

    [Fact]
    public void Build_rejects_a_bad_guid_or_hash()
    {
        Assert.Throws<ArgumentException>(() => ElevationScript.Build("a", Hash, "p", "r", "x'; calc"));
        Assert.Throws<ArgumentException>(() => ElevationScript.Build("a", "zz", "p", "r", Guid1));
    }

    [Fact]
    public void Encode_is_base64_of_utf16le()
    {
        var text = "$a = '한글'";
        var encoded = ElevationScript.Encode(text);
        Assert.Equal(text, Encoding.Unicode.GetString(Convert.FromBase64String(encoded)));
        Assert.Equal(Convert.ToBase64String(Encoding.Unicode.GetBytes(text)), encoded);
    }

    // ---- powershell.exe로 실제 실행(승격 없이, rootOverride 사용) ----

    private sealed record Run(int Exit, string Output);

    // exe 대신 배치 파일을 쓴다: 인자와 환경을 CPW_OUT 폴더에 남기고 CPW_EXIT로 종료 코드를 정한다
    private string MakeStandIn()
    {
        var bat = Path.Combine(_dir, "ComPortWidget.cmd");
        File.WriteAllText(bat, "@echo off\r\necho %*> \"%CPW_OUT%\\args.txt\"\r\nset > \"%CPW_OUT%\\env.txt\"\r\ncd > \"%CPW_OUT%\\cwd.txt\"\r\nexit /b %CPW_EXIT%\r\n");
        return bat;
    }

    private static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private Run RunScript(string script, int childExit = 0, Action<ProcessStartInfo>? tweak = null)
    {
        var psi = new ProcessStartInfo(ResetLauncher.PowerShellPath)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
        };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", ElevationScript.Encode(script) })
            psi.ArgumentList.Add(a);
        Directory.CreateDirectory(Path.Combine(_dir, "out"));
        psi.Environment["CPW_OUT"] = Path.Combine(_dir, "out");
        psi.Environment["CPW_EXIT"] = childExit.ToString();
        psi.Environment["DOTNET_ROOT_TEST"] = "x";
        psi.Environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = @"C:\evil";
        psi.Environment["CORECLR_ENABLE_PROFILING"] = "1";
        psi.Environment["COMPlus_Foo"] = "1";
        psi.Environment["COR_PROFILER"] = "{x}";
        psi.Environment["COREHOST_TRACE"] = "1";
        psi.Environment["CPW_KEEP"] = "kept";
        tweak?.Invoke(psi);
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEndAsync();
        var e = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return new Run(p.ExitCode, o.Result + e.Result);
    }

    private string Script(string exe, string hash, bool dry = true)
        => ElevationScript.Build(exe, hash, Path.Combine(_dir, "reset-plan.json"), Path.Combine(_dir, "reset-result.json"),
            Guid1, dry, Path.Combine(_dir, "root"));

    [Fact]
    public void Script_runs_the_copy_with_filtered_environment_and_propagates_exit_code()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "root"));
        var bat = MakeStandIn();
        var r = RunScript(Script(bat, Sha(bat)), childExit: 7);
        Assert.True(r.Exit == 7, "exit=" + r.Exit + " " + r.Output);

        var args = File.ReadAllText(Path.Combine(_dir, "out", "args.txt")).Trim();
        Assert.Equal($"--reset-plan \"{Path.Combine(_dir, "reset-plan.json")}\" --reset-result \"{Path.Combine(_dir, "reset-result.json")}\" --dry-run", args);

        var env = File.ReadAllLines(Path.Combine(_dir, "out", "env.txt"));
        Assert.DoesNotContain(env, l => l.StartsWith("DOTNET_ROOT_TEST=", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(env, l => l.StartsWith("CORECLR_", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(env, l => l.StartsWith("COMPlus_", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(env, l => l.StartsWith("COR_", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(env, l => l.StartsWith("COREHOST_", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("CPW_KEEP=kept", env);
        Assert.Contains(env, l => l.StartsWith("DOTNET_BUNDLE_EXTRACT_BASE_DIR=" + Folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(env, l => l.Contains(@"C:\evil"));

        // 복사본이 새 폴더에서 실행됐다
        Assert.Equal(Folder, File.ReadAllText(Path.Combine(_dir, "out", "cwd.txt")).Trim(), ignoreCase: true);
        Assert.False(Directory.Exists(Folder), "cleanup folder was not removed");
    }

    [Fact]
    public void Script_without_dry_run_passes_exactly_two_options()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "root"));
        var bat = MakeStandIn();
        var r = RunScript(Script(bat, Sha(bat), dry: false));
        Assert.True(r.Exit == 0, r.Output);
        Assert.DoesNotContain("--dry-run", File.ReadAllText(Path.Combine(_dir, "out", "args.txt")));
    }

    [Fact]
    public void Script_refuses_a_hash_mismatch_and_removes_the_folder()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "root"));
        var bat = MakeStandIn();
        var r = RunScript(Script(bat, new string('0', 64)));
        Assert.Equal(ElevationScript.ExitHashMismatch, r.Exit);
        Assert.False(File.Exists(Path.Combine(_dir, "out", "args.txt")), "the copy must not run");
        Assert.False(Directory.Exists(Folder));
    }

    [Fact]
    public void Script_fails_and_leaves_an_existing_folder_alone()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, "keep.txt"), "x");
        var bat = MakeStandIn();
        var r = RunScript(Script(bat, Sha(bat)));
        Assert.Equal(ElevationScript.ExitFolderFailed, r.Exit);
        Assert.True(File.Exists(Path.Combine(Folder, "keep.txt")));
        Assert.False(File.Exists(Path.Combine(_dir, "out", "args.txt")));
    }

    [Fact]
    public void Script_exits_11_when_the_exe_cannot_be_copied()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "root"));
        var r = RunScript(Script(Path.Combine(_dir, "missing.exe"), new string('A', 64)));
        Assert.Equal(ElevationScript.ExitCopyFailed, r.Exit);
        Assert.False(Directory.Exists(Folder));
    }

    [Fact]
    public void Script_exits_10_when_the_root_does_not_exist_for_the_folder()
    {
        // root 폴더가 없어도 CreateDirectory는 만들어 버리므로, 루트 자리에 같은 이름의 파일을 둬서 만들 수 없게 한다
        File.WriteAllText(Path.Combine(_dir, "root"), "x");
        var bat = MakeStandIn();
        Assert.Equal(ElevationScript.ExitFolderFailed, RunScript(Script(bat, Sha(bat))).Exit);
    }

    [Fact]
    public void A_module_earlier_on_PSModulePath_cannot_run_code_in_the_script()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "root"));
        var mods = Path.Combine(_dir, "mods");
        var marker = Path.Combine(_dir, "marker.txt");
        // 코어 모듈 이름과 같은 가짜 모듈이 cmdlet을 가로채도록 만든다
        foreach (var name in new[] { "Microsoft.PowerShell.Utility", "Microsoft.PowerShell.Management", "Microsoft.PowerShell.Core" })
        {
            var d = Path.Combine(mods, name);
            Directory.CreateDirectory(d);
            File.WriteAllText(Path.Combine(d, name + ".psm1"),
                "foreach ($n in 'New-Object','Start-Sleep','Remove-Item','Copy-Item','Get-FileHash','Join-Path','Test-Path','ForEach-Object','Where-Object')"
                + " { Set-Item -Path ('function:global:' + $n) -Value { [IO.File]::AppendAllText($env:CPW_MARKER, 'ran') } }\r\n");
        }
        // 통제 실험: 이 환경에서 가짜 모듈이 실제로 가로채는지 먼저 확인한다
        var control = RunPs("Import-Module Microsoft.PowerShell.Utility -Force; $null = New-Object Object", psi =>
        {
            psi.Environment["PSModulePath"] = mods + ";" + Environment.GetEnvironmentVariable("PSModulePath");
            psi.Environment["CPW_MARKER"] = marker;
        });
        Assert.True(File.Exists(marker), "control run did not hit the fake module: " + control.Output);
        File.Delete(marker);

        var bat = MakeStandIn();
        var r = RunScript(Script(bat, Sha(bat)), 0, psi =>
        {
            psi.Environment["PSModulePath"] = mods + ";" + Environment.GetEnvironmentVariable("PSModulePath");
            psi.Environment["CPW_MARKER"] = marker;
        });
        Assert.True(r.Exit == 0, r.Output);
        Assert.False(File.Exists(marker), "module code ran inside the elevation script");
    }
}
