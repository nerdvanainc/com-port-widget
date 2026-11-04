using ComPortWidget.Platform;

namespace ComPortWidget.Platform.Tests;

public class SystemDllsTests
{
    [Theory]
    [InlineData("cfgmgr32.dll")]
    [InlineData("setupapi.dll")]
    [InlineData("msports.dll")]
    [InlineData("advapi32.dll")]
    [InlineData("user32.dll")]
    [InlineData("kernel32")]
    [InlineData("SETUPAPI.DLL")]
    public void Allowed_libraries_resolve_to_absolute_system_directory_paths(string name)
    {
        var path = SystemDlls.PathFor(name);
        Assert.True(Path.IsPathFullyQualified(path));
        Assert.Equal(Environment.SystemDirectory, Path.GetDirectoryName(path), ignoreCase: true);
        Assert.True(File.Exists(path), path);
    }

    [Theory]
    [InlineData("evil.dll")]
    [InlineData("..\\cfgmgr32.dll")]
    [InlineData("C:\\temp\\cfgmgr32.dll")]
    public void Unknown_or_path_like_names_are_refused(string name)
        => Assert.Throws<DllNotFoundException>(() => SystemDlls.PathFor(name));
}
