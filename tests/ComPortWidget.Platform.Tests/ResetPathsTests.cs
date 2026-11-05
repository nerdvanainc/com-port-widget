using System.Diagnostics;
using ComPortWidget.Platform;

namespace ComPortWidget.Platform.Tests;

public class ResetPathsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cpw-test-" + Guid.NewGuid().ToString("N"));
    public ResetPathsTests() => Directory.CreateDirectory(_dir);
    public void Dispose()
    {
        // 접합 지점은 대상 폴더를 지우지 않도록 먼저 링크만 제거한다
        try
        {
            foreach (var d in Directory.GetDirectories(_dir))
                if (new DirectoryInfo(d).Attributes.HasFlag(FileAttributes.ReparsePoint)) Directory.Delete(d);
        }
        catch { }
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string P(string n) => Path.Combine(_dir, n);

    /// <summary>mklink /J로 접합 지점을 만든다(관리자 권한이 필요 없다).</summary>
    internal static bool TryMakeJunction(string link, string target)
    {
        var psi = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        psi.Arguments = $"/c mklink /J \"{link}\" \"{target}\"";
        using var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode == 0 && Directory.Exists(link);
    }

    [Fact]
    public void Accepts_the_two_fixed_names_in_one_existing_folder()
    {
        Assert.True(ResetPaths.TryValidate(P("reset-plan.json"), P("reset-result.json"), out var dir, out _));
        Assert.Equal(_dir, dir);
    }

    [Theory]
    [InlineData("plan.json", "reset-result.json")]
    [InlineData("reset-plan.json", "result.json")]
    public void Rejects_other_file_names(string plan, string result)
        => Assert.False(ResetPaths.TryValidate(P(plan), P(result), out _, out _));

    [Fact]
    public void Rejects_different_folders_relative_paths_and_dot_segments()
    {
        Directory.CreateDirectory(P("sub"));
        Assert.False(ResetPaths.TryValidate(P("reset-plan.json"), Path.Combine(P("sub"), "reset-result.json"), out _, out _));
        Assert.False(ResetPaths.TryValidate("reset-plan.json", "reset-result.json", out _, out _));
        Assert.False(ResetPaths.TryValidate(Path.Combine(P("sub"), "..", "reset-plan.json"), Path.Combine(P("sub"), "..", "reset-result.json"), out _, out _));
        Assert.False(ResetPaths.TryValidate(null, null, out _, out _));
    }

    [Fact]
    public void Rejects_missing_folder()
        => Assert.False(ResetPaths.TryValidate(P("none\\reset-plan.json"), P("none\\reset-result.json"), out _, out _));

    [Fact]
    public void Rejects_a_reparse_point_in_the_folder_path()
    {
        Directory.CreateDirectory(P("real"));
        Directory.CreateDirectory(Path.Combine(P("real"), "child"));
        var link = P("link");
        Assert.True(TryMakeJunction(link, P("real")));
        Assert.False(ResetPaths.TryValidate(Path.Combine(link, "reset-plan.json"), Path.Combine(link, "reset-result.json"), out _, out _));
        // 부모가 접합 지점인 하위 폴더도 거부한다
        var deep = Path.Combine(link, "child");
        Assert.False(ResetPaths.TryValidate(Path.Combine(deep, "reset-plan.json"), Path.Combine(deep, "reset-result.json"), out _, out _));
    }

    public static IEnumerable<object[]> BadPaths()
    {
        var bs = "\\";
        yield return new object[] { "C:" + bs + "a|b" + bs + "reset-plan.json" };
        yield return new object[] { "C:" + bs + "a\"b" + bs + "reset-plan.json" };
        yield return new object[] { "C:" + bs + "a<b" + bs + "reset-plan.json" };
        yield return new object[] { "C:" + bs + "a*b" + bs + "reset-plan.json" };
        yield return new object[] { "C:" + bs + "a:b" + bs + "reset-plan.json" };
        yield return new object[] { "C:" + bs + "dir" + bs + "reset-plan.json:stream" };
        yield return new object[] { "C:" + bs + "dir" + bs + "reset-plan.json::$DATA" };
        yield return new object[] { "C:" + bs + "a" + new string('x', 400) + bs + "reset-plan.json" };
        yield return new object[] { "C:" + bs + "a" + (char)0 + "b" + bs + "reset-plan.json" };
        yield return new object[] { "C:" + bs + "a" + (char)1 + "b" + bs + "reset-plan.json" };
        yield return new object[] { bs + bs + "?" + bs + "C:" + bs + "reset-plan.json" };
    }

    [Theory]
    [MemberData(nameof(BadPaths))]
    public void Invalid_path_forms_fail_without_throwing(string plan)
    {
        Assert.False(ResetPaths.TryValidate(plan, plan.Replace("reset-plan.json", "reset-result.json"), out var folder, out var error));
        Assert.Equal("", folder);
        Assert.False(string.IsNullOrEmpty(error));
    }
}
