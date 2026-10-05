namespace Jellyfin.Plugin.OnDemand.Streaming;

public record GateInput(PieceMap Pieces, double DownloadBytesPerSec, double DurationSeconds);

public record GateResult(bool Open, bool HeaderTail, bool Fraction, bool Speed, double Progress);

public static class BufferGate
{
    public static GateResult Evaluate(GateInput i, double minFraction, double speedFactor)
    {
        var headerTail = i.Pieces.HeaderAndTailDone();
        var fraction = i.Pieces.DoneFraction >= minFraction;
        var avgBitrate = i.DurationSeconds > 0 ? i.Pieces.TotalSize / i.DurationSeconds : 0;
        var speed = avgBitrate > 0 && i.DownloadBytesPerSec >= avgBitrate * speedFactor;
        var progress = Math.Min(1.0, i.Pieces.DoneFraction / minFraction);
        return new GateResult(headerTail && fraction && speed, headerTail, fraction, speed, progress);
    }
}
