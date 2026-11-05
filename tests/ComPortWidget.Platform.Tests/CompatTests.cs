using System.Runtime.InteropServices;
using ComPortWidget.Platform;

namespace ComPortWidget.Platform.Tests;

public class CompatTests
{
    // 기준: .NET의 Path.IsPathFullyQualified(Windows) 결과
    [Theory]
    [InlineData(@"C:\a", true)]
    [InlineData(@"C:/a", true)]
    [InlineData(@"c:\", true)]
    [InlineData(@"\\server\share", true)]
    [InlineData(@"\\?\C:\a", true)]
    [InlineData(@"\\.\COM1", true)]
    [InlineData(@"C:", false)]
    [InlineData(@"C:a", false)]
    [InlineData(@"\a", false)]
    [InlineData(@"/a", false)]
    [InlineData(@"a\b", false)]
    [InlineData(@"..\a", false)]
    [InlineData(@"1:\a", false)]
    [InlineData("", false)]
    [InlineData("C", false)]
    public void IsPathFullyQualified_matches_dotnet_rules(string path, bool expected)
        => Assert.Equal(expected, Compat.IsPathFullyQualified(path));

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "\"\"")]
    [InlineData("a b", "\"a b\"")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    [InlineData(@"C:\dir with space\", "\"C:\\dir with space\\\\\"")]
    [InlineData(@"C:\dir\", @"C:\dir\")]
    [InlineData("/remove-device", "/remove-device")]
    public void QuoteArgument_follows_command_line_rules(string arg, string expected)
        => Assert.Equal(expected, Compat.QuoteArgument(arg));

    [Fact]
    public void Quoted_arguments_round_trip_through_the_real_command_line_parser()
    {
        var args = new[] { "--reset-plan", @"C:\Users\a b\AppData\Roaming\x\reset-plan.json", "q\"uote", @"trail\", "", @"USB\VID_0000&PID_0000\FAKE 1" };
        var parsed = CommandLineToArgv("prog.exe " + Compat.JoinArguments(args));
        Assert.Equal(new[] { "prog.exe" }.Concat(args), parsed);
    }

    [Fact]
    public void ProcessPath_is_known_and_tick_count_is_positive()
    {
        Assert.False(string.IsNullOrEmpty(Compat.ProcessPath));
        Assert.True(Compat.TickCount64 > 0);
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string cmd, out int argc);

    private static string[] CommandLineToArgv(string line)
    {
        var p = CommandLineToArgvW(line, out var n);
        var r = new string[n];
        for (var i = 0; i < n; i++)
            r[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(p, i * IntPtr.Size))!;
        return r;
    }
}
