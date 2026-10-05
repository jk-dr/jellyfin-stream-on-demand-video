using Jellyfin.Plugin.OnDemand.Seerr;
using Jellyfin.Plugin.OnDemand.Streaming;
using Xunit;

public class GateTests
{
    private static PieceMap Map(string s, long ps = 1000) =>
        new(s.Select(c => c == '#' ? 2 : 0).ToArray(), ps, s.Length * ps);

    [Fact] public void RangeNeedsAllPieces()
    {
        var m = Map("##..#");
        Assert.True(m.RangeAvailable(0, 1999));
        Assert.False(m.RangeAvailable(1500, 2500));
        Assert.True(m.RangeAvailable(4000, 4999));
    }

    [Fact] public void ContiguousStopsAtHole() =>
        Assert.Equal(2000, Map("##.##").ContiguousFrom(0));

    [Fact] public void ContiguousZeroWhenMissing() =>
        Assert.Equal(0, Map("#.###").ContiguousFrom(1000));

    [Fact] public void GateOpensOnlyWhenAllConditionsHold()
    {
        // 100 pieces, 6 done incl. head+tail -> 6% >= 5%
        var s = "#####" + new string('.', 94) + "#";
        var m = Map(s, 1_000_000); // 100 MB over 7200 s => ~13.9 KB/s avg bitrate
        Assert.True(BufferGate.Evaluate(new GateInput(m, 100_000, 7200), 0.05, 1.5).Open);
        Assert.False(BufferGate.Evaluate(new GateInput(m, 1_000, 7200), 0.05, 1.5).Open);   // too slow
        var noTail = "######" + new string('.', 94);
        Assert.False(BufferGate.Evaluate(new GateInput(Map(noTail, 1_000_000), 100_000, 7200), 0.05, 1.5).Open);
        var tooLittle = "#" + new string('.', 98) + "#";
        Assert.False(BufferGate.Evaluate(new GateInput(Map(tooLittle, 1_000_000), 100_000, 7200), 0.05, 1.5).Open);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(2, true)] [InlineData(128, true)] [InlineData(256, true)] [InlineData(32, false)]
    public void AutoApprove(int perms, bool expected) =>
        Assert.Equal(expected, SeerrPermissions.AutoApprovesMovies(perms));
}
