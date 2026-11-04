namespace ComPortWidget.Core.Text;

/// <summary>"Ctrl+Alt+Shift+C" 같은 문자열을 RegisterHotKey 인자로 바꾼다.</summary>
public static class HotkeyParser
{
    public const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModWin = 0x8;

    /// <summary>한정자 하나 이상과 키 하나(영문자, 숫자, F1~F24)가 있어야 한다. 실패하면 두 값은 0이다.</summary>
    public static bool TryParse(string text, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        uint mods = 0, vk = 0;
        foreach (var raw in text.Split('+'))
        {
            var part = raw.Trim();
            if (part.Length == 0) return false;
            var mod = part.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => ModControl,
                "ALT" => ModAlt,
                "SHIFT" => ModShift,
                "WIN" => ModWin,
                _ => 0u,
            };
            if (mod != 0)
            {
                if ((mods & mod) != 0) return false; // 같은 한정자 중복
                mods |= mod;
                continue;
            }
            if (vk != 0) return false; // 키는 하나만
            vk = ParseKey(part);
            if (vk == 0) return false;
        }
        if (mods == 0 || vk == 0) return false;
        modifiers = mods;
        virtualKey = vk;
        return true;
    }

    private static uint ParseKey(string part)
    {
        if (part.Length == 1)
        {
            var c = char.ToUpperInvariant(part[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') return c;
            return 0;
        }
        if ((part[0] is 'F' or 'f') && int.TryParse(part.AsSpan(1), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 24)
            return 0x70u + (uint)(n - 1);
        return 0;
    }
}
