using ComPortWidget.Core.Listing;

namespace ComPortWidget.Core.Storage;

/// <summary>
/// AppData와 JSON 사이의 변환. 이전 버전이 쓴 data.json(속성 이름은 파스칼 표기, 날짜는 ISO 8601)을 그대로 읽고 쓴다.
/// 모르는 필드는 무시하고, 빠졌거나 null인 필드는 기본값으로 채우며, 형식이 틀린 값은 JsonFormatException이다.
/// </summary>
public static class AppDataJson
{
    public static AppData FromBytes(byte[] utf8)
    {
        var root = MiniJson.AsObject(MiniJson.Parse(utf8));
        var data = new AppData { Version = MiniJson.OptInt(root, "Version", 1) };

        if (root.TryGetValue("Memos", out var memos) && memos is not null)
            foreach (var kv in MiniJson.AsObject(memos))
                if (kv.Value is string m) data.Memos[kv.Key] = m;
                else if (kv.Value is not null) throw new JsonFormatException("Memo is not a string.");

        if (root.TryGetValue("Observed", out var observed) && observed is not null)
            foreach (var kv in MiniJson.AsObject(observed))
            {
                if (kv.Value is null) continue;
                var o = MiniJson.AsObject(kv.Value);
                data.Observed[kv.Key] = new ObservedTimes(MiniJson.OptDate(o, "Arrival"), MiniJson.OptDate(o, "Removal"));
            }

        if (root.TryGetValue("Settings", out var settings) && settings is not null)
        {
            var o = MiniJson.AsObject(settings);
            var d = new AppSettings();
            data.Settings = new AppSettings
            {
                HighlightMinutes = MiniJson.OptInt(o, "HighlightMinutes", d.HighlightMinutes),
                AlwaysOnTop = MiniJson.OptBool(o, "AlwaysOnTop", d.AlwaysOnTop),
                Language = MiniJson.OptString(o, "Language") ?? d.Language,
                AutoStartAsked = MiniJson.OptBool(o, "AutoStartAsked", d.AutoStartAsked),
                Hotkey = MiniJson.OptString(o, "Hotkey") ?? d.Hotkey,
                Left = MiniJson.OptDouble(o, "Left"),
                Top = MiniJson.OptDouble(o, "Top"),
                Width = MiniJson.OptDouble(o, "Width"),
                Height = MiniJson.OptDouble(o, "Height"),
                DisconnectedExpanded = MiniJson.OptBool(o, "DisconnectedExpanded", d.DisconnectedExpanded),
            };
        }
        return data;
    }

    public static byte[] ToBytes(AppData data)
    {
        var s = data.Settings;
        var root = new Dictionary<string, object?>
        {
            ["Version"] = data.Version,
            ["Memos"] = data.Memos.ToDictionary(kv => kv.Key, kv => (object?)kv.Value),
            ["Observed"] = data.Observed.ToDictionary(kv => kv.Key, kv => (object?)new Dictionary<string, object?>
            {
                ["Arrival"] = kv.Value.Arrival,
                ["Removal"] = kv.Value.Removal,
            }),
            ["Settings"] = new Dictionary<string, object?>
            {
                ["HighlightMinutes"] = s.HighlightMinutes,
                ["AlwaysOnTop"] = s.AlwaysOnTop,
                ["Language"] = s.Language,
                ["AutoStartAsked"] = s.AutoStartAsked,
                ["Hotkey"] = s.Hotkey,
                ["Left"] = s.Left,
                ["Top"] = s.Top,
                ["Width"] = s.Width,
                ["Height"] = s.Height,
                ["DisconnectedExpanded"] = s.DisconnectedExpanded,
            },
        };
        return MiniJson.ToUtf8(root);
    }
}
