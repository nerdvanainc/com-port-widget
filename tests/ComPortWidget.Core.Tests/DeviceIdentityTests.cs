using ComPortWidget.Core.Devices;

namespace ComPortWidget.Core.Tests;

public class DeviceIdentityTests
{
    private static DeviceRecord Make(string id, string ancestors) => new(
        id, "COM3", true, null, null, null, null,
        ancestors.Length == 0 ? Array.Empty<string>() : ancestors.Split('|'));

    [Theory]
    // 자체 시리얼
    [InlineData(@"USB\VID_10C4&PID_EA60\ABC123", "", "10C4:EA60:ABC123", true)]
    // 소문자 시리얼 정규화
    [InlineData(@"USB\VID_10C4&PID_EA60\abc123", "", "10C4:EA60:ABC123", true)]
    // 복합 장치, VID/PID가 같은 상위 장치의 MAC 시리얼: VID/PID를 키에서 뺀다
    [InlineData(@"USB\VID_303A&PID_1001&MI_00\9&25EF&0&0000", @"USB\VID_303A&PID_1001\AA:BB:CC:DD:EE:01", "MAC:AA:BB:CC:DD:EE:01:00", true)]
    // 같은 보드의 앱 모드: 소문자 MAC, 다른 VID/PID, 같은 키
    [InlineData(@"USB\VID_239A&PID_811B&MI_00\9&16AB&0&0000", @"USB\VID_239A&PID_811B\aa:bb:cc:dd:ee:01", "MAC:AA:BB:CC:DD:EE:01:00", true)]
    // FTDI 단일 채널: 상위 USB 시리얼 우선 (채널 문자 없는 값)
    [InlineData(@"FTDIBUS\VID_0403+PID_6001+B0000XYZA\0000", @"USB\VID_0403&PID_6001\B0000XYZ", "0403:6001:B0000XYZ", true)]
    // FTDI 상위 정보 없음: + 구분 시리얼 사용
    [InlineData(@"FTDIBUS\VID_0403+PID_6001+B0000XYZA\0000", "", "0403:6001:B0000XYZA", true)]
    // FTDI 2채널 시리얼 있음: 인터페이스 번호 포함
    [InlineData(@"FTDIBUS\VID_0403+PID_6010+FT1234B\0000", @"USB\VID_0403&PID_6010&MI_01\a&5da&0&0001|USB\VID_0403&PID_6010\FT1234", "0403:6010:FT1234:01", true)]
    // FTDI 2채널 시리얼 없음: 인스턴스 ID
    [InlineData(@"FTDIBUS\VID_0403+PID_6010+9&A1B2&0&1&2\0000", @"USB\VID_0403&PID_6010&MI_01\a&5da&0&0001|USB\VID_0403&PID_6010\9&A1B2&0&1", @"FTDIBUS\VID_0403+PID_6010+9&A1B2&0&1&2\0000", false)]
    // 상위가 허브: 허브 시리얼을 쓰지 않는다
    [InlineData(@"USB\VID_1A86&PID_7523\5&123&0&2", @"USB\VID_0BDA&PID_5429\HUB0001", @"USB\VID_1A86&PID_7523\5&123&0&2", false)]
    // 알려진 기본 시리얼은 시리얼 없음으로 처리
    [InlineData(@"USB\VID_10C4&PID_EA60\0001", "", @"USB\VID_10C4&PID_EA60\0001", false)]
    [InlineData(@"USB\VID_10C4&PID_EA60\123456", "", @"USB\VID_10C4&PID_EA60\123456", false)]
    // FTDI + 기본 시리얼: 규칙 7이 FTDI 경로에도 적용된다
    [InlineData(@"FTDIBUS\VID_0403+PID_6001+0001A\0000", @"USB\VID_0403&PID_6001\0001", @"FTDIBUS\VID_0403+PID_6001+0001A\0000", false)]
    [InlineData(@"FTDIBUS\VID_0403+PID_6001+123456A\0000", @"USB\VID_0403&PID_6001\123456", @"FTDIBUS\VID_0403+PID_6001+123456A\0000", false)]
    [InlineData(@"FTDIBUS\VID_0403+PID_6001+0001A\0000", "", @"FTDIBUS\VID_0403+PID_6001+0001A\0000", false)]
    [InlineData(@"FTDIBUS\VID_0403+PID_6010+0001B\0000", @"USB\VID_0403&PID_6010&MI_01\a&5da&0&0001|USB\VID_0403&PID_6010\0001", @"FTDIBUS\VID_0403+PID_6010+0001B\0000", false)]
    // 가상 포트
    [InlineData(@"COM0COM\PORT\CNCA1", @"ROOT\COM0COM\0001", @"COM0COM\PORT\CNCA1", false)]
    public void Resolve_returns_expected_key(string id, string ancestors, string key, bool hasSerial)
    {
        var result = DeviceIdentity.Resolve(Make(id, ancestors));
        Assert.Equal(key, result.Value);
        Assert.Equal(hasSerial, result.HasSerial);
    }

    [Theory]
    [InlineData(@"USB\VID_10C4&PID_EA60\ABC123", true, "10C4", "EA60")]
    [InlineData(@"USB\VID_303A&PID_1001&MI_00\9&25EF&0&0000", true, "303A", "1001")]
    [InlineData(@"FTDIBUS\VID_0403+PID_6001+B0000XYZA\0000", true, "0403", "6001")]
    [InlineData(@"usb\vid_10c4&pid_ea60\abc", true, "10C4", "EA60")]
    [InlineData(@"COM0COM\PORT\CNCA1", false, "", "")]
    public void TryParseVidPid_parses_instance_id(string id, bool ok, string vid, string pid)
    {
        Assert.Equal(ok, DeviceIdentity.TryParseVidPid(Make(id, ""), out var v, out var p));
        Assert.Equal(vid, v);
        Assert.Equal(pid, p);
    }
}
