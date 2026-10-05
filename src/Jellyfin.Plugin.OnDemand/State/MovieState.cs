namespace Jellyfin.Plugin.OnDemand.State;

public enum MovieStatus
{
    Requested, Grabbing, Preparing, Streaming, Downloading, InLibrary, Failed, Cancelled
}

/// <summary>One record per movie keyed by TMDB id; rebuildable from Radarr/qBittorrent.</summary>
public class MovieState
{
    public int TmdbId { get; set; }
    public MovieStatus Status { get; set; } = MovieStatus.Requested;
    public string? TorrentHash { get; set; }
    public string? StubPath { get; set; }
    public int? RadarrMovieId { get; set; }
    public Guid? RequestedBy { get; set; }
    public int FailedGrabs { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
