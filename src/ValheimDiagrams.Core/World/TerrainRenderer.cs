namespace ValheimDiagrams.Core;

// Biome map tiles for a world, computed from its seed and drawn only over explored zones.
// Level L is 2^L metres per pixel; a tile is TileSize pixels square, so 256·2^L metres.
// Tile (tx, ty) covers x ∈ [tx·S, (tx+1)·S) and z ∈ [ty·S, (ty+1)·S), with north at the top of the image.
public sealed class TerrainRenderer(BiomeGenerator biomes, IReadOnlySet<(int X, int Z)> explored)
{
    public const int TileSize = 256;
    public const int MaxLevel = 6;

    // Bumped whenever tiles would come out differently, so ones stored in the browser are redrawn.
    public const int Version = 4;

    public static double TileMetres(int level) => TileSize * Math.Pow(2, level);

    // The colours Valheim's own map uses, lightened a little where they'd hide the builds, except
    // mountains: grey rather than the game's white, which read as blank paper. 0xRRGGBB.
    public static readonly IReadOnlyDictionary<Biome, uint> Colours = new Dictionary<Biome, uint>
    {
        [Biome.Meadows] = 0x92A75C,
        [Biome.BlackForest] = 0x6B743F,
        [Biome.Swamp] = 0xA37258,
        [Biome.Mountain] = 0xA9ACB1,
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
    // Biomes are looked up on a grid every Step pixels; a cell whose corners agree is filled with that
    // biome, and only cells on a border are looked up pixel by pixel.
    public byte[]? Draw(int level, int tx, int ty)
    {
        const int Step = 8, n = TileSize / Step + 1;
        double mpp = Math.Pow(2, level), size = TileMetres(level);
        const double radius2 = BiomeGenerator.WorldRadius * (double)BiomeGenerator.WorldRadius;
        var grid = new Biome[n * n];
        for (var b = 0; b < n; b++)
        for (var a = 0; a < n; a++)
        {
            var (x, z) = Pixel(a * Step, b * Step);
            grid[b * n + a] = biomes.GetBiome((float)x, (float)z);
        }

        var rgba = new byte[TileSize * TileSize * 4];
        var any = false;
        for (var j = 0; j < TileSize; j++)
        for (var i = 0; i < TileSize; i++)
        {
            var (x, z) = Pixel(i, j);
            if (!explored.Contains(ZoneData.ZoneOf(x, z)) || x * x + z * z > radius2) continue;
            var k = j / Step * n + i / Step;
            var biome = grid[k] == grid[k + 1] && grid[k] == grid[k + n] && grid[k] == grid[k + n + 1]
                ? grid[k]
                : biomes.GetBiome((float)x, (float)z);
            var c = Colours[biome];
            var o = (j * TileSize + i) * 4;
            (rgba[o], rgba[o + 1], rgba[o + 2], rgba[o + 3]) = ((byte)(c >> 16), (byte)(c >> 8), (byte)c, 255);
            any = true;
        }
        return any ? rgba : null;

        // Centre of pixel (i, j): column i from the west, row j from the north. Grid points at i or
        // j = TileSize fall just past the tile's edge.
        (double X, double Z) Pixel(int i, int j) => (tx * size + (i + 0.5) * mpp, (ty + 1) * size - (j + 0.5) * mpp);
    }
}
