using ComPortWidget.Core.Devices;

namespace ComPortWidget.Core.Tests;

public class DemoDeviceSourceTests
{
    [Fact]
    public void Demo_records_use_only_fake_serials()
    {
        var records = new DemoDeviceSource().GetRecords();
        Assert.NotEmpty(records);
        foreach (var r in records)
            Assert.StartsWith("DEMO", r.InstanceId.Split((char)92).Last());
    }

    [Fact]
    public void Demo_has_connected_and_disconnected_records()
    {
        var records = new DemoDeviceSource().GetRecords();
        Assert.Contains(records, r => r.Present);
        Assert.Contains(records, r => !r.Present);
    }

    [Fact]
    public void Demo_has_a_recent_arrival_for_highlight()
    {
        var records = new DemoDeviceSource().GetRecords();
        Assert.Contains(records, r => r.Present && DateTimeOffset.Now - r.LastArrival < TimeSpan.FromMinutes(2));
    }
}
