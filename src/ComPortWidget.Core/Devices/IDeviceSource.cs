namespace ComPortWidget.Core.Devices;

/// <summary>장치 기록을 읽어 오는 원본. 실제 구현은 Platform 프로젝트에 둔다.</summary>
public interface IDeviceSource
{
    IReadOnlyList<DeviceRecord> GetRecords();
}
