namespace ValheimDiagrams.Core;

// Biome map tiles for a world, computed from its seed and drawn only over explored zones.
// Level L is 2^L metres per pixel; a tile is TileSize pixels square, so 256·2^L metres.
// Tile (tx, ty) covers x ∈ [tx·S, (tx+1)·S) and z ∈ [ty·S, (ty+1)·S), with north at the top of the image.
public sealed class TerrainRenderer(BiomeGenerator biomes, IReadOnlySet<(int X, int Z)> explored)
{
    public const int TileSize = 256;
    public const int MaxLevel = 6;

    public static double TileMetres(int level) => TileSize * Math.Pow(2, level);

    // The colours Valheim's own map uses, lightened a little where they'd hide the builds. 0xRRGGBB.
    public static readonly IReadOnlyDictionary<Biome, uint> Colours = new Dictionary<Biome, uint>
    {
        [Biome.Meadows] = 0x92A75C,
        [Biome.BlackForest] = 0x6B743F,
        [Biome.Swamp] = 0xA37258,
        [Biome.Mountain] = 0xE6E8EC,
        [Biome.Plains] = 0xE7D171,
        [Biome.Mistlands] = 0x77737F,
        [Biome.AshLands] = 0xB03131,
        [Biome.DeepNorth] = 0xD9D9FF,
        [Biome.Ocean] = 0x4F7FAE,
    };

    // The save also lists a few zones far outside the map (the game parks things there); they're ignored.
    readonly (int X, int Z)[] _zones = [.. explored.Where(z =>
        Math.Abs(z.X * ZoneData.ZoneSize) <= BiomeGenerator.WorldRadius + ZoneData.ZoneSize &&
        Math.Abs(z.Z * ZoneData.ZoneSize) <= BiomeGenerator.WorldRadius + ZoneData.ZoneSize)];

    // Tiles at a level that contain at least one explored zone.
    public HashSet<(int X, int Y)> Index(int level)
    {
        var size = TileMetres(level);
        var half = ZoneData.ZoneSize / 2;
        var tiles = new HashSet<(int, int)>();
        foreach (var (zx, zz) in _zones)
        {
            double x0 = zx * ZoneData.ZoneSize - half, z0 = zz * ZoneData.ZoneSize - half;
            for (var tx = (int)Math.Floor(x0 / size); tx <= (int)Math.Floor((x0 + ZoneData.ZoneSize - 0.001) / size); tx++)
            for (var ty = (int)Math.Floor(z0 / size); ty <= (int)Math.Floor((z0 + ZoneData.ZoneSize - 0.001) / size); ty++)
                tiles.Add((tx, ty));
        }
        return tiles;
    }

    // RGBA pixels (TileSize², rows from the north edge), transparent where unexplored; null if all of it is.
    public byte[]? Draw(int level, int tx, int ty)
    {
        double mpp = Math.Pow(2, level), size = TileMetres(level);
        const double radius2 = BiomeGenerator.WorldRadius * (double)BiomeGenerator.WorldRadius;
        var rgba = new byte[TileSize * TileSize * 4];
        var any = false;
        for (var j = 0; j < TileSize; j++)
        {
            var z = (ty + 1) * size - (j + 0.5) * mpp;
            for (var i = 0; i < TileSize; i++)
            {
                var x = tx * size + (i + 0.5) * mpp;
                if (!explored.Contains(ZoneData.ZoneOf(x, z)) || x * x + z * z > radius2) continue;
                var c = Colours[biomes.GetBiome((float)x, (float)z)];
                var o = (j * TileSize + i) * 4;
                (rgba[o], rgba[o + 1], rgba[o + 2], rgba[o + 3]) = ((byte)(c >> 16), (byte)(c >> 8), (byte)c, 255);
                any = true;
            }
        }
        return any ? rgba : null;
    }
}
