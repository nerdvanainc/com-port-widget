using ComPortWidget.Platform;

namespace ComPortWidget.Platform.Tests;

public class FrameworkVersionTests
{
    [Theory]
    [InlineData(528040, true)]
    [InlineData(528049, true)]
    [InlineData(533320, true)]
    [InlineData(461814, false)]
    [InlineData(394802, false)]
    [InlineData(0, false)]
    public void Release_numbers_are_compared_with_the_48_minimum(int release, bool expected)
        => Assert.Equal(expected, FrameworkVersion.IsSupported(release));

    [Fact]
    public void Unknown_release_is_not_reported_as_supported()
        => Assert.False(FrameworkVersion.IsSupported(null));

    [Fact]
    public void This_machine_reports_a_supported_release()
        => Assert.True(FrameworkVersion.IsSupported(FrameworkVersion.ReadRelease()));
}
