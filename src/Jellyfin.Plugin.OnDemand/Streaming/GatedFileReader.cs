namespace Jellyfin.Plugin.OnDemand.Streaming;

/// <summary>Reads bytes from a partial file only once their pieces are complete.</summary>
public static class GatedFileReader
{
    public static async Task<bool> WaitForRangeAsync(
        Func<CancellationToken, Task<PieceMap?>> pieces, long start, long endInclusive,
        TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var map = await pieces(ct);
            if (map != null && map.RangeAvailable(start, endInclusive)) return true;
            if (DateTime.UtcNow >= deadline) return false;
            await Task.Delay(500, ct);
        }
    }

    public static async Task CopyRangeAsync(string path, long start, long length, Stream output, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        fs.Seek(start, SeekOrigin.Begin);
        var buf = new byte[81920];
        var left = length;
        while (left > 0)
        {
            var n = await fs.ReadAsync(buf.AsMemory(0, (int)Math.Min(buf.Length, left)), ct);
            if (n == 0) break;
            await output.WriteAsync(buf.AsMemory(0, n), ct);
            left -= n;
        }
    }
}
