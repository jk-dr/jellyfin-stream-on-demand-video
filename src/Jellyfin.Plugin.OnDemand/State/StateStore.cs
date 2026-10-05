using System.Collections.Concurrent;
using System.Text.Json;

namespace Jellyfin.Plugin.OnDemand.State;

public class StateStore
{
    private readonly string _path;
    private readonly ConcurrentDictionary<int, MovieState> _items = new();
    private readonly object _writeLock = new();

    public StateStore(string path)
    {
        _path = path;
        if (File.Exists(path))
        {
            var list = JsonSerializer.Deserialize<List<MovieState>>(File.ReadAllText(path)) ?? new();
            foreach (var s in list) _items[s.TmdbId] = s;
        }
    }

    public MovieState? Get(int tmdbId) => _items.TryGetValue(tmdbId, out var s) ? s : null;

    public MovieState? FindByHash(string hash) =>
        _items.Values.FirstOrDefault(s => string.Equals(s.TorrentHash, hash, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyCollection<MovieState> All() => _items.Values.ToArray();

    public MovieState Upsert(int tmdbId, Action<MovieState> mutate)
    {
        var s = _items.GetOrAdd(tmdbId, id => new MovieState { TmdbId = id });
        lock (_writeLock)
        {
            mutate(s);
            s.UpdatedUtc = DateTime.UtcNow;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_items.Values));
        }
        return s;
    }
}
