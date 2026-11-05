namespace ComPortWidget.Core.Text;

/// <summary>메모 문자열 정리 규칙.</summary>
public static class MemoText
{
    public const int MaxLength = 500;

    /// <summary>줄바꿈을 공백으로 바꾼다. 붙여넣기에서 첫 줄만 남지 않게 한다.</summary>
    public static string FlattenLineBreaks(string text)
        => text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');

    /// <summary>저장 전 정리: 줄바꿈을 공백으로, 앞뒤 공백 제거, 500자 제한.</summary>
    public static string Normalize(string? memo)
    {
        if (string.IsNullOrEmpty(memo)) return "";
        var text = FlattenLineBreaks(memo!).Trim();
        return text.Length > MaxLength ? text[..MaxLength] : text;
    }
}
