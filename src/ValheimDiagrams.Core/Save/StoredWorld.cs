using System.Text.Json.Serialization;

namespace ValheimDiagrams.Core;

// A world as the viewer keeps it: what the browser stores after an import, and what the demo ships as.
// Only what the viewer draws: piece names and positions, explored zones and the seed. None of the
// save's player names, sign text or portal tags.

public sealed record StoredSummary(string WorldName, string SeedName, int PieceCount, DateTime SavedUtc, DateTime ImportedUtc,
    bool HasTerrain, int BiomeCheckLocations, int BiomeCheckMatched)
{
    public static StoredSummary Of(LoadedWorld w, DateTime savedUtc, DateTime importedUtc) => new(w.Meta.WorldName, w.Meta.SeedName,
        w.Pieces.Count, savedUtc, importedUtc, w.Explored.Count > 0, w.BiomeCheckLocations, w.BiomeCheckMatched);
}

// A LoadedWorld as stored in IndexedDB: pieces as parallel arrays to keep the JSON compact.
public sealed class StoredWorld
{
    public int Format { get; set; } = 1;
    public required WorldMeta Meta { get; set; }
    public required string[] Prefabs { get; set; }
    public required int[] PrefabIndex { get; set; }
    public required float[] X { get; set; }
    public required float[] Y { get; set; }
    public required float[] Z { get; set; }
    public required float[] RotY { get; set; }
    public required int[] Explored { get; set; }
    public required string[] GlobalKeys { get; set; }
    public int ChunkFiles { get; set; }
    public int ObjectsRead { get; set; }
    public int BiomeCheckLocations { get; set; }
    public int BiomeCheckMatched { get; set; }
    public int UnknownPrefabs { get; set; }

    public static StoredWorld From(LoadedWorld w) => new()
    {
        Meta = w.Meta,
        Prefabs = [.. w.Prefabs],
        PrefabIndex = [.. w.Pieces.Select(p => p.PrefabIndex)],
        X = [.. w.Pieces.Select(p => p.X)],
        Y = [.. w.Pieces.Select(p => p.Y)],
        Z = [.. w.Pieces.Select(p => p.Z)],
        RotY = [.. w.Pieces.Select(p => p.RotY)],
        Explored = [.. w.Explored.SelectMany(z => new[] { z.X, z.Z })],
        GlobalKeys = [.. w.GlobalKeys],
        ChunkFiles = w.ChunkFiles,
        ObjectsRead = w.ObjectsRead,
        BiomeCheckLocations = w.BiomeCheckLocations,
        BiomeCheckMatched = w.BiomeCheckMatched,
        UnknownPrefabs = w.UnknownPrefabs,
    };

    public LoadedWorld ToLoadedWorld() => new()
    {
        Meta = Meta,
        Prefabs = Prefabs,
        Pieces = [.. Enumerable.Range(0, PrefabIndex.Length).Select(i => new WorldPiece(i + 1, PrefabIndex[i], X[i], Y[i], Z[i], RotY[i]))],
        Explored = Enumerable.Range(0, Explored.Length / 2).Select(i => (Explored[2 * i], Explored[2 * i + 1])).ToHashSet(),
        GlobalKeys = GlobalKeys,
        ChunkFiles = ChunkFiles,
        ObjectsRead = ObjectsRead,
        BiomeCheckLocations = BiomeCheckLocations,
        BiomeCheckMatched = BiomeCheckMatched,
        UnknownPrefabs = UnknownPrefabs,
    };
}

// Source-generated so it survives trimming in the published (AOT) app.
[JsonSerializable(typeof(StoredWorld))]
[JsonSerializable(typeof(StoredSummary))]
public partial class SnapshotJson : JsonSerializerContext;
