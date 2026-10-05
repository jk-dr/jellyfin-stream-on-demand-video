namespace Jellyfin.Plugin.OnDemand.Streaming;

/// <summary>Piece state per qBittorrent: 0 missing, 1 downloading, 2 done.</summary>
public class PieceMap
{
    private readonly int[] _states;
    public long PieceSize { get; }
    public long TotalSize { get; }

    public PieceMap(int[] states, long pieceSize, long totalSize)
    {
        _states = states; PieceSize = pieceSize; TotalSize = totalSize;
    }

    public int Count => _states.Length;
    public bool IsDone(int i) => _states[i] == 2;

    public (int First, int Last) PiecesFor(long start, long endInclusive)
    {
        if (start < 0 || endInclusive < start || endInclusive >= TotalSize)
            throw new ArgumentOutOfRangeException(nameof(start));
        return ((int)(start / PieceSize), (int)(endInclusive / PieceSize));
    }

    public bool RangeAvailable(long start, long endInclusive)
    {
        var (f, l) = PiecesFor(start, endInclusive);
        for (var i = f; i <= l; i++) if (!IsDone(i)) return false;
        return true;
    }

    /// <summary>Length (bytes, from start) that is safe to serve contiguously.</summary>
    public long ContiguousFrom(long start)
    {
        var i = (int)(start / PieceSize);
        if (i >= _states.Length || !IsDone(i)) return 0;
        while (i < _states.Length && IsDone(i)) i++;
        return Math.Min(TotalSize, (long)i * PieceSize) - start;
    }

    public double DoneFraction => _states.Count(s => s == 2) / (double)Math.Max(1, _states.Length);

    public bool HeaderAndTailDone(int pieces = 1) =>
        Enumerable.Range(0, Math.Min(pieces, Count)).All(IsDone) &&
        Enumerable.Range(Math.Max(0, Count - pieces), Math.Min(pieces, Count)).All(IsDone);
}
