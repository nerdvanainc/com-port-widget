using ComPortWidget.Core.Reset;
using ComPortWidget.Core.Storage;

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
    public static void WritePlan(string path, ResetPlan plan)
        => WriteAtomic(path, MiniJson.ToUtf8(new Dictionary<string, object?>
        {
            ["InstanceIds"] = plan.InstanceIds.ToList(),
            ["ReservedOnlyPorts"] = plan.ReservedOnlyPorts.ToList(),
        }));

    /// <summary>계획을 읽는다. 파일이 없거나 형식이 다르거나 필드가 빠졌으면 예외를 던진다.</summary>
    public static ResetPlan ReadPlan(string path)
    {
        object? parsed;
        try { parsed = MiniJson.Parse(File.ReadAllBytes(path)); }
        catch (JsonFormatException e) { throw new InvalidDataException(e.Message, e); }
        if (parsed is not Dictionary<string, object?> root) throw new InvalidDataException("Plan is not an object.");
        if (!root.TryGetValue("InstanceIds", out var ids) || ids is not List<object?> idItems)
            throw new InvalidDataException("InstanceIds is missing or not an array.");
        if (!root.TryGetValue("ReservedOnlyPorts", out var ports) || ports is not List<object?> portItems)
            throw new InvalidDataException("ReservedOnlyPorts is missing or not an array.");

        var idList = new List<string>();
        foreach (var e in idItems)
        {
            if (e is not string str || string.IsNullOrWhiteSpace(str))
                throw new InvalidDataException("InstanceIds has an invalid entry.");
            idList.Add(str);
        }
        var portList = new List<int>();
        foreach (var e in portItems)
        {
            if (e is not JsonNumber num || !num.TryGetInt32(out var n) || n <= 0)
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
        WriteAtomic(path, MiniJson.ToUtf8(new Dictionary<string, object?>
        {
            ["DryRun"] = file.DryRun,
            ["Aborted"] = file.Aborted,
            ["AbortReason"] = file.AbortReason,
            ["AbortError"] = file.AbortError,
            ["Items"] = file.Items.Select(i => (object?)new Dictionary<string, object?>
            {
                ["Target"] = i.Target,
                ["Status"] = i.Status.ToString(),
                ["Detail"] = i.Detail,
                ["Error"] = i.Error,
            }).ToList(),
            ["Actions"] = file.Actions,
        }));
    }

    /// <summary>결과 파일을 읽는다. 없거나 읽을 수 없거나 형식이 틀리면 null.</summary>
    public static ResetResultFile? ReadResult(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var root = MiniJson.AsObject(MiniJson.Parse(File.ReadAllBytes(path)));
            var file = new ResetResultFile
            {
                DryRun = MiniJson.OptBool(root, "DryRun", false),
                Aborted = MiniJson.OptBool(root, "Aborted", false),
                AbortReason = MiniJson.OptString(root, "AbortReason"),
                AbortError = MiniJson.OptString(root, "AbortError"),
            };
            if (root.TryGetValue("Items", out var items) && items is not null)
                foreach (var it in MiniJson.AsArray(items))
                {
                    if (it is null) return null;
                    var o = MiniJson.AsObject(it);
                    var target = MiniJson.OptString(o, "Target");
                    var status = MiniJson.OptString(o, "Status");
                    if (string.IsNullOrEmpty(target) || status is null
                        || !Enum.TryParse<ResetItemStatus>(status, true, out var st) || !Enum.IsDefined(typeof(ResetItemStatus), st)) return null;
                    file.Items.Add(new ResetItemResult(target!, st, MiniJson.OptString(o, "Detail"), MiniJson.OptString(o, "Error")));
                }
            if (root.TryGetValue("Actions", out var actions) && actions is not null)
                foreach (var a in MiniJson.AsArray(actions))
                    file.Actions.Add(a as string ?? throw new JsonFormatException("Action is not a string."));
            return file;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonFormatException)
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
            FileUtil.ReplaceAtomic(temp, path);
        }
        catch
        {
            try { File.Delete(temp); } catch { }
            throw;
        }
    }
}
