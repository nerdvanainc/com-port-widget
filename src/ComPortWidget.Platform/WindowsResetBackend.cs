using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ComPortWidget.Core.Devices;
using ComPortWidget.Core.Reset;

namespace ComPortWidget.Platform;

internal sealed record ProcessRunResult(int ExitCode, string Output);

/// <summary>외부 프로그램 실행. 셸을 거치지 않는다. 테스트는 가짜 구현을 쓴다.</summary>
internal interface IProcessRunner
{
    ProcessRunResult Run(string exe, IReadOnlyList<string> args);
}

/// <summary>msports.dll의 ComDB API. 반환값은 Win32 오류 코드(0이 성공). 테스트는 가짜 구현을 쓴다.</summary>
internal interface IComDbNative
{
    int Open(out nint handle);
    int ReleasePort(nint handle, int number);
    int Close(nint handle);
}

/// <summary>시험 실행에서 하지 않고 기록만 한 동작 목록.</summary>
public interface IDryRunLog
{
    IReadOnlyList<string> Actions { get; }
}

internal sealed class RealProcessRunner : IProcessRunner
{
    private const int TimeoutMs = 60_000;

    public ProcessRunResult Run(string exe, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            // 콘솔 프로그램은 OEM 코드 페이지로 출력한다
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var oem = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            psi.StandardOutputEncoding = oem;
            psi.StandardErrorEncoding = oem;
        }
        catch (Exception) { /* 기본 인코딩을 쓴다 */ }

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Process did not start.");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(TimeoutMs))
        {
            try { p.Kill(true); } catch (Exception) { }
            throw new TimeoutException("Process did not finish in time.");
        }
        Task.WaitAll(stdout, stderr);
        return new ProcessRunResult(p.ExitCode, (stdout.Result + stderr.Result).Trim());
    }
}

internal sealed class RealComDbNative : IComDbNative
{
    public int Open(out nint handle) => ComDBOpen(out handle);
    public int ReleasePort(nint handle, int number) => ComDBReleasePort(handle, (uint)number);
    public int Close(nint handle) => ComDBClose(handle);

    [DllImport("msports.dll")] private static extern int ComDBOpen(out nint handle);
    [DllImport("msports.dll")] private static extern int ComDBClose(nint handle);
    [DllImport("msports.dll")] private static extern int ComDBReleasePort(nint handle, uint comNumber);
}

/// <summary>
/// 실제 시스템을 읽고 바꾸는 정리 백엔드. 읽기는 엄격 모드(Ports 클래스, 하나라도 못 읽으면 예외)이다.
/// dryRun이 참이면 장치 삭제와 번호 해제는 실행하지 않고 Actions에 기록만 한다.
/// </summary>
public sealed class WindowsResetBackend : IResetBackend, IDryRunLog
{
    private static readonly JsonSerializerOptions BackupOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _backupDir;
    private readonly bool _dryRun;
    private readonly Func<IReadOnlyList<DeviceRecord>> _readRecords;
    private readonly Func<byte[]> _readComDb;
    private readonly Func<ISet<int>> _readOthers;
    private readonly IProcessRunner _runner;
    private readonly IComDbNative _comDb;
    private readonly Func<DateTime> _clock;
    private readonly TimeSpan _comDbTimeout;
    private readonly List<string> _actions = new();
    private bool _comDbStuck;

    public WindowsResetBackend(string backupDir, bool dryRun)
        : this(backupDir, dryRun,
            () => new WindowsDeviceSource().GetRecordsStrict(),
            ComRegistry.ReadComDb, ComRegistry.ReadOtherHolders,
            new RealProcessRunner(), new RealComDbNative(), () => DateTime.Now)
    {
    }

    internal WindowsResetBackend(string backupDir, bool dryRun,
        Func<IReadOnlyList<DeviceRecord>> readRecords, Func<byte[]> readComDb, Func<ISet<int>> readOthers,
        IProcessRunner runner, IComDbNative comDb, Func<DateTime> clock, TimeSpan? comDbTimeout = null)
    {
        _backupDir = backupDir;
        _dryRun = dryRun;
        _readRecords = readRecords;
        _readComDb = readComDb;
        _readOthers = readOthers;
        _runner = runner;
        _comDb = comDb;
        _clock = clock;
        _comDbTimeout = comDbTimeout ?? TimeSpan.FromSeconds(30);
    }

    public IReadOnlyList<string> Actions => _actions;

    public IReadOnlyList<DeviceRecord> GetRecords() => _readRecords();

    public ISet<int> GetReservedPorts() => ComDbBitmap.ToPorts(_readComDb());

    public ISet<int> GetOtherHolders() => _readOthers();

    public void WriteBackup(ResetPlan plan, IReadOnlyList<DeviceRecord> targets)
    {
        Directory.CreateDirectory(_backupDir);
        var comDb = _readComDb();
        var json = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Created = _clock().ToString("o"),
            DryRun = _dryRun,
            ComDbBase64 = Convert.ToBase64String(comDb),
            Plan = new { plan.InstanceIds, plan.ReservedOnlyPorts },
            Targets = targets,
        }, BackupOptions);

        // 같은 초에 두 번 써도 덮어쓰지 않도록 번호를 붙인다
        var stamp = _clock().ToString("yyyyMMdd-HHmmss");
        for (var i = 0; ; i++)
        {
            var name = i == 0 ? $"backup-{stamp}.json" : $"backup-{stamp}-{i}.json";
            try
            {
                using var fs = new FileStream(Path.Combine(_backupDir, name), FileMode.CreateNew, FileAccess.Write, FileShare.None);
                fs.Write(json);
                fs.Flush(flushToDisk: true);
                return;
            }
            catch (IOException) when (File.Exists(Path.Combine(_backupDir, name)) && i < 100)
            {
            }
        }
    }

    /// <summary>현재 연결 여부. 조회가 실패하거나 장치를 찾지 못하면 예외이고, 성공한 읽기가 미연결이라고 할 때만 false.</summary>
    public bool IsPresent(string instanceId)
    {
        var records = _readRecords();
        var rec = records.FirstOrDefault(r => string.Equals(r.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Device record was not found.");
        return rec.Present;
    }

    private const int ExitRebootRequired = 3010; // ERROR_SUCCESS_REBOOT_REQUIRED

    /// <summary>true면 삭제가 재부팅 후에 끝난다(기록이 남아 있을 수 있다).</summary>
    public bool RemoveDevice(string instanceId)
    {
        if (_dryRun)
        {
            _actions.Add("pnputil /remove-device " + instanceId);
            return false;
        }
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "pnputil.exe");
        var r = _runner.Run(exe, ["/remove-device", instanceId]);
        if (r.ExitCode == ExitRebootRequired) return true;
        if (r.ExitCode != 0)
            throw new InvalidOperationException($"pnputil exited with {r.ExitCode}: {r.Output}");
        return false;
    }

    public void ReleasePort(int number)
    {
        if (_dryRun)
        {
            _actions.Add("ComDBReleasePort COM" + number);
            return;
        }
        // ComDBOpen은 잠금을 얻을 때까지 무기한 기다릴 수 있으므로 작업 스레드에서 제한 시간을 두고 실행한다
        if (_comDbStuck) throw new TimeoutException("A previous ComDB call did not finish.");
        OnWorker(abandoned =>
        {
            // ComDB를 잠그는 시간을 줄이려고 호출마다 열고 닫는다
            var rc = _comDb.Open(out var handle);
            if (rc != 0) throw new InvalidOperationException("ComDBOpen failed: " + rc);
            try
            {
                // 호출부가 이미 시간 초과로 포기했다면 번호를 해제하지 않고 닫기만 한다
                if (abandoned()) throw new TimeoutException("ComDB call was abandoned.");
                rc = _comDb.ReleasePort(handle, number);
                if (rc != 0) throw new InvalidOperationException($"ComDBReleasePort(COM{number}) failed: {rc}");
            }
            finally
            {
                _comDb.Close(handle);
            }
        });
    }

    private void OnWorker(Action<Func<bool>> work)
    {
        Exception? error = null;
        var gaveUp = false;
        var thread = new Thread(() =>
        {
            try { work(() => Volatile.Read(ref gaveUp)); }
            catch (Exception ex) { error = ex; }
        }) { IsBackground = true, Name = "ComDbCall" };
        thread.Start();
        if (!thread.Join(_comDbTimeout))
        {
            Volatile.Write(ref gaveUp, true);
            _comDbStuck = true; // 멈춘 호출이 남아 있으므로 이후 호출은 기다리지 않고 실패시킨다
            throw new TimeoutException("ComDB call did not finish in time.");
        }
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
