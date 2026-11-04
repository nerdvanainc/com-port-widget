using ComPortWidget.Core.Listing;

namespace ComPortWidget.Core.Storage;

/// <summary>위젯 설정. System.Text.Json은 필드를 무시하므로 속성으로 둔다.</summary>
public sealed class AppSettings
{
    public int HighlightMinutes { get; set; } = 5;
    public bool AlwaysOnTop { get; set; } = false;
    public string Language { get; set; } = "auto";
    public bool AutoStartAsked { get; set; } = false;
    public string Hotkey { get; set; } = "Ctrl+Alt+Shift+C";
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public bool DisconnectedExpanded { get; set; } = false;
}

/// <summary>저장 파일 한 개의 내용. 메모와 인식/해제 시간, 설정을 담는다.</summary>
public sealed class AppData
{
    public int Version { get; set; } = 1;
    public Dictionary<string, string> Memos { get; set; } = new();
    public Dictionary<string, ObservedTimes> Observed { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
}
