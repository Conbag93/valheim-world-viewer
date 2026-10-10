using System.Text.RegularExpressions;

namespace ValheimDiagrams.Core;

// The files of one Valheim 1.0 world save, held in memory: the folder named after the world, with
// _main.<n>.fwl2 (metadata), _main.<n>.db2 (zones, locations) and the *.chunk files (objects).
// In the browser these come from a picked folder; elsewhere from FromDirectory.
public sealed partial class SaveFolder(IReadOnlyDictionary<string, byte[]> files)
{
    public IReadOnlyDictionary<string, byte[]> Files { get; } = files;

    // Only the files a world save is made of; anything else in a picked folder is ignored.
    public static bool IsSaveFile(string name) =>
        name.EndsWith(".chunk", StringComparison.Ordinal) || name.StartsWith("_main.", StringComparison.Ordinal);

    public static SaveFolder FromDirectory(string path) => new(
        Directory.EnumerateFiles(path).Where(f => IsSaveFile(Path.GetFileName(f)))
            .ToDictionary(f => Path.GetFileName(f), File.ReadAllBytes));

    public string? Latest(string extension) => Files.Keys
        .Select(name => (name, m: MainFile().Match(name)))
        .Where(x => x.m.Success && x.m.Groups["ext"].Value == extension)
        .OrderByDescending(x => int.Parse(x.m.Groups["n"].Value))
        .Select(x => x.name)
        .FirstOrDefault();

    public Stream Open(string name) => new MemoryStream(Files[name], writable: false);

    [GeneratedRegex(@"^_main\.(?<n>\d+)\.(?<ext>\w+)$")]
    private static partial Regex MainFile();
}

public sealed record WorldMeta(string WorldName, string SeedName, int Seed, int WorldGenVersion);

// One player-built object. Valheim's axes: +X east, +Y up, +Z north; RotY is the facing in degrees.
public readonly record struct WorldPiece(int Id, int PrefabIndex, float X, float Y, float Z, float RotY);

// Everything the viewer needs from a save, after reading it.
public sealed class LoadedWorld
{
    public required WorldMeta Meta { get; init; }

    // Prefab names, indexed by WorldPiece.PrefabIndex.
    public required IReadOnlyList<string> Prefabs { get; init; }
    public required IReadOnlyList<WorldPiece> Pieces { get; init; }

    // Every 64 m zone anyone has explored (from _main.db2), as ZoneData.ZoneOf keys.
    public required IReadOnlySet<(int X, int Z)> Explored { get; init; }
    public required IReadOnlyList<string> GlobalKeys { get; init; }

    public int ChunkFiles { get; init; }
    public int ObjectsRead { get; init; }

    // How many of the save's locations sit in a biome they're allowed in, by BiomeGenerator:
    // all of them if the generator still matches the game.
    public int BiomeCheckLocations { get; init; }
    public int BiomeCheckMatched { get; init; }
    public int UnknownPrefabs { get; init; }
}

public readonly record struct LoadProgress(int Done, int Total, string Step);

public static class WorldLoader
{
    // Reads a save. pause, when given, is awaited between chunk files so a single-threaded host
    // (the browser) can repaint and report progress.
    public static async Task<LoadedWorld> LoadAsync(SaveFolder save, IProgress<LoadProgress>? progress = null, Func<Task>? pause = null, CancellationToken ct = default)
    {
        var metaName = save.Latest("fwl2")
            ?? throw new InvalidDataException("This isn't a Valheim 1.0 world save: there's no _main.*.fwl2 file. Pick the folder named after the world.");
        var meta = ReadMeta(save.Open(metaName))
            ?? throw new InvalidDataException("This world was saved before Valheim 1.0. Load it in the game once and it's converted.");

        var chunks = ChunkReader.LatestChunkFiles(save.Files.Keys).ToList();
        if (chunks.Count == 0) throw new InvalidDataException($"“{meta.WorldName}” has no .chunk files.");

        var prefabs = new List<string>();
        var prefabIndex = new Dictionary<int, int>();
        var pieces = new List<WorldPiece>();
        int objects = 0, unknown = 0;
        for (var i = 0; i < chunks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new LoadProgress(i, chunks.Count, "Reading chunk files"));
            if (pause is not null) await pause();

            var zdos = ChunkReader.Read(save.Open(chunks[i]), chunks[i]);
            objects += zdos.Count;
            foreach (var z in zdos)
            {
                if (z.Creator is null) continue;
                if (!prefabIndex.TryGetValue(z.Prefab, out var index))
                {
                    var name = PrefabNames.Find(z.Prefab);
                    if (name is null) unknown++;
                    prefabIndex[z.Prefab] = index = prefabs.Count;
                    prefabs.Add(name ?? $"#{z.Prefab}");
                }
                pieces.Add(new WorldPiece(pieces.Count + 1, index, z.X, z.Y, z.Z, z.RotY));
            }
        }

        progress?.Report(new LoadProgress(chunks.Count, chunks.Count, "Checking biomes"));
        if (pause is not null) await pause();
        var zones = save.Latest("db2") is { } db2 ? ZoneData.Read(save.Open(db2)) : null;
        var (checkedLocations, matched) = zones is null ? (0, 0)
            : LocationCheck.Run(new BiomeGenerator(meta.Seed, meta.WorldGenVersion), zones.Locations);

        return new LoadedWorld
        {
            Meta = meta,
            Prefabs = prefabs,
            Pieces = pieces,
            Explored = zones?.ExploredZones.Select(z => ((int)z.X, (int)z.Z)).ToHashSet() ?? [],
            GlobalKeys = zones?.GlobalKeys ?? [],
            ChunkFiles = chunks.Count,
            ObjectsRead = objects,
            BiomeCheckLocations = checkedLocations,
            BiomeCheckMatched = matched,
            UnknownPrefabs = unknown,
        };
    }

    // World.LoadWorld: int length, int version, string name, string seed name, int seed, long uid, int worldgen version, ...
    // Null for worlds older than the chunked save format.
    public static WorldMeta? ReadMeta(Stream fwl2)
    {
        using var reader = new BinaryReader(fwl2);
        reader.ReadInt32();
        if (reader.ReadInt32() < ChunkReader.SupportedVersion) return null;
        var name = reader.ReadString();
        var seedName = reader.ReadString();
        var seed = reader.ReadInt32();
        reader.ReadInt64();
        return new WorldMeta(name, seedName, seed, reader.ReadInt32());
    }
}
