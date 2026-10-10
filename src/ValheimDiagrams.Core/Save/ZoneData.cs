using System.IO.Compression;

namespace ValheimDiagrams.Core;

// The ZoneSystem part of a world's _main.<n>.db2: which 64 m zones have ever been generated (that is,
// explored by someone), the world's global keys, and every placed location (boss altars, traders, ...).
public sealed record ZoneData(IReadOnlyList<(short X, short Z)> ExploredZones, IReadOnlyList<string> GlobalKeys, IReadOnlyList<ZoneData.Location> Locations)
{
    public const float ZoneSize = 64;

    public readonly record struct Location(int PrefabHash, float X, float Y, float Z);

    // ZNet.LoadWorld: int version, double netTime, then ZoneSystem.Load: int length + gzip block.
    public static ZoneData Read(Stream db2)
    {
        using var file = new BinaryReader(db2);
        file.ReadInt32();
        file.ReadDouble();
        var length = file.ReadInt32();
        using var gzip = new GZipStream(new MemoryStream(file.ReadBytes(length)), CompressionMode.Decompress);
        using var r = new BinaryReader(gzip);

        var zones = new (short, short)[r.ReadInt32()];
        for (var i = 0; i < zones.Length; i++) zones[i] = (r.ReadInt16(), r.ReadInt16());

        r.ReadInt32(); // location version
        var keys = new string[r.ReadInt32()];
        for (var i = 0; i < keys.Length; i++) keys[i] = r.ReadString();

        r.ReadBoolean(); // locations generated
        var locations = new Location[r.ReadInt32()];
        for (var i = 0; i < locations.Length; i++)
        {
            locations[i] = new Location(r.ReadInt32(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            r.ReadBoolean(); // placed in the world yet
        }
        return new ZoneData(zones, keys, locations);
    }

    // Zone of a world position, as ZoneSystem.GetZone: zones are centred on multiples of 64 m.
    public static (int X, int Z) ZoneOf(double x, double z) =>
        ((int)Math.Floor((x + ZoneSize / 2) / ZoneSize), (int)Math.Floor((z + ZoneSize / 2) / ZoneSize));

    // Zones packed as pairs of little-endian shorts, for storing with a snapshot.
    public static byte[] Pack(IEnumerable<(short X, short Z)> zones)
    {
        var list = zones.ToList();
        var bytes = new byte[list.Count * 4];
        for (var i = 0; i < list.Count; i++)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 4), list[i].X);
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 4 + 2), list[i].Z);
        }
        return bytes;
    }

    public static HashSet<(int X, int Z)> Unpack(byte[] bytes)
    {
        var set = new HashSet<(int, int)>(bytes.Length / 4);
        for (var i = 0; i + 3 < bytes.Length; i += 4)
            set.Add((BitConverter.ToInt16(bytes, i), BitConverter.ToInt16(bytes, i + 2)));
        return set;
    }
}

// Checks the biome generator against a save: every location may only be placed in certain biomes,
// so the share that land in an allowed biome says whether this game version still generates biomes
// the way BiomeGenerator does. 1.0.17 scores 100%.
public static class LocationCheck
{
    static readonly Lazy<Dictionary<int, Biome>> AllowedByHash = new(Load);

    public static (int Checked, int Matched) Run(BiomeGenerator biomes, IEnumerable<ZoneData.Location> locations)
    {
        int checkedCount = 0, matched = 0;
        foreach (var l in locations)
        {
            if (!AllowedByHash.Value.TryGetValue(l.PrefabHash, out var allowed)) continue;
            checkedCount++;
            if ((biomes.GetBiome(l.X, l.Z) & allowed) != 0) matched++;
        }
        return (checkedCount, matched);
    }

    // location-biomes.txt: "<location prefab>\t<Biome>,<Biome>", from the Jötunn location list.
    static Dictionary<int, Biome> Load()
    {
        using var stream = typeof(LocationCheck).Assembly.GetManifestResourceStream("ValheimDiagrams.Core.location-biomes.txt")
            ?? throw new InvalidOperationException("location-biomes.txt is not embedded");
        using var reader = new StreamReader(stream);
        var map = new Dictionary<int, Biome>();
        while (reader.ReadLine() is { } line)
        {
            var parts = line.Split('\t');
            if (parts.Length != 2) continue;
            var biomes = parts[1].Split(',').Aggregate(Biome.None, (acc, b) => Enum.TryParse<Biome>(b, out var v) ? acc | v : acc);
            map.TryAdd(StableHash.Of(parts[0]), biomes);
        }
        return map;
    }
}
