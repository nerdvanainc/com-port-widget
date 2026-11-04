namespace ComPortWidget.Core.Devices;

/// <summary>호출할 때마다 지연을 다시 시작하는 타이머. 테스트에서 가짜로 바꿀 수 있게 분리한다.</summary>
public interface IDelayTimer
{
    /// <summary>이미 대기 중이면 취소하고, delay 뒤에 callback을 한 번 실행하도록 다시 예약한다.</summary>
    void Restart(TimeSpan delay, Action callback);
}

/// <summary>
/// 연속 호출을 하나로 묶는다(후행 디바운스). Trigger를 부를 때마다 지연이 다시 시작되고,
/// 마지막 호출 뒤 delay가 지나면 action을 한 번 실행한다.
/// </summary>
public sealed class Debouncer(TimeSpan delay, Action action, IDelayTimer timer)
{
    public void Trigger() => timer.Restart(delay, action);
}
