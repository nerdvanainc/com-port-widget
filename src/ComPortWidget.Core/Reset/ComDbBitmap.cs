namespace ComPortWidget.Core.Reset;

/// <summary>ComDB 예약 비트맵 해석. 바이트 0의 최하위 비트가 COM1이다.</summary>
public static class ComDbBitmap
{
    public static SortedSet<int> ToPorts(byte[] comDb)
    {
        var ports = new SortedSet<int>();
        for (var i = 0; i < comDb.Length; i++)
            for (var bit = 0; bit < 8; bit++)
                if ((comDb[i] & (1 << bit)) != 0)
                    ports.Add(i * 8 + bit + 1);
        return ports;
    }
}
