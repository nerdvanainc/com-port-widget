using ComPortWidget.Core.Devices;

namespace ComPortWidget.Core.Tests;

public class VendorTableTests
{
    private static DeviceRecord Make(string id, string? bus = null, string? friendly = null) => new(
        id, "COM5", true, bus, friendly, null, null, Array.Empty<string>());

    [Theory]
    [InlineData("0403", "FTDI")] [InlineData("10C4", "Silicon Labs")]
    [InlineData("303A", "Espressif")] [InlineData("1A86", "WCH")]
    [InlineData("239A", "Adafruit")] [InlineData("0483", "STMicroelectronics")]
    [InlineData("067B", "Prolific")] [InlineData("2341", "Arduino")]
    [InlineData("1366", "SEGGER")] [InlineData("0d28", "Arm")]
    public void Lookup_returns_vendor(string vid, string name)
        => Assert.Equal(name, VendorTable.Lookup(vid));

    [Fact]
    public void Lookup_returns_null_for_unknown_vid()
        => Assert.Null(VendorTable.Lookup("ABCD"));

    [Fact]
    public void DisplayVendor_shows_number_for_unknown_vid()
        => Assert.Equal("VID ABCD", VendorTable.DisplayVendor(Make(@"USB\VID_ABCD&PID_0001\X1")));

    [Fact]
    public void DisplayVendor_is_empty_for_shared_vid()
        => Assert.Equal("", VendorTable.DisplayVendor(Make(@"USB\VID_16D0&PID_0001\X1")));

    [Fact]
    public void DisplayVendor_is_empty_without_vid()
        => Assert.Equal("", VendorTable.DisplayVendor(Make(@"COM0COM\PORT\CNCA1")));

    [Fact]
    public void DisplayVendor_shows_name_for_known_vid()
        => Assert.Equal("FTDI", VendorTable.DisplayVendor(Make(@"USB\VID_0403&PID_6001\X1")));

    [Fact]
    public void DisplayDescription_prefers_bus_description()
        => Assert.Equal("CP2102 USB to UART Bridge",
            VendorTable.DisplayDescription(Make("X", "CP2102 USB to UART Bridge", "Silicon Labs (COM5)")));

    [Fact]
    public void DisplayDescription_strips_com_suffix_from_friendly_name()
        => Assert.Equal("USB Serial Port",
            VendorTable.DisplayDescription(Make("X", null, "USB Serial Port(COM5)")));

    [Fact]
    public void DisplayDescription_is_empty_without_any_description()
        => Assert.Equal("", VendorTable.DisplayDescription(Make("X")));
}
