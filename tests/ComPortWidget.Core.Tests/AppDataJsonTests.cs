using System.Text;
using ComPortWidget.Core.Storage;

namespace ComPortWidget.Core.Tests;

/// <summary>이전 버전(System.Text.Json)이 쓴 data.json 형식을 그대로 읽고 쓰는지 확인한다.</summary>
public class AppDataJsonTests
{
    private static byte[] Fixture()
        => File.ReadAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", "data-v1-previous.json"));

    private static AppData Parse(string json) => AppDataJson.FromBytes(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void Previous_format_loads_with_korean_escapes_dates_and_unknown_fields()
    {
        var d = AppDataJson.FromBytes(Fixture());
        Assert.Equal("시험용 보드 A", d.Memos["10C4:EA60:TEST0001"]);
        Assert.Equal("escaped 시험 \"quote\" \\ back", d.Memos["10C4:EA60:TEST0002"]);
        var a = d.Observed["10C4:EA60:TEST0001"];
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(9)).AddTicks(1234567), a.Arrival);
        Assert.Equal(TimeSpan.FromHours(9), a.Arrival!.Value.Offset);
        Assert.Null(a.Removal);
        Assert.Equal(new DateTimeOffset(2026, 3, 5, 5, 6, 7, TimeSpan.Zero), d.Observed["10C4:EA60:TEST0002"].Removal);
        Assert.Equal(7, d.Settings.HighlightMinutes);
        Assert.True(d.Settings.AlwaysOnTop);
        Assert.Equal("ko", d.Settings.Language);
        Assert.Equal(12.5, d.Settings.Left);
        Assert.Null(d.Settings.Top);
        Assert.Equal(320.0, d.Settings.Width);
        Assert.Equal(480.25, d.Settings.Height);
        Assert.True(d.Settings.DisconnectedExpanded);
    }

    [Fact]
    public void Written_file_keeps_property_names_korean_text_and_iso_dates()
    {
        var d = AppDataJson.FromBytes(Fixture());
        var text = new UTF8Encoding(false, true).GetString(AppDataJson.ToBytes(d));
        Assert.Contains("\"HighlightMinutes\": 7", text);
        Assert.Contains("시험용 보드 A", text);
        Assert.DoesNotContain("\\uC2DC", text);
        Assert.Contains("\"Arrival\": \"2026-01-02T03:04:05.1234567+09:00\"", text);
        Assert.Contains("\"Top\": null", text);
        Assert.Contains("\n  \"Memos\": {\n    \"", text); // 2칸 들여쓰기
        var back = Parse(text);
        Assert.Equal(d.Memos, back.Memos);
        Assert.Equal(d.Observed, back.Observed);
        Assert.Equal(d.Settings.Height, back.Settings.Height);
    }

    [Fact]
    public void Missing_and_null_sections_use_defaults()
    {
        var d = Parse("{\"Memos\": null, \"Settings\": null}");
        Assert.Empty(d.Memos);
        Assert.Empty(d.Observed);
        Assert.Equal(5, d.Settings.HighlightMinutes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{ not json")]
    [InlineData("{\"Version\": \"x\"}")]
    [InlineData("{\"Memos\": {\"a\": 5}}")]
    [InlineData("{\"Observed\": {\"a\": {\"Arrival\": \"not a date\"}}}")]
    [InlineData("{\"Settings\": {\"HighlightMinutes\": 1.5}}")]
    [InlineData("{} trailing")]
    [InlineData("{\"Version\": 01}")]
    [InlineData("{\"Version\": -007}")]
    [InlineData("{\"Version\": 00}")]
    [InlineData("{\"Observed\": {\"a\": {\"Arrival\": \"01/02/2026 03:04\"}}}")]
    [InlineData("{\"Observed\": {\"a\": {\"Arrival\": \"2026-01-02\"}}}")]
    [InlineData("{\"Observed\": {\"a\": {\"Arrival\": \"2026-01-02T03:04:05\"}}}")]
    public void Malformed_or_mistyped_documents_are_rejected(string json)
        => Assert.Throws<JsonFormatException>(() => Parse(json));

    [Theory]
    [InlineData("{\"Version\": 0}", 0)]
    [InlineData("{\"Version\": -1}", -1)]
    [InlineData("{\"Version\": 10}", 10)]
    public void Plain_integers_are_accepted(string json, int expected)
        => Assert.Equal(expected, Parse(json).Version);

    [Theory]
    [InlineData("2026-01-02T03:04:05Z")]
    [InlineData("2026-01-02T03:04:05+09:00")]
    [InlineData("2026-01-02T03:04:05.1+09:00")]
    [InlineData("2026-01-02T03:04:05.1234567-05:30")]
    public void Iso_dates_are_accepted(string date)
        => Assert.NotNull(Parse("{\"Observed\": {\"a\": {\"Arrival\": \"" + date + "\"}}}").Observed["a"].Arrival);

    [Fact]
    public void Invalid_utf8_is_rejected()
        => Assert.Throws<JsonFormatException>(() => AppDataJson.FromBytes(new byte[] { 0x7B, 0xFF, 0x7D }));

    [Fact]
    public void Utf8_bom_is_accepted()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("{}")).ToArray();
        Assert.NotNull(AppDataJson.FromBytes(bytes));
    }

    [Fact]
    public void Control_characters_and_quotes_survive_a_round_trip()
    {
        var d = new AppData();
        d.Memos["k"] = "a\"b\\c\td\u0001e";
        var back = AppDataJson.FromBytes(AppDataJson.ToBytes(d));
        Assert.Equal(d.Memos["k"], back.Memos["k"]);
    }
}
