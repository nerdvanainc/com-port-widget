using System.Text.Encodings.Web;
using System.Text.Json;

namespace ComPortWidget.Core.Storage;

/// <summary>AppData를 JSON 파일로 읽고 쓴다. 쓰기는 임시 파일 후 교체해서 기존 파일을 보호한다.</summary>
public sealed class JsonStore(string path)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // 한글을 \uXXXX로 이스케이프하지 않는다.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>마지막 Load가 손상된 파일을 보존하고 기본값으로 복구했으면 true.</summary>
    public bool LastLoadRecovered { get; private set; }

    /// <summary>
    /// 파일은 있으나 읽지 못했거나 손상 파일을 보존하지 못했으면 true.
    /// 이 동안 Save는 기존 파일을 건드리지 않고 false를 돌려준다. 이후 Load가 성공하면 풀린다.
    /// </summary>
    public bool LastLoadFailed { get; private set; }

    /// <summary>시험용: 손상 파일 보존 동작을 대체한다. 보존에 성공하면 true를 돌려준다.</summary>
    internal Func<string, bool>? PreserveHook { get; set; }

    private const int ReadAttempts = 3;
    private const int RetryDelayMs = 100;

    public AppData Load()
    {
        LastLoadRecovered = false;
        LastLoadFailed = false;
        if (!File.Exists(path)) return new AppData();

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var stream = File.OpenRead(path);
                var data = JsonSerializer.Deserialize<AppData>(stream, Options);
                if (data is null) throw new JsonException("null document");
                // JSON에 null로 적힌 항목을 기본값으로 채운다.
                data.Memos ??= new();
                data.Observed ??= new();
                data.Settings ??= new();
                return data;
            }
            catch (JsonException)
            {
                // 보존에 실패하면 원본을 덮어쓰지 않도록 저장을 막는다.
                if (PreserveCorrupt()) LastLoadRecovered = true;
                else LastLoadFailed = true;
                return new AppData();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt >= ReadAttempts)
                {
                    LastLoadFailed = true;
                    return new AppData();
                }
                Thread.Sleep(RetryDelayMs);
            }
        }
    }

    public bool Save(AppData data)
    {
        // 읽기에 실패한 파일을 기본값으로 덮어쓰지 않는다.
        if (LastLoadFailed) return false;
        string? temp = null;
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
            Directory.CreateDirectory(dir);
            // 같은 폴더에 고유한 이름으로 써서 다른 프로세스와 겹치지 않게 한다.
            temp = Path.Combine(dir, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(data, Options);
            using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes);
                // 교체 전에 디스크까지 내려 전원 차단 시 빈 파일이 남지 않게 한다.
                fs.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException
                                      or ArgumentException or JsonException)
        {
            if (temp is not null)
            {
                try { File.Delete(temp); } catch { }
            }
            return false;
        }
    }

    /// <summary>손상된 파일을 *.corrupt-시각 이름으로 옮기고, 옮기지 못하면 복사한다. 성공하면 true.</summary>
    private bool PreserveCorrupt()
    {
        if (PreserveHook is not null) return PreserveHook(path);
        var dest = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}";
        try
        {
            File.Move(path, dest);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        try
        {
            File.Copy(path, dest);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
