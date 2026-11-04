namespace ComPortWidget.Core.Devices;

/// <summary>
/// COM 포트 장치 하나의 Windows 기록. Ancestors는 가까운 상위 장치부터 나열한 인스턴스 ID이다.
/// AncestorsIncomplete는 식별에 필요한 상위 장치를 끝까지 읽지 못했다는 표시이다(예: 상위 장치 기록이 이미 없음).
/// 이 경우 장비 키가 시리얼을 잃을 수 있어, 정리 후보에서는 기본 선택하지 않는다.
/// </summary>
public sealed record DeviceRecord(
    string InstanceId,
    string PortName,
    bool Present,
    string? BusDescription,
    string? FriendlyName,
    DateTimeOffset? LastArrival,
    DateTimeOffset? LastRemoval,
    IReadOnlyList<string> Ancestors,
    bool AncestorsIncomplete = false);
