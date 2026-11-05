using System.Runtime.InteropServices;
using System.Text;
using ComPortWidget.Core.Devices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace ComPortWidget.Platform;

/// <summary>장치 한 건의 속성을 읽는 계층. 테스트에서 가짜로 바꿀 수 있게 분리한다.</summary>
public interface IDevicePropertyReader
{
    IReadOnlyList<string> ListInstanceIds();
    DeviceRecord? TryRead(string instanceId);
}

/// <summary>
/// SetupAPI로 Ports 클래스 장치 기록을 읽는다. 읽기 전용이며 포트를 열지 않는다.
/// GetRecords는 읽지 못한 기록을 건너뛰는 관대한 모드, GetRecordsStrict는 하나라도 못 읽으면 예외를 던지는 엄격한 모드이다.
/// </summary>
public sealed class WindowsDeviceSource : IDeviceSource
{
    private readonly IDevicePropertyReader? _reader;

    public WindowsDeviceSource(IDevicePropertyReader? reader = null) => _reader = reader;

    public IReadOnlyList<DeviceRecord> GetRecords() => Read(strict: false);

    /// <summary>건너뛴 기록이 있거나 PortName이 빈 기록이 있으면 예외를 던진다.
    /// 읽기 실패의 원인에 따라 InvalidOperationException, Win32Exception, IOException 등 어떤 예외든 나올 수 있다. 정리 기능이 사용한다.</summary>
    public IReadOnlyList<DeviceRecord> GetRecordsStrict() => Read(strict: true);

    private IReadOnlyList<DeviceRecord> Read(bool strict)
    {
        var owned = _reader is null ? new SetupApiReader(strict) : null;
        try
        {
            var reader = _reader ?? owned!;
            var result = new List<DeviceRecord>();
            foreach (var id in reader.ListInstanceIds())
            {
                DeviceRecord? rec;
                try
                {
                    rec = reader.TryRead(id);
                }
                catch (Exception) when (!strict)
                {
                    continue; // 한 장치의 읽기 실패가 전체 목록을 막지 않게 건너뛴다
                }
                if (rec is null)
                {
                    if (strict) throw new InvalidOperationException($"Device record could not be read: {id}");
                    continue;
                }
                if (strict && rec.PortName.Length == 0)
                    throw new InvalidOperationException($"Device record has no readable PortName: {id}");
                result.Add(rec);
            }
            return result;
        }
        finally
        {
            owned?.Dispose();
        }
    }
}

/// <summary>레지스트리 키 하나. 읽기 전용 조회만 노출한다.</summary>
internal interface IRegistryNode : IDisposable
{
    string[] GetSubKeyNames();
    string[] GetValueNames();
    /// <summary>값이 없으면 null. 값이 있는데 읽지 못하면 예외.</summary>
    object? GetValue(string name);
    /// <summary>없는 하위 키(ERROR_FILE_NOT_FOUND, ERROR_PATH_NOT_FOUND)만 null. 그 밖의 모든 열기 실패는 예외.</summary>
    IRegistryNode? OpenSubKey(string name);
}

/// <summary>HKLM 아래 경로를 여는 진입점. 테스트에서 가짜로 바꾼다.</summary>
internal interface IRegistryView
{
    /// <summary>없는 키(ERROR_FILE_NOT_FOUND, ERROR_PATH_NOT_FOUND)만 null. 그 밖의 모든 열기 실패는 예외.</summary>
    IRegistryNode? Open(string path);
}

internal sealed class RealRegistryView : IRegistryView
{
    public IRegistryNode? Open(string path)
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
        return RealNode.OpenKey(hklm, path);
    }
}

/// <summary>RegistryKey 래퍼. RegistryKey.GetValue는 조회 실패에도 예외 없이 null을 돌려주므로 값 이름 목록으로 구분한다.</summary>
internal sealed class RealNode(RegistryKey key) : IRegistryNode
{
    public string[] GetSubKeyNames() => key.GetSubKeyNames();
    public string[] GetValueNames() => key.GetValueNames();

    public object? GetValue(string name)
    {
        var v = key.GetValue(name);
        if (v is not null) return v;
        // null이면 값이 없는 것인지 읽기에 실패한 것인지 구분한다 (GetValueNames 실패는 그대로 전파)
        if (key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new IOException("Registry value exists but could not be read.");
        return null;
    }

    public IRegistryNode? OpenSubKey(string name) => OpenKey(key, name);

    /// <summary>
    /// RegOpenKeyExW(KEY_READ)로 직접 연다. RegistryKey.OpenSubKey는 접근 거부 외의 실패에도 null을 돌려주므로 쓰지 않는다.
    /// ERROR_FILE_NOT_FOUND(2), ERROR_PATH_NOT_FOUND(3)만 null이고 나머지는 Win32Exception.
    /// </summary>
    internal static RealNode? OpenKey(RegistryKey parent, string name)
    {
        var err = NativeMethods.RegOpenKeyExW(parent.Handle, name, 0, NativeMethods.KeyRead, out var h);
        if (err == 2 || err == 3) return null;
        if (err != 0) throw new System.ComponentModel.Win32Exception(err);
        var safe = new SafeRegistryHandle(h, ownsHandle: true);
        return new RealNode(RegistryKey.FromHandle(safe));
    }

    public void Dispose() => key.Dispose();
}

/// <summary>ComDB와 다른 COM 번호 점유처를 레지스트리에서 읽기 전용으로 조회한다. 읽기 실패는 예외로 알린다.</summary>
public static class ComRegistry
{
    private const string ComDbKey = @"SYSTEM\CurrentControlSet\Control\COM Name Arbiter";
    private const string PortsClass = "{4d36e978-e325-11ce-bfc1-08002be10318}";
    private const string ModemClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4D36E96D-E325-11CE-BFC1-08002BE10318}";
    private const string SerialCommKey = @"HARDWARE\DEVICEMAP\SERIALCOMM";
    private const string EnumKey = @"SYSTEM\CurrentControlSet\Enum";

    /// <summary>ComDB 원본 바이트. 키나 값이 없을 때만 빈 배열이고, 읽기 실패는 예외.</summary>
    public static byte[] ReadComDb() => ReadComDb(new RealRegistryView());

    /// <summary>ComDB 밖에서 COM 번호를 쓰는 곳(SERIALCOMM, Ports 외 PortName, 모뎀 AttachedTo)의 번호 집합. 읽기 실패는 예외.</summary>
    public static ISet<int> ReadOtherHolders() => ReadOtherHolders(new RealRegistryView());

    internal static byte[] ReadComDb(IRegistryView view)
    {
        using var key = view.Open(ComDbKey);
        if (key is null) return Array.Empty<byte>();
        var v = key.GetValue("ComDB");
        if (v is null) return Array.Empty<byte>();
        return v as byte[] ?? throw new InvalidDataException("ComDB value is not binary.");
    }

    internal static ISet<int> ReadOtherHolders(IRegistryView view)
    {
        var set = new HashSet<int>();
        ReadSerialComm(view, set);
        ReadEnumPortNames(view, set);
        ReadModemAttachedTo(view, set);
        return set;
    }

    internal static bool TryParseComNumber(object? value, out int number)
    {
        number = 0;
        if (value is not string s) return false;
        s = s.Trim();
        return s.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(s.Substring(3), out number) && number > 0;
    }

    private static void ReadSerialComm(IRegistryView view, HashSet<int> set)
    {
        using var key = view.Open(SerialCommKey); // 장치가 없으면 키 자체가 없을 수 있다
        if (key is null) return;
        foreach (var name in key.GetValueNames())
            if (TryParseComNumber(key.GetValue(name), out var n)) set.Add(n);
    }

    private static void ReadEnumPortNames(IRegistryView view, HashSet<int> set)
    {
        using var enumKey = view.Open(EnumKey) ?? throw new InvalidOperationException("Enum key not found.");
        // Enum\<버스>\<장치>\<인스턴스> 3단계를 훑는다. 도중에 사라진 키(null)는 건너뛰고, 접근 거부는 예외로 알린다.
        foreach (var bus in enumKey.GetSubKeyNames())
        {
            using var busKey = enumKey.OpenSubKey(bus);
            if (busKey is null) continue;
            foreach (var dev in busKey.GetSubKeyNames())
            {
                using var devKey = busKey.OpenSubKey(dev);
                if (devKey is null) continue;
                foreach (var inst in devKey.GetSubKeyNames())
                {
                    using var instKey = devKey.OpenSubKey(inst);
                    if (instKey is null) continue;
                    if (string.Equals(instKey.GetValue("ClassGUID") as string, PortsClass, StringComparison.OrdinalIgnoreCase))
                        continue; // Ports 클래스는 장치 조회 쪽에서 다룬다
                    using var p = instKey.OpenSubKey("Device Parameters");
                    if (p is not null && TryParseComNumber(p.GetValue("PortName"), out var n)) set.Add(n);
                }
            }
        }
    }

    private static void ReadModemAttachedTo(IRegistryView view, HashSet<int> set)
    {
        using var cls = view.Open(ModemClassKey);
        if (cls is null) return;
        foreach (var sub in cls.GetSubKeyNames())
        {
            // Properties 같은 비숫자 키는 정상 PC에서도 접근이 거부되므로 읽지 않는다
            if (sub.Length == 0 || !sub.All(Compat.IsAsciiDigit)) continue;
            using var k = cls.OpenSubKey(sub);
            if (k is not null && TryParseComNumber(k.GetValue("AttachedTo"), out var n)) set.Add(n);
        }
    }
}

/// <summary>상위 장치 기록이 시스템에 이미 없을 때 던진다. 읽기 실패와 구분해서 사슬을 거기서 끊는다.</summary>
internal sealed class AncestorMissingException(string instanceId)
    : Exception($"Device record no longer exists: {instanceId}");

/// <summary>상위 장치 사슬. Incomplete는 식별에 필요한 부분을 끝까지 읽지 못했다는 뜻이다.</summary>
internal sealed record AncestorChain(List<string> Ids, bool Incomplete);

/// <summary>SetupAPI/CfgMgr32 기반 실제 구현. 쓰기 API는 선언하지 않는다.</summary>
internal sealed class SetupApiReader : IDevicePropertyReader, IDisposable
{
    private static readonly Guid PortsGuid = new("4D36E978-E325-11CE-BFC1-08002BE10318");
    private const int MaxAncestorDepth = 8;
    private const int ErrorNoSuchDevinst = unchecked((int)0xE000020B);

    private readonly nint _set;
    private readonly Dictionary<string, NativeMethods.SP_DEVINFO_DATA> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _strict;
    private List<string>? _ids;

    public SetupApiReader(bool strict = false)
    {
        _strict = strict;
        var g = PortsGuid;
        // DIGCF_PRESENT를 주지 않아 미연결 기록까지 읽는다
        _set = NativeMethods.SetupDiGetClassDevsW(ref g, null, 0, 0);
        if (_set == NativeMethods.InvalidHandle)
            throw new InvalidOperationException("SetupDiGetClassDevsW failed: " + Marshal.GetLastWin32Error());
    }

    public IReadOnlyList<string> ListInstanceIds()
    {
        if (_ids is not null) return _ids;
        var ids = new List<string>();
        for (uint i = 0; ; i++)
        {
            var data = new NativeMethods.SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<NativeMethods.SP_DEVINFO_DATA>() };
            if (!NativeMethods.SetupDiEnumDeviceInfo(_set, i, ref data))
            {
                var err = Marshal.GetLastWin32Error();
                if (err == 259 /* ERROR_NO_MORE_ITEMS */) break;
                throw new InvalidOperationException("SetupDiEnumDeviceInfo failed: " + err);
            }
            var sb = new StringBuilder(512);
            if (!NativeMethods.SetupDiGetDeviceInstanceIdW(_set, ref data, sb, sb.Capacity, out _))
            {
                if (_strict) throw new InvalidOperationException("SetupDiGetDeviceInstanceIdW failed: " + Marshal.GetLastWin32Error());
                continue; // 관대한 모드에서는 건너뛴다
            }
            var id = sb.ToString();
            _devices[id] = data;
            ids.Add(id);
        }
        return _ids = ids;
    }

    public DeviceRecord? TryRead(string instanceId)
    {
        ListInstanceIds();
        if (!_devices.TryGetValue(instanceId, out var data)) return null;

        var chain = ReadAncestors(instanceId, ref data);
        return Compose(
            instanceId,
            ReadBool(_set, ref data, NativeMethods.KeyIsPresent),
            ReadPortName(ref data),
            ReadString(_set, ref data, NativeMethods.KeyBusReportedDeviceDesc),
            ReadString(_set, ref data, NativeMethods.KeyFriendlyName),
            ReadFileTime(_set, ref data, NativeMethods.KeyLastArrival),
            ReadFileTime(_set, ref data, NativeMethods.KeyLastRemoval),
            chain.Ids,
            chain.Incomplete);
    }

    /// <summary>읽은 값으로 기록을 만든다. 연결 여부를 못 읽었으면 미연결로 보지 않고 null(읽기 실패)을 돌려준다.</summary>
    internal static DeviceRecord? Compose(string instanceId, bool? present, string? portName, string? busDescription,
        string? friendlyName, DateTimeOffset? lastArrival, DateTimeOffset? lastRemoval, IReadOnlyList<string> ancestors,
        bool ancestorsIncomplete = false)
        => present is null
            ? null
            : new DeviceRecord(instanceId, portName ?? string.Empty, present.Value, busDescription, friendlyName,
                lastArrival, lastRemoval, ancestors, ancestorsIncomplete);

    private string? ReadPortName(ref NativeMethods.SP_DEVINFO_DATA data)
    {
        // DICS_FLAG_GLOBAL, DIREG_DEV, KEY_READ: 읽기 전용으로 연다
        var hKey = NativeMethods.SetupDiOpenDevRegKey(_set, ref data, 1, 0, 1, NativeMethods.KeyRead);
        if (hKey == NativeMethods.InvalidHandle) return null;
        using var safe = new SafeRegistryHandle(hKey, ownsHandle: true);
        using var key = RegistryKey.FromHandle(safe);
        return key.GetValue("PortName") as string;
    }

    private AncestorChain ReadAncestors(string instanceId, ref NativeMethods.SP_DEVINFO_DATA data)
    {
        var own = data; // ref 매개변수는 람다에서 쓸 수 없어 복사한다
        var set = _set;
        return WalkAncestors(
            instanceId,
            id => id == instanceId ? ReadParentStrict(set, ref own) ?? ParentOf(id) : ParentOf(id),
            _strict);
    }

    /// <summary>
    /// 가까운 상위 장치부터 사슬을 만든다. parentOf는 상위가 없으면 null, 읽기에 실패하면 예외를 던진다.
    /// DeviceIdentity.Resolve는 VID/PID가 다른 첫 상위 장치에서 멈추므로 여기서도 거기까지만 읽는다
    /// (그 위의 허브나 컨트롤러를 못 읽는다고 정리가 막히면 안 된다). VID/PID가 없는 기록은 상위 장치를 쓰지 않는다.
    /// 상위 장치 기록이 이미 없으면(AncestorMissingException) 읽은 데까지를 돌려주고 Incomplete로 표시한다.
    /// 그 밖의 읽기 실패는 엄격 모드에서 어느 인스턴스인지 밝혀 던지고, 관대한 모드에서는 Incomplete로 표시한다.
    /// </summary>
    internal static AncestorChain WalkAncestors(string instanceId, Func<string, string?> parentOf, bool strict)
    {
        var list = new List<string>();
        if (!DeviceIdentity.TryParseVidPid(instanceId, out var vid, out var pid))
            return new AncestorChain(list, false);

        var current = instanceId;
        for (var depth = 0; depth < MaxAncestorDepth; depth++)
        {
            string? parent;
            try
            {
                parent = parentOf(current);
            }
            catch (AncestorMissingException)
            {
                return new AncestorChain(list, true);
            }
            catch (Exception ex) when (!strict)
            {
                _ = ex;
                return new AncestorChain(list, true);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Could not read the parent of '{current}' (ancestors of '{instanceId}'): {ex.Message}", ex);
            }
            if (parent is null || parent.Length == 0) break;
            if (parent.StartsWith("ROOT\\", StringComparison.OrdinalIgnoreCase)
                || parent.StartsWith("HTREE\\ROOT", StringComparison.OrdinalIgnoreCase)) break;
            list.Add(parent);
            // VID/PID가 다른 상위 장치는 목록에 넣되 그 위는 읽지 않는다
            if (!DeviceIdentity.TryParseVidPid(parent, out var v, out var p) || v != vid || p != pid) break;
            current = parent;
        }
        return new AncestorChain(list, false);
    }

    /// <summary>상위 장치 ID. 일반 조회가 안 되는 유령 장치는 속성 값(DEVPKEY_Device_Parent)으로 읽는다. 상위가 없으면 null, 읽기 실패는 예외.</summary>
    private static string? ParentOf(string id)
    {
        if (NativeMethods.CM_Locate_DevNodeW(out var inst, id, NativeMethods.CM_LOCATE_DEVNODE_PHANTOM) == 0
            && NativeMethods.CM_Get_Parent(out var parentInst, inst, 0) == 0)
        {
            var sb = new StringBuilder(512);
            if (NativeMethods.CM_Get_Device_IDW(parentInst, sb, sb.Capacity, 0) == 0)
                return sb.ToString();
        }
        return ParentFromProperty(id);
    }

    private static string? ParentFromProperty(string id)
    {
        var tmp = NativeMethods.SetupDiCreateDeviceInfoList(0, 0); // 메모리 안의 빈 목록. 시스템을 바꾸지 않는다.
        if (tmp == NativeMethods.InvalidHandle) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var d = new NativeMethods.SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<NativeMethods.SP_DEVINFO_DATA>() };
            if (!NativeMethods.SetupDiOpenDeviceInfoW(tmp, id, 0, 0, ref d))
            {
                var err = Marshal.GetLastWin32Error();
                if (err == ErrorNoSuchDevinst) throw new AncestorMissingException(id);
                throw new System.ComponentModel.Win32Exception(err);
            }
            return ReadParentStrict(tmp, ref d);
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(tmp);
        }
    }

    /// <summary>DEVPKEY_Device_Parent를 읽는다. 속성이 없으면(ERROR_NOT_FOUND) null, 그 밖의 실패는 예외.</summary>
    private static string? ReadParentStrict(nint set, ref NativeMethods.SP_DEVINFO_DATA data)
    {
        var key = NativeMethods.KeyParent;
        var buf = new byte[1024];
        if (!NativeMethods.SetupDiGetDevicePropertyW(set, ref data, ref key, out var type, buf, (uint)buf.Length, out var required, 0))
        {
            var err = Marshal.GetLastWin32Error();
            if (err == 1168 /* ERROR_NOT_FOUND */) return null;
            if (err != 122 /* ERROR_INSUFFICIENT_BUFFER */ || required == 0)
                throw new System.ComponentModel.Win32Exception(err);
            buf = new byte[required];
            if (!NativeMethods.SetupDiGetDevicePropertyW(set, ref data, ref key, out type, buf, (uint)buf.Length, out required, 0))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        if (type != NativeMethods.DEVPROP_TYPE_STRING) throw new InvalidDataException("Parent property has an unexpected type.");
        var s = Encoding.Unicode.GetString(buf, 0, (int)Math.Min(required, (uint)buf.Length)).TrimEnd('\0');
        return s.Length == 0 ? null : s;
    }

    private static byte[]? ReadProperty(nint set, ref NativeMethods.SP_DEVINFO_DATA data, NativeMethods.DEVPROPKEY key, uint expectedType)
    {
        var buf = new byte[1024];
        if (!NativeMethods.SetupDiGetDevicePropertyW(set, ref data, ref key, out var type, buf, (uint)buf.Length, out var required, 0))
        {
            if (Marshal.GetLastWin32Error() != 122 /* ERROR_INSUFFICIENT_BUFFER */ || required == 0) return null;
            buf = new byte[required];
            if (!NativeMethods.SetupDiGetDevicePropertyW(set, ref data, ref key, out type, buf, (uint)buf.Length, out required, 0)) return null;
        }
        if (type != expectedType) return null;
        var len = (int)Math.Min(required, (uint)buf.Length);
        var copy = new byte[len];
        Array.Copy(buf, copy, len);
        return copy;
    }

    private static string? ReadString(nint set, ref NativeMethods.SP_DEVINFO_DATA data, NativeMethods.DEVPROPKEY key)
    {
        var b = ReadProperty(set, ref data, key, NativeMethods.DEVPROP_TYPE_STRING);
        if (b is null) return null;
        var s = Encoding.Unicode.GetString(b).TrimEnd('\0');
        return s.Length == 0 ? null : s;
    }

    private static bool? ReadBool(nint set, ref NativeMethods.SP_DEVINFO_DATA data, NativeMethods.DEVPROPKEY key)
    {
        var b = ReadProperty(set, ref data, key, NativeMethods.DEVPROP_TYPE_BOOLEAN);
        return b is { Length: > 0 } ? b[0] != 0 : null;
    }

    private static DateTimeOffset? ReadFileTime(nint set, ref NativeMethods.SP_DEVINFO_DATA data, NativeMethods.DEVPROPKEY key)
    {
        var b = ReadProperty(set, ref data, key, NativeMethods.DEVPROP_TYPE_FILETIME);
        if (b is null || b.Length < 8) return null;
        var ft = BitConverter.ToInt64(b, 0);
        if (ft <= 0) return null;
        try { return DateTimeOffset.FromFileTime(ft); } // 로컬 시간대 기준
        catch (ArgumentOutOfRangeException) { return null; }
    }

    public void Dispose() => NativeMethods.SetupDiDestroyDeviceInfoList(_set);
}

internal static class NativeMethods
{
    public static readonly nint InvalidHandle = -1;
    public const uint KeyRead = 0x20019;
    public const uint CM_LOCATE_DEVNODE_PHANTOM = 1;
    public const uint DEVPROP_TYPE_BOOLEAN = 0x11;
    public const uint DEVPROP_TYPE_STRING = 0x12;
    public const uint DEVPROP_TYPE_FILETIME = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public nint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;
        public DEVPROPKEY(string guid, uint pid) { fmtid = new Guid(guid); this.pid = pid; }
    }

    public static readonly DEVPROPKEY KeyLastArrival = new("83DA6326-97A6-4088-9453-A1923F573B29", 102);
    public static readonly DEVPROPKEY KeyLastRemoval = new("83DA6326-97A6-4088-9453-A1923F573B29", 103);
    public static readonly DEVPROPKEY KeyIsPresent = new("540B947E-8B40-45BC-A8A2-6A0B894CBDA2", 5);
    public static readonly DEVPROPKEY KeyBusReportedDeviceDesc = new("540B947E-8B40-45BC-A8A2-6A0B894CBDA2", 4);
    public static readonly DEVPROPKEY KeyParent = new("4340A6C5-93FA-4706-972C-7B648008A5A7", 8);
    public static readonly DEVPROPKEY KeyFriendlyName = new("A45C254E-DF1C-4EFD-8020-67D146A850E0", 14);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern nint SetupDiGetClassDevsW(ref Guid classGuid, string? enumerator, nint hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern nint SetupDiCreateDeviceInfoList(nint classGuid, nint hwndParent);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiDestroyDeviceInfoList(nint set);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiEnumDeviceInfo(nint set, uint index, ref SP_DEVINFO_DATA data);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiOpenDeviceInfoW(nint set, string instanceId, nint hwndParent, uint flags, ref SP_DEVINFO_DATA data);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiGetDeviceInstanceIdW(nint set, ref SP_DEVINFO_DATA data, StringBuilder buf, int size, out int required);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiGetDevicePropertyW(nint set, ref SP_DEVINFO_DATA data, ref DEVPROPKEY key,
        out uint type, byte[] buffer, uint size, out uint required, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern nint SetupDiOpenDevRegKey(nint set, ref SP_DEVINFO_DATA data, uint scope, uint hwProfile, uint keyType, uint samDesired);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int RegOpenKeyExW(SafeRegistryHandle hKey, string subKey, uint options, uint samDesired, out nint result);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern uint CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll")]
    public static extern uint CM_Get_Parent(out uint parent, uint devInst, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern uint CM_Get_Device_IDW(uint devInst, StringBuilder buffer, int len, uint flags);
}
