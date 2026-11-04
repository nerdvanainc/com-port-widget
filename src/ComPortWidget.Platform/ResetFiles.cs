using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ComPortWidget.Core.Reset;

namespace ComPortWidget.Platform;

/// <summary>결과 파일의 내용. 정리 결과에 시험 실행 여부와 수행할 동작 목록을 붙인다.</summary>
public sealed class ResetResultFile
{
    public bool DryRun { get; set; }
    public bool Aborted { get; set; }
    public string? AbortReason { get; set; }
    public string? AbortError { get; set; }
    public List<ResetItemResult> Items { get; set; } = new();
    /// <summary>시험 실행에서 실제로 하지 않고 기록만 한 동작.</summary>
    public List<string> Actions { get; set; } = new();

    public ResetResult ToResult() => new(Aborted, AbortReason, Items, AbortError);
}

/// <summary>계획 파일과 결과 파일의 읽기/쓰기. 계획 파일은 일반 권한으로 쓸 수 있으므로 형식을 엄격히 검사한다.</summary>
public static class ResetFiles
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public static void WritePlan(string path, ResetPlan plan)
        => WriteAtomic(path, JsonSerializer.SerializeToUtf8Bytes(
            new { plan.InstanceIds, plan.ReservedOnlyPorts }, Options));

    /// <summary>계획을 읽는다. 파일이 없거나 형식이 다르거나 필드가 빠졌으면 예외를 던진다.</summary>
    public static ResetPlan ReadPlan(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Plan is not an object.");
        if (!root.TryGetProperty("InstanceIds", out var ids) || ids.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("InstanceIds is missing or not an array.");
        if (!root.TryGetProperty("ReservedOnlyPorts", out var ports) || ports.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("ReservedOnlyPorts is missing or not an array.");

        var idList = new List<string>();
        foreach (var e in ids.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(e.GetString()))
                throw new InvalidDataException("InstanceIds has an invalid entry.");
            idList.Add(e.GetString()!);
        }
        var portList = new List<int>();
        foreach (var e in ports.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Number || !e.TryGetInt32(out var n) || n <= 0)
                throw new InvalidDataException("ReservedOnlyPorts has an invalid entry.");
            portList.Add(n);
        }
        return new ResetPlan(idList, portList);
    }

    public static void WriteResult(string path, ResetResult result, bool dryRun, IReadOnlyList<string> actions)
    {
        var file = new ResetResultFile
        {
            DryRun = dryRun,
            Aborted = result.Aborted,
            AbortReason = result.AbortReason,
            AbortError = result.AbortError,
            Items = result.Items.ToList(),
            Actions = actions.ToList(),
        };
        WriteAtomic(path, JsonSerializer.SerializeToUtf8Bytes(file, Options));
    }

    /// <summary>결과 파일을 읽는다. 없거나 읽을 수 없거나 형식이 틀리면 null.</summary>
    public static ResetResultFile? ReadResult(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var file = JsonSerializer.Deserialize<ResetResultFile>(File.ReadAllBytes(path), Options);
            if (file is null || file.Items is null || file.Actions is null) return null;
            if (file.Items.Any(i => i is null || string.IsNullOrEmpty(i.Target))) return null;
            return file;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    // 같은 폴더의 임시 파일에 쓴 뒤 교체해 반쯤 쓰인 파일이 남지 않게 한다
    private static void WriteAtomic(string path, byte[] bytes)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); } catch { }
            throw;
        }
    }
}
