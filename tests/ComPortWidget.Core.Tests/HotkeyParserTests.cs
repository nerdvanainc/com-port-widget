using ComPortWidget.Core.Text;

namespace ComPortWidget.Core.Tests;

public class HotkeyParserTests
{
    [Fact]
    public void Parses_default_hotkey()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+Alt+Shift+C", out var mod, out var vk));
        Assert.Equal(0x2u | 0x1u | 0x4u, mod);
        Assert.Equal((uint)'C', vk);
    }

    [Fact]
    public void Is_case_insensitive_and_allows_spaces_and_win()
    {
        Assert.True(HotkeyParser.TryParse(" win + ctrl + f12 ", out var mod, out var vk));
        Assert.Equal(0x8u | 0x2u, mod);
        Assert.Equal(0x70u + 11, vk);
    }

    [Fact]
    public void Parses_digit_and_f24()
    {
        Assert.True(HotkeyParser.TryParse("Alt+7", out _, out var d));
        Assert.Equal((uint)'7', d);
        Assert.True(HotkeyParser.TryParse("Alt+F24", out _, out var f));
        Assert.Equal(0x70u + 23, f);
    }

    [Theory]
    [InlineData("")]
    [InlineData("C")]              // 한정자 없음
    [InlineData("F5")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Alt")]       // 키 없음
    [InlineData("Ctrl+C+D")]       // 키 둘
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+F0")]
    [InlineData("Ctrl+Foo")]
    [InlineData("Ctrl+Ctrl+C")]    // 중복 한정자
    [InlineData("Ctrl+가")]
    public void Rejects_invalid(string text)
    {
        Assert.False(HotkeyParser.TryParse(text, out var mod, out var vk));
        Assert.Equal(0u, mod);
        Assert.Equal(0u, vk);
    }

    [Fact]
    public void Null_is_rejected()
        => Assert.False(HotkeyParser.TryParse(null!, out _, out _));
}
