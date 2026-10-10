using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using ValheimDiagrams.Core;
using ValheimDiagrams.Shared;

namespace ValheimDiagrams.Client.Services;

// Every world the user has imported, kept in the browser (IndexedDB via js/world-store.js) and
// queried in memory. Nothing leaves the browser: saves are read here with the Core library.
public sealed class WorldStore(IJSRuntime js) : IAsyncDisposable
{
    // More shapes than this and the browser's SVG gets slow; the window is cut short and says so.
    public const int MaxPiecesPerWindow = 10_000;

    IJSObjectReference? _module;
    readonly Dictionary<string, OpenWorld> _open = new(StringComparer.Ordinal);

    sealed class OpenWorld(StoredSummary summary, LoadedWorld world, List<SavedViewDto> views)
    {
        public StoredSummary Summary { get; } = summary;
        public LoadedWorld World { get; } = world;
        public List<SavedViewDto> Views { get; } = views;
        public List<BuildDto> Builds { get; } = BuildFinder.Find([.. world.Pieces.Select(p =>
            new BuildFinder.Piece(p.X, p.Y, p.Z, PieceCatalog.Get(world.Prefabs[p.PrefabIndex]).DisplayName))]);
        public TerrainRenderer Terrain { get; } = new(new WorldGenerator(world.Meta.Seed, world.Meta.WorldGenVersion), world.Explored);
        public Dictionary<int, HashSet<(int X, int Y)>> TerrainIndex { get; } = [];
        public Dictionary<(int, int, int), string?> TileUrls { get; } = [];
        public bool StaleTilesRemoved { get; set; }
    }

    // The demo world, shipped with the site in wwwroot/demo-world (made by tools/MakeDemo) and opened under
    // this name. It's never stored as one of the visitor's worlds, but its views and tiles are kept as
    // theirs are.
    public const string DemoKey = "(demo)";

    async Task<IJSObjectReference> Module() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/world-store.js");

    // --- Worlds ---

    public async Task<IReadOnlyList<WorldDto>> ListAsync()
    {
        var m = await Module();
        var json = await m.InvokeAsync<string[]>("values", "summaries");
        return [.. json.Select(j => JsonSerializer.Deserialize(j, SnapshotJson.Default.StoredSummary)!)
            .OrderBy(s => s.WorldName, StringComparer.OrdinalIgnoreCase)
            .Select(s => ToDto(s, null))];
    }

    // The demo's summary, or null if this site has no demo.
    public async Task<WorldDto?> GetDemoAsync() =>
        await (await Module()).InvokeAsync<string?>("fetchText", "demo-world/summary.json") is { } json
            ? ToDto(JsonSerializer.Deserialize(json, SnapshotJson.Default.StoredSummary)!, null)
            : null;

    public async Task<WorldDto?> GetWorldAsync(string name) =>
        await Open(name) is { } w ? ToDto(w.Summary, w) : null;

    public async Task<IReadOnlyList<BuildDto>> GetBuildsAsync(string name) =>
        (await Open(name))?.Builds ?? [];

    // The pieces inside an area as a diagram; interior asks for the dungeon interiors above it instead.
    public async Task<WindowDto?> GetWindowAsync(string name, AreaDto a, bool interior = false)
    {
        if (await Open(name) is not { } w) return null;
        var pieces = w.World.Pieces
            .Where(p => p.X >= a.MinX && p.X <= a.MaxX && p.Z >= a.MinZ && p.Z <= a.MaxZ
                        && (interior ? p.Y > BuildFinder.InteriorHeight : p.Y <= BuildFinder.InteriorHeight))
            .Take(MaxPiecesPerWindow + 1)
            .ToList();
        var truncated = pieces.Count > MaxPiecesPerWindow;
        if (truncated) pieces.RemoveAt(pieces.Count - 1);
        return WindowDiagram.Build(w.World, a, pieces, truncated);
    }

    public async Task DeleteWorldAsync(string name)
    {
        var m = await Module();
        foreach (var store in new[] { "summaries", "worlds", "views" }) await m.InvokeVoidAsync("remove", store, name);
        await m.InvokeVoidAsync("removePrefix", "tiles", name + "|");
        _open.Remove(name);
    }

    // Reads a picked world folder in the browser and stores the result, replacing any earlier import.
    public async Task<ImportResultDto> ImportAsync(IReadOnlyList<IBrowserFile> picked, IProgress<LoadProgress>? progress = null)
    {
        var files = picked.Where(f => SaveFolder.IsSaveFile(f.Name)).ToList();
        var contents = new Dictionary<string, byte[]>();
        for (var i = 0; i < files.Count; i++)
        {
            progress?.Report(new LoadProgress(i, files.Count, "Reading files"));
            using var ms = new MemoryStream((int)files[i].Size);
            await using (var stream = files[i].OpenReadStream(maxAllowedSize: 1L << 30)) await stream.CopyToAsync(ms);
            contents[files[i].Name] = ms.ToArray();
        }

        var save = new SaveFolder(contents);
        var world = await WorldLoader.LoadAsync(save, progress, () => Task.Delay(1));

        // The metadata file's modified time is when the game last saved the world.
        var saved = files.Where(f => f.Name == save.Latest("fwl2")).Select(f => f.LastModified.UtcDateTime).FirstOrDefault();
        var summary = StoredSummary.Of(world, saved, DateTime.UtcNow);

        progress?.Report(new LoadProgress(1, 1, "Saving in the browser"));
        var m = await Module();
        await m.InvokeVoidAsync("removePrefix", "tiles", summary.WorldName + "|");
        await m.InvokeVoidAsync("put", "worlds", summary.WorldName, JsonSerializer.Serialize(StoredWorld.From(world), SnapshotJson.Default.StoredWorld));
        await m.InvokeVoidAsync("put", "summaries", summary.WorldName, JsonSerializer.Serialize(summary, SnapshotJson.Default.StoredSummary));

        _open[summary.WorldName] = new OpenWorld(summary, world, await LoadViews(summary.WorldName));
        return new ImportResultDto(0, world.Meta.WorldName, world.ChunkFiles, world.ObjectsRead, world.Pieces.Count, []);
    }

    async Task<OpenWorld?> Open(string name)
    {
        if (_open.TryGetValue(name, out var open)) return open;
        var m = await Module();
        var (summaryJson, worldJson) = name == DemoKey
            ? (await m.InvokeAsync<string?>("fetchText", "demo-world/summary.json"), await m.InvokeAsync<string?>("fetchText", "demo-world/world.json"))
            : (await m.InvokeAsync<string?>("get", "summaries", name), await m.InvokeAsync<string?>("get", "worlds", name));
        if (summaryJson is null || worldJson is null) return null;
        var summary = JsonSerializer.Deserialize(summaryJson, SnapshotJson.Default.StoredSummary)!;
        var world = JsonSerializer.Deserialize(worldJson, SnapshotJson.Default.StoredWorld)!.ToLoadedWorld();
        return _open[name] = new OpenWorld(summary, world, await LoadViews(name));
    }

    // --- Saved views ---

    async Task<List<SavedViewDto>> LoadViews(string name) =>
        await (await Module()).InvokeAsync<string?>("get", "views", name) is { } json
            ? JsonSerializer.Deserialize(json, StoreJson.Default.ListSavedViewDto)!
            : [];

    // Saving under an existing name moves that view.
    public async Task<SavedViewDto> SaveViewAsync(string world, string viewName, AreaDto area)
    {
        var w = await Open(world) ?? throw new InvalidOperationException($"No world called {world}");
        var name = viewName.Trim();
        var existing = w.Views.FindIndex(v => v.Name == name);
        var view = new SavedViewDto(existing >= 0 ? w.Views[existing].Id : w.Views.Select(v => v.Id).DefaultIfEmpty().Max() + 1, name, area);
        if (existing >= 0) w.Views[existing] = view; else w.Views.Add(view);
        w.Views.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        await SaveViews(world, w.Views);
        return view;
    }

    public async Task DeleteViewAsync(string world, int id)
    {
        if (await Open(world) is not { } w) return;
        w.Views.RemoveAll(v => v.Id == id);
        await SaveViews(world, w.Views);
    }

    async Task SaveViews(string world, List<SavedViewDto> views) =>
        await (await Module()).InvokeVoidAsync("put", "views", world, JsonSerializer.Serialize(views, StoreJson.Default.ListSavedViewDto));

    // --- Terrain ---

    public HashSet<(int X, int Y)> TerrainIndex(string world, int level)
    {
        if (!_open.TryGetValue(world, out var w)) return [];
        if (!w.TerrainIndex.TryGetValue(level, out var index)) w.TerrainIndex[level] = index = w.Terrain.Index(level);
        return index;
    }

    // An object URL for a terrain tile: from memory, from the browser's tile store, or drawn now.
    // Null when nothing in the tile has been explored.
    public async Task<string?> TileUrlAsync(string world, int level, int tx, int ty)
    {
        if (!_open.TryGetValue(world, out var w)) return null;
        if (w.TileUrls.TryGetValue((level, tx, ty), out var url)) return url;
        // Tiles are kept per import and renderer version; older ones are cleared out once.
        var prefix = $"{world}|{w.Summary.ImportedUtc.Ticks}|v{TerrainRenderer.Version}|";
        var m = await Module();
        if (!w.StaleTilesRemoved)
        {
            w.StaleTilesRemoved = true;
            await m.InvokeVoidAsync("removeStale", "tiles", world + "|", prefix);
        }
        var key = $"{prefix}{level}|{tx}|{ty}";
        url = await m.InvokeAsync<string?>("storedTileUrl", key);
        if (url is null && w.Terrain.Draw(level, tx, ty) is { } rgba)
            url = await m.InvokeAsync<string>("tileUrl", key, rgba, TerrainRenderer.TileSize);
        return w.TileUrls[(level, tx, ty)] = url;
    }

    static WorldDto ToDto(StoredSummary s, OpenWorld? w)
    {
        AreaDto? extent = null;
        if (w is { World.Pieces.Count: > 0 })
        {
            var p = w.World.Pieces;
            extent = new AreaDto(p.Min(x => x.X), p.Max(x => x.X), p.Min(x => x.Z), p.Max(x => x.Z));
        }
        return new WorldDto(0, s.WorldName, s.SeedName, "browser", s.SavedUtc, s.ImportedUtc, s.PieceCount, extent,
            w?.Views ?? [], s.HasTerrain, s.BiomeCheckLocations, s.BiomeCheckMatched);
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null) await _module.DisposeAsync();
    }
}

// Source-generated so it survives trimming in the published (AOT) app.
[JsonSerializable(typeof(List<SavedViewDto>))]
internal partial class StoreJson : JsonSerializerContext;
