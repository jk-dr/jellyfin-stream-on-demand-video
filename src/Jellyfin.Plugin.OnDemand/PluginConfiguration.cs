using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.OnDemand;

public class PluginConfiguration : BasePluginConfiguration
{
    public string SeerrUrl { get; set; } = "http://seerr:5055";
    public string SeerrApiKey { get; set; } = string.Empty;
    public string QbitUrl { get; set; } = "http://qbittorrent:8080";
    public string QbitUsername { get; set; } = "admin";
    public string QbitPassword { get; set; } = string.Empty;
    public string RadarrUrl { get; set; } = "http://radarr:7878";
    public string RadarrApiKey { get; set; } = string.Empty;

    /// <summary>Shared secret Radarr's webhook must send (?token=...).</summary>
    public string WebhookToken { get; set; } = Guid.NewGuid().ToString("N");

    // Buffer gate (plan: 5% of file, 1.5x average bitrate, header+tail pieces).
    public double MinBufferFraction { get; set; } = 0.05;
    public double MinSpeedFactor { get; set; } = 1.5;
    public int DeadSwarmSeconds { get; set; } = 45;
    public int RetentionDays { get; set; } = 30;
}
