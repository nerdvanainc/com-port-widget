using ComPortWidget.Core.Reset;

namespace ComPortWidget.Core.Tests;

public class ComDbBitmapTests
{
    [Fact]
    public void ToPorts_maps_first_bit_to_COM1()
        => Assert.Equal(new[] { 1 }, ComDbBitmap.ToPorts(new byte[] { 0x01 }));

    [Fact]
    public void ToPorts_maps_bit_order_within_byte()
        => Assert.Equal(new[] { 2, 8 }, ComDbBitmap.ToPorts(new byte[] { 0x82 }));

    [Fact]
    public void ToPorts_supports_numbers_over_256()
    {
        var data = new byte[40];
        data[39] = 0x80;
        Assert.Equal(new[] { 320 }, ComDbBitmap.ToPorts(data));
    }

    [Fact]
    public void ToPorts_returns_empty_for_empty_input()
        => Assert.Empty(ComDbBitmap.ToPorts(Array.Empty<byte>()));
}
