using System.Globalization;
using ComPortWidget.Core.Text;

namespace ComPortWidget.Core.Tests;

public class LanguageTests
{
    [Fact]
    public void ResolveLanguage_uses_korean_for_ko_ui()
    {
        Assert.Equal("ko", Strings.ResolveLanguage("auto", new CultureInfo("ko-KR")));
        Assert.Equal("ko", Strings.ResolveLanguage("", new CultureInfo("ko")));
    }

    [Fact]
    public void ResolveLanguage_uses_english_for_other_ui()
    {
        Assert.Equal("en", Strings.ResolveLanguage("auto", new CultureInfo("ja-JP")));
        Assert.Equal("en", Strings.ResolveLanguage("auto", new CultureInfo("en-US")));
        Assert.Equal("en", Strings.ResolveLanguage("알 수 없음", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ResolveLanguage_respects_explicit_setting()
    {
        Assert.Equal("en", Strings.ResolveLanguage("en", new CultureInfo("ko-KR")));
        Assert.Equal("ko", Strings.ResolveLanguage("ko", new CultureInfo("en-US")));
    }
}
