namespace ValheimDiagrams.Core;

[Flags]
public enum Biome
{
    None = 0,
    Meadows = 1,
    Swamp = 2,
    Mountain = 4,
    BlackForest = 8,
    Plains = 16,
    AshLands = 32,
    DeepNorth = 64,
    Ocean = 256,
    Mistlands = 512,
}

// Valheim 1.0's WorldGenerator: biomes and terrain height, which aren't saved anywhere: the game
// recomputes them from the seed. Ported from the decompiled 1.0 assembly_valheim.dll, keeping its
// float/double mix so results match; research/worldgen/biome.py is the reference port of GetBiome.
// Checked on import against the save's own locations (see LocationCheck).
public sealed class WorldGenerator
{
    public const float WorldRadius = 10500;
    public const float WaterLevel = 30;
    const float HeightMultiplier = 200;

    readonly float _offset0, _offset1, _offset2, _offset3, _offset4;
    readonly int _riverSeed, _streamSeed;
    readonly float _minMountainDistance = 1000, _minDarklandNoise = 0.4f, _maxMarshDistance = 6000;

    // River and stream points by 64 m grid cell, from Pregenerate. Placing them takes a moment,
    // so it happens on first use of a height (or ahead of time, via Pregenerate / LoadRivers).
    Dictionary<(int X, int Y), RiverPoint[]>? _riverPoints;
    readonly List<RiverPoint> _riverList = [];

    public WorldGenerator(int seed, int worldGenVersion)
    {
        // WorldGenerator's constructor: Random.InitState(seed), then these draws in this order.
        var random = new UnityRandom(seed);
        _offset0 = random.Range(-10000, 10000);
        _offset1 = random.Range(-10000, 10000);
        _offset2 = random.Range(-10000, 10000);
        _offset3 = random.Range(-10000, 10000);
        _riverSeed = random.Range(int.MinValue, int.MaxValue);
        _streamSeed = random.Range(int.MinValue, int.MaxValue);
        _offset4 = random.Range(-10000, 10000);

        if (worldGenVersion <= 0) _minMountainDistance = 1500;
        if (worldGenVersion <= 1) (_minDarklandNoise, _maxMarshDistance) = (0.5f, 8000);
    }

    // --- Biomes ---

    // x east, z north, in metres.
    public Biome GetBiome(float wx, float wy)
    {
        var distance = Length(wx, wy);
        var baseHeight = GetBaseHeight(wx, wy);
        var angle = (float)(WorldAngle(wx, wy) * 100.0);

        if (IsAshlands(wx, wy)) return Biome.AshLands;
        if (baseHeight <= 0.02f) return Biome.Ocean;
        if (IsDeepNorth(wx, wy)) return Biome.DeepNorth;
        if (baseHeight > 0.4f) return Biome.Mountain;
        if (Noise(_offset0, wx, wy) > 0.6f && distance > 2000 && distance < _maxMarshDistance && baseHeight > 0.05f && baseHeight < 0.25f) return Biome.Swamp;
        if (Noise(_offset4, wx, wy) > _minDarklandNoise && distance > 6000 + angle && distance < 10000) return Biome.Mistlands;
        if (Noise(_offset1, wx, wy) > 0.4f && distance > 3000 + angle && distance < 8000) return Biome.Plains;
        if (Noise(_offset2, wx, wy) > 0.4f && distance > 600 + angle && distance < 6000) return Biome.BlackForest;
        if (distance > 5000 + angle) return Biome.BlackForest;
        return Biome.Meadows;
    }

    static bool IsAshlands(float x, float y) =>
        Length(x, (float)(y - 4000.0)) > 12000.0 + WorldAngle(x, y) * 100.0;

    static bool IsDeepNorth(float x, float y) =>
        Length(x, (float)(y + 4000.0)) > (float)(12000.0 + WorldAngle(x, y) * 100.0);

    static float Noise(float offset, float wx, float wy) =>
        Perlin((double)(float)(offset + (double)wx) * 0.001f, (double)(float)(offset + (double)wy) * 0.001f);

    // Whether trees grow here (WorldGenerator.InForest), for Meadows and Plains: < 1.15 is forest.
    public static float ForestFactor(float wx, float wy)
    {
        float x = wx * 0.01f * 0.4f, y = wy * 0.01f * 0.4f, sum = 0, amplitude = 1;
        for (var i = 0; i < 3; i++)
        {
            sum = (float)(sum + (double)amplitude * Perlin(x, y));
            amplitude = (float)(amplitude * 0.7);
            (x, y) = (x * 1.6f, y * 1.6f);
        }
        return sum;
    }

    // --- Heights ---

    // Ground height in metres above the sea floor datum; the water surface is at WaterLevel (30 m).
    public float GetHeight(float wx, float wy) => GetHeight(GetBiome(wx, wy), wx, wy);

    public float GetHeight(Biome biome, float wx, float wy) => GetBiomeHeight(biome, wx, wy, out _, preGeneration: false);

    // lava: in the Ashlands, how much of this spot is a lava field (0..1).
    public float GetHeight(Biome biome, float wx, float wy, out float lava) => GetBiomeHeight(biome, wx, wy, out lava, preGeneration: false);

    // The ground as the game builds it (HeightmapBuilder.Build): each 64 m zone takes the biomes at its
    // four corners and, where they differ, blends their heights across the zone, so there are no
    // cliffs at biome borders. lava: in the Ashlands, how much of this spot is a lava field (0..1).
    public float GetTerrainHeight(float wx, float wy, out float lava)
    {
        var (zx, zz) = ZoneData.ZoneOf(wx, wy);
        float x0 = (float)(zx * 64.0 - 32.0), z0 = (float)(zz * 64.0 - 32.0);
        Biome b00 = CornerBiome(x0, z0), b10 = CornerBiome(x0 + 64, z0), b01 = CornerBiome(x0, z0 + 64), b11 = CornerBiome(x0 + 64, z0 + 64);
        if (b00 == b10 && b00 == b01 && b00 == b11) return GetBiomeHeight(b00, wx, wy, out lava, preGeneration: false);
        var tx = SmoothStep(0, 1, (float)((wx - (double)x0) / 64.0));
        var tz = SmoothStep(0, 1, (float)((wy - (double)z0) / 64.0));
        var h00 = GetBiomeHeight(b00, wx, wy, out var l00, preGeneration: false);
        var h10 = GetBiomeHeight(b10, wx, wy, out var l10, preGeneration: false);
        var h01 = GetBiomeHeight(b01, wx, wy, out var l01, preGeneration: false);
        var h11 = GetBiomeHeight(b11, wx, wy, out var l11, preGeneration: false);
        lava = Lerp(Lerp(l00, l10, tx), Lerp(l01, l11, tx), tz);
        return Lerp(Lerp(h00, h10, tx), Lerp(h01, h11, tx), tz);
    }

    // Zone corners are shared by four zones and asked for over and over while drawing.
    readonly Dictionary<(float, float), Biome> _corners = [];

    Biome CornerBiome(float x, float z)
    {
        lock (_corners)
        {
            if (_corners.TryGetValue((x, z), out var b)) return b;
            if (_corners.Count > 200_000) _corners.Clear();
            return _corners[(x, z)] = GetBiome(x, z);
        }
    }

    float GetBiomeHeight(Biome biome, float wx, float wy, bool preGeneration, bool riverPreDN = true) =>
        GetBiomeHeight(biome, wx, wy, out _, preGeneration, riverPreDN);

    float GetBiomeHeight(Biome biome, float wx, float wy, out float lava, bool preGeneration, bool riverPreDN = true)
    {
        lava = 0;
        var m = preGeneration ? HeightMultiplier : (float)(HeightMultiplier * CreateGap(wx, wy, -4000) * CreateGap(wx, wy, 4000));
        if (Length(wx, wy) > 10500) return -2f * HeightMultiplier;
        var h = biome switch
        {
            Biome.Swamp => GetMarshHeight(wx, wy),
            Biome.DeepNorth => preGeneration ? GetDeepNorthHeightPregenerate(wx, wy, riverPreDN) : GetDeepNorthHeight(wx, wy),
            Biome.Mountain => GetSnowMountainHeight(wx, wy),
            Biome.BlackForest => GetForestHeight(wx, wy),
            Biome.Ocean => GetBaseHeight(wx, wy),
            Biome.AshLands => preGeneration ? GetAshlandsHeightPregenerate(wx, wy) : GetAshlandsHeight(wx, wy, out lava),
            Biome.Plains => GetPlainsHeight(wx, wy),
            Biome.Meadows => GetMeadowsHeight(wx, wy),
            Biome.Mistlands => preGeneration ? GetForestHeight(wx, wy) : GetMistlandsHeight(wx, wy),
            _ => 0f,
        };
        return (float)(h * (double)m);
    }

    // CreateAshlandsGap / CreateDeepNorthGap: a channel of sea between those biomes and the rest.
    static double CreateGap(float wx, float wy, float yOffset)
    {
        var d = Length(wx, wy + yOffset) - (12000.0 + WorldAngle(wx, wy) * 100.0);
        return MathfLikeSmoothStep(0, 1, (float)Clamp01(Math.Abs(d) / 400.0));
    }

    // A height asks for the base height at the same spot several times over (its biome, then each
    // biome blended in at zone borders), so the last one is remembered.
    [ThreadStatic] static (WorldGenerator? Owner, float X, float Y, float H) s_lastBase;

    float GetBaseHeight(float wx, float wy)
    {
        var last = s_lastBase;
        if (last.Owner == this && last.X == wx && last.Y == wy) return last.H;
        var h = ComputeBaseHeight(wx, wy);
        s_lastBase = (this, wx, wy, h);
        return h;
    }

    float ComputeBaseHeight(float wx, float wy)
    {
        var distance = Length(wx, wy);
        double x = wx + 100000.0 + _offset0, y = wy + 100000.0 + _offset1;
        var h = 0f;
        h = (float)(h + (double)Perlin(x * 0.002f * 0.5, y * 0.002f * 0.5) * Perlin(x * 0.003f * 0.5, y * 0.003f * 0.5) * 1.0);
        h = (float)(h + (double)Perlin(x * 0.002f, y * 0.002f) * Perlin(x * 0.003f, y * 0.003f) * h * 0.9f);
        h = (float)(h + (double)Perlin(x * 0.005f, y * 0.005f) * Perlin(x * 0.01f, y * 0.01f) * 0.5 * h);
        h = (float)(h - 0.07f);

        // River valleys carved into the base height.
        var a = Perlin(x * 0.002f * 0.25 + 0.123f, y * 0.002f * 0.25 + 0.15123f);
        var b = Perlin(x * 0.002f * 0.25 + 0.321f, y * 0.002f * 0.25 + 0.231f);
        var river = (float)(1.0 - LerpStep(0.02f, 0.12f, Math.Abs(a - b)));
        river = (float)(river * (double)SmoothStep(744, 1000, distance));
        h = (float)(h * (1.0 - river));

        if (distance > 10000)
        {
            h = Lerp(h, -0.2f, LerpStep(10000, 10500, distance));
            if (distance > 10490) h = Lerp(h, -2f, LerpStep(10490, 10500, distance));
            return h;
        }
        if (distance < _minMountainDistance && h > 0.28f)
        {
            var t = (float)Clamp01((h - 0.2800000011920929) / 0.09999999403953552);
            h = Lerp(Lerp(0.28f, 0.38f, t), h, LerpStep((float)(_minMountainDistance - 400.0), _minMountainDistance, distance));
        }
        return h;
    }

    // The detail noise every land biome adds to its base height, at the offset3 coordinates.
    static float Hills(double x, double y)
    {
        var n = (float)((double)Perlin(x * 0.01f, y * 0.01f) * Perlin(x * 0.02f, y * 0.02f));
        return (float)(n + (double)Perlin(x * 0.05f, y * 0.05f) * Perlin(x * 0.1f, y * 0.1f) * n * 0.5);
    }

    static float Detail(float h, double x, double y) =>
        (float)((float)(h + (double)Perlin(x * 0.1f, y * 0.1f) * 0.01f) + (double)Perlin(x * 0.4f, y * 0.4f) * 0.003f);

    float GetMarshHeight(float wx, float wy)
    {
        double x = (float)(wx + 100000.0), y = (float)(wy + 100000.0);
        var h = 0.137f;
        var n = (float)((double)Perlin(x * 0.04f, y * 0.04f) * Perlin(x * 0.08f, y * 0.08f));
        h = (float)(h + (double)n * 0.03f);
        h = AddRivers(wx, wy, h);
        return Detail(h, x, y);
    }

    // Meadows and Plains: hills, flattened towards 0.15 away from mountains.
    float GetLowlandHeight(float wx, float wy)
    {
        var baseHeight = GetBaseHeight(wx, wy);
        double x = (float)(wx + 100000.0 + _offset3), y = (float)(wy + 100000.0 + _offset3);
        var h = (float)(baseHeight + (double)Hills(x, y) * 0.1f);
        var over = h - 0.15f;
        var mountainness = (float)Clamp01(baseHeight / 0.4000000059604645);
        if (over > 0f) h = (float)(h - over * (1.0 - mountainness) * 0.75);
        h = AddRivers(wx, wy, h);
        return Detail(h, x, y);
    }

    float GetMeadowsHeight(float wx, float wy) => GetLowlandHeight(wx, wy);

    float GetPlainsHeight(float wx, float wy) => GetLowlandHeight(wx, wy);

    float GetForestHeight(float wx, float wy)
    {
        var h = GetBaseHeight(wx, wy);
        double x = (float)(wx + 100000.0 + _offset3), y = (float)(wy + 100000.0 + _offset3);
        h = (float)(h + (double)Hills(x, y) * 0.1f);
        h = AddRivers(wx, wy, h);
        return Detail(h, x, y);
    }

    float GetMistlandsHeight(float wx, float wy)
    {
        var h = GetBaseHeight(wx, wy);
        double x = (float)(wx + 100000.0 + _offset3), y = (float)(wy + 100000.0 + _offset3);
        var n = Perlin(x * 0.02f * 0.7f, y * 0.02f * 0.7f) * Perlin(x * 0.04f * 0.7f, y * 0.04f * 0.7f);
        n = (float)(n + (double)Perlin(x * 0.03f * 0.7f, y * 0.03f * 0.7f) * Perlin(x * 0.05f * 0.7f, y * 0.05f * 0.7f) * n * 0.5);
        n = n > 0f ? (float)Math.Pow(n, 1.5) : n;
        h = (float)(h + (double)n * 0.4f);
        h = AddRivers(wx, wy, h);
        var cliffs = (float)Clamp01(n * 7.0);
        h = (float)(h + (double)Perlin(x * 0.1f, y * 0.1f) * 0.03f * cliffs);
        h = (float)(h + (double)Perlin(x * 0.4f, y * 0.4f) * 0.01f * cliffs);
        var smooth = (float)(h + (double)Perlin(x * 0.4f, y * 0.4f) * 0.002f);
        var terraced = (float)(MathF.Ceiling((float)(h * 400.0)) / 400.0);
        return Lerp(smooth, terraced, cliffs);
    }

    float GetAshlandsHeightPregenerate(float wx, float wy)
    {
        var h = GetBaseHeight(wx, wy);
        double x = (float)(wx + 100000.0 + _offset3), y = (float)(wy + 100000.0 + _offset3);
        h = (float)(h + (double)Hills(x, y) * 0.1f);
        h = (float)(h + 0.1f);
        h = Detail(h, x, y);
        return AddRivers(wx, wy, h);
    }

    float GetAshlandsHeight(float wx, float wy, out float lava)
    {
        double x = wx, y = wy;
        double baseHeight = GetBaseHeight(wx, wy);
        var angle = WorldAngle(wx, wy) * 100.0;
        // Raised ground along the inner edge of the Ashlands, fading towards the east and west.
        var rim = Math.Abs(Length(x, y - 4000.0 + 4000.0 * 0.3) - (12000.0 + angle)) / 1000.0;
        rim = MathfLikeSmoothStep(0.1, 1.0, 1.0 - Clamp01(rim));
        rim *= 1.0 - Clamp01(Math.Abs(x) / 7500.0);
        var shore = 1.0 - Clamp01((Length(x, y) - 10150.0) / 600.0);
        x += 100000f + _offset3;
        y += 100000f + _offset3;

        double cells = 0, amplitude = 1, frequency = 0.33000001311302185;
        for (var i = 0; i < 5; i++)
        {
            cells += amplitude * MathfLikeSmoothStep(0, 1, FastNoise.Cellular(x * frequency, y * frequency));
            frequency *= 2;
            amplitude *= 0.5;
        }
        cells = Remap(cells, -1, 1, 0, 1);
        var ridges = Lerp(rim, BlendOverlay(rim, cells), 0.5);
        var h = Lerp(baseHeight, 0.15000000596046448, 0.75) + ridges * 0.5;
        h = Lerp(-1.0, h, MathfLikeSmoothStep(0, 1, shore));

        const double lavaLevel = 0.15;
        double crust = 0, crustAmplitude = 1, crustFrequency = 8;
        for (var i = 0; i < 3; i++)
        {
            crust += crustAmplitude * FastNoise.Cellular(x * crustFrequency, y * crustFrequency);
            crustFrequency *= 2;
            crustAmplitude *= 0.5;
        }
        crust = Remap(crust, -1, 1, 0, 1);
        crust = Clamp01(Math.Pow(crust, 4) * 2);
        h *= Math.Pow(Remap(FastNoise.SimplexFractal(x * 0.075, y * 0.075), -1, 1, 0, 1), 1.399999976158142);

        var fields = Fbm((float)(x * 0.01f), (float)(y * 0.01f), 3, 2.0, 0.5);
        fields *= Clamp01(Remap(rim, 0, 0.5, 0.5, 1));
        fields = Math.Pow(LerpStep(0.699999988079071, 1.0, fields), 2);
        var mask = BlendOverlay(fields, crust) * Clamp01((h - lavaLevel - 0.02) / 0.01);
        var dip = Remap(Math.Pow(Perlin(x * 0.05 + 5124.0, y * 0.05 + 5000.0), 2), 0, 1, 0.009999999776482582, 0.054999999701976776);
        var lavaHeight = Math.Clamp((float)(h - dip), (float)(lavaLevel + 0.009999999776482582), 5000f);
        h = Lerp(h, lavaHeight, mask);
        lava = (float)mask;
        return (float)h;
    }

    float BaseHeightTilt(float wx, float wy)
    {
        var l = GetBaseHeight((float)(wx - 1.0), wy);
        var r = GetBaseHeight((float)(wx + 1.0), wy);
        var d = GetBaseHeight(wx, (float)(wy - 1.0));
        var u = GetBaseHeight(wx, (float)(wy + 1.0));
        return (float)(Math.Abs((float)(r - (double)l)) + (double)Math.Abs((float)(d - (double)u)));
    }

    float GetSnowMountainHeight(float wx, float wy)
    {
        var h = GetBaseHeight(wx, wy);
        var tilt = BaseHeightTilt(wx, wy);
        double x = (float)(wx + 100000.0 + _offset3), y = (float)(wy + 100000.0 + _offset3);
        h = (float)(h + (double)(float)(h - 0.4f));
        h = (float)(h + (double)Hills(x, y) * 0.2f);
        h = AddRivers(wx, wy, h);
        h = Detail(h, x, y);
        return (float)(h + (double)Perlin(x * 0.2f, y * 0.2f) * 2.0 * tilt);
    }

    float GetDeepNorthHeightPregenerate(float wx, float wy, bool riverPregen)
    {
        var h = GetBaseHeight(wx, wy);
        if (!riverPregen) h += 0.1f;
        float fx = (float)(wx + 100000.0 + _offset3), fy = (float)(wy + 100000.0 + _offset3);
        double x = fx, y = fy;
        h = (float)(h + (double)Math.Max(0f, (float)(h - 0.4f)));
        h = (float)(h + (double)Hills(x, y) * 0.2f);
        h = (float)(h * 1.2f);
        h = AddRivers(wx, wy, h);
        h = (float)(h + (double)Perlin(fx * 0.1f, fy * 0.1f) * 0.01f);
        return (float)(h + (double)Perlin(fx * 0.4f, fy * 0.4f) * 0.003f);
    }

    float GetDeepNorthHeight(float wx, float wy)
    {
        var baseHeight = GetBaseHeight(wx, wy) + 0.1f;
        double x = (float)(wx + 100000.0 + _offset3), y = (float)(wy + 100000.0 + _offset3);
        var h = (float)(baseHeight + (double)Hills(x, y) * 0.1f);
        var over = (float)(h - 0.15f);
        var mountainness = (float)Clamp01(baseHeight / 0.4000000059604645);
        if (over > 0f) h = (float)(h - over * ((1.0 - mountainness) * 0.75));
        h = AddRivers(wx, wy, h);
        return Detail(h, x, y);
    }

    // --- Rivers ---

    readonly record struct RiverPoint(float X, float Y, float W)
    {
        public float W2 { get; } = (float)(W * (double)W);
    }

    sealed record River(V2 P0, V2 P1, float WidthMin, float WidthMax, float CurveWidth, float CurveWavelength);

    float AddRivers(float wx, float wy, float h)
    {
        GetRiverWeight(wx, wy, out var weight, out var width);
        if (weight <= 0f) return h;
        var t = LerpStep(20f, 60f, width);
        var top = Lerp(0.14f, 0.12f, t);
        var bed = Lerp(0.139f, 0.128f, t);
        if (h > top) h = Lerp(h, top, weight);
        if (h > bed) h = Lerp(h, bed, LerpStep(0.85f, 1f, weight));
        return h;
    }

    void GetRiverWeight(float wx, float wy, out float weight, out float width)
    {
        weight = width = 0;
        var points = Rivers();
        if (!points.TryGetValue(RiverGrid(wx, wy), out var cell)) return;
        float sumW = 0, sum = 0;
        foreach (var p in cell)
        {
            var dx = p.X - wx;
            var dy = p.Y - wy;
            var d2 = dx * dx + dy * dy;
            if (d2 >= p.W2) continue;
            var w = (float)(1.0 - (float)Math.Sqrt(d2) / (double)p.W);
            if (w > weight) weight = w;
            sumW = (float)(sumW + p.W * (double)w);
            sum = (float)(sum + (double)w);
        }
        if (sum > 0f) width = (float)(sumW / (double)sum);
    }

    static (int, int) RiverGrid(float wx, float wy) =>
        ((int)MathF.Floor((float)((wx + 32.0) / 64.0)), (int)MathF.Floor((float)((wy + 32.0) / 64.0)));

    Dictionary<(int X, int Y), RiverPoint[]> Rivers()
    {
        if (_riverPoints is null) Pregenerate();
        return _riverPoints!;
    }

    // Streams are 20 m wide gullies that take most of Pregenerate's time; the map leaves them out.
    // With them, heights match the game exactly (every saved location's height, in testing).
    public bool IncludeStreams { get; init; }

    // WorldGenerator.Pregenerate: lakes, the rivers between them, then streams. Streams are placed by
    // sampling heights that already include the rivers, so the order matters.
    public void Pregenerate()
    {
        if (_riverPoints is not null) return;
        _riverPoints = [];
        var random = new UnityRandom(_riverSeed);
        RenderRivers(PlaceRivers(FindLakes(), random), RiverAdd.All, random);
        if (!IncludeStreams) return;
        PlaceStreams(isDN: false);
        PlaceStreams(isDN: true);
    }

    // Every river and stream point as x, y, radius triples, for storing with a world so they needn't
    // be placed again; LoadRivers puts them back on the grid.
    public float[] SaveRivers()
    {
        Rivers();
        return [.. _riverList.SelectMany(p => new[] { p.X, p.Y, p.W })];
    }

    public void LoadRivers(float[] saved)
    {
        _riverPoints = [];
        _riverList.Clear();
        var added = new Dictionary<(int, int), List<RiverPoint>>();
        for (var i = 0; i + 2 < saved.Length; i += 3) AddRiverPoint(added, new V2(saved[i], saved[i + 1]), saved[i + 2]);
        AddToGrid(added);
    }

    List<V2> FindLakes()
    {
        var points = new List<V2>();
        for (var y = -10000f; y <= 10000f; y = (float)(y + 128.0))
        for (var x = -10000f; x <= 10000f; x = (float)(x + 128.0))
            if (!(new V2(x, y).Magnitude > 10000f) && GetBaseHeight(x, y) < 0.05f)
                points.Add(new V2(x, y));
        return MergePoints(points, 800);
    }

    // The game's MergePoints: take the first point, repeatedly pull in its closest neighbour within range
    // (ties to the earlier one) and move to their midpoint, removing the neighbour by swapping in the last
    // point. The game scans every point for each neighbour, which took most of Pregenerate's time in the
    // browser; here points are kept on a grid of range-sized cells, so only nearby ones are looked at.
    // Positions in the array keep the list's order, so the result is the same.
    static List<V2> MergePoints(List<V2> list, float range)
    {
        var points = list.ToArray();
        int head = 0, end = points.Length; // the list is points[head..end)
        var cells = new Dictionary<(int, int), List<int>>();
        for (var i = 0; i < end; i++) CellOf(points[i]).Add(i);

        var merged = new List<V2>();
        while (head < end)
        {
            var p = points[head];
            CellOf(p).Remove(head++);
            while (head < end)
            {
                var closest = FindClosest(p);
                if (closest == -1) break;
                p = (p + points[closest]) * 0.5f;
                CellOf(points[closest]).Remove(closest);
                if (closest != --end)
                {
                    CellOf(points[end]).Remove(end);
                    points[closest] = points[end];
                    CellOf(points[closest]).Add(closest);
                }
            }
            merged.Add(p);
        }
        return merged;

        List<int> CellOf(V2 v)
        {
            var key = Cell(v);
            if (!cells.TryGetValue(key, out var cell)) cells[key] = cell = [];
            return cell;
        }

        (int, int) Cell(V2 v) => ((int)MathF.Floor(v.X / range), (int)MathF.Floor(v.Y / range));

        int FindClosest(V2 p)
        {
            var result = -1;
            var best = 99999f;
            var (cx, cy) = Cell(p);
            for (var y = cy - 1; y <= cy + 1; y++)
            for (var x = cx - 1; x <= cx + 1; x++)
            {
                if (!cells.TryGetValue((x, y), out var cell)) continue;
                foreach (var i in cell)
                {
                    if (points[i] == p) continue;
                    var d = V2.Distance(p, points[i]);
                    if (d < range && (d < best || (d == best && i < result))) (result, best) = (i, d);
                }
            }
            return result;
        }
    }

    List<River> PlaceRivers(List<V2> lakes, UnityRandom random)
    {
        var rivers = new List<River>();
        var open = new List<V2>(lakes);
        while (open.Count > 1)
        {
            var p = open[0];
            var end = FindRandomRiverEnd(random, rivers, lakes, p, 2000f, 0.4f, 128f);
            if (end == -1 && !HaveRiver(rivers, p)) end = FindRandomRiverEnd(random, rivers, lakes, p, 5000f, 0.4f, 128f);
            if (end != -1)
            {
                var widthMax = random.Range(60f, 100f);
                var widthMin = random.Range(60f, widthMax);
                var length = V2.Distance(p, lakes[end]);
                rivers.Add(new River(p, lakes[end], widthMin, widthMax, (float)(length / 15.0), (float)(length / 20.0)));
            }
            else
            {
                open.RemoveAt(0);
            }
        }
        return rivers;
    }

    int FindRandomRiverEnd(UnityRandom random, List<River> rivers, List<V2> points, V2 p, float maxDistance, float heightLimit, float step)
    {
        var candidates = new List<int>();
        for (var i = 0; i < points.Count; i++)
            if (!(points[i] == p) && V2.Distance(p, points[i]) < maxDistance && !HaveRiver(rivers, p, points[i]) && IsRiverAllowed(p, points[i], step, heightLimit))
                candidates.Add(i);
        return candidates.Count == 0 ? -1 : candidates[random.Range(0, candidates.Count)];
    }

    static bool HaveRiver(List<River> rivers, V2 p) => rivers.Any(r => r.P0 == p || r.P1 == p);

    static bool HaveRiver(List<River> rivers, V2 a, V2 b) =>
        rivers.Any(r => (r.P0 == a && r.P1 == b) || (r.P0 == b && r.P1 == a));

    bool IsRiverAllowed(V2 p0, V2 p1, float step, float heightLimit)
    {
        var length = V2.Distance(p0, p1);
        var dir = (p1 - p0).Normalized;
        var allWater = true;
        for (var d = step; d <= (float)(length - (double)step); d = (float)(d + (double)step))
        {
            var p = p0 + dir * d;
            var h = GetBaseHeight(p.X, p.Y);
            if (h > heightLimit) return false;
            if (h > 0.05f) allWater = false;
        }
        return !allWater;
    }

    enum RiverAdd { All, SkipDeepNorth, OnlyDeepNorth }

    void PlaceStreams(bool isDN)
    {
        var random = new UnityRandom(_streamSeed);
        var streams = new List<River>();
        for (var i = 0; i < 3000; i++)
        {
            if (!FindStreamStartPoint(random, 100, 26f, 31f, out var start, !isDN)) continue;
            if (!FindStreamEndPoint(random, 100, 36f, 44f, start, 80f, 200f, out var end, !isDN)) continue;
            var centre = (start + end) * 0.5f;
            var h = GetPregenerationHeight(centre.X, centre.Y, !isDN);
            if (h < 26f || h > 44f) continue;
            var length = V2.Distance(start, end);
            streams.Add(new River(start, end, 20f, 20f, (float)(length / 15.0), (float)(length / 20.0)));
        }
        RenderRivers(streams, isDN ? RiverAdd.OnlyDeepNorth : RiverAdd.SkipDeepNorth, random);
    }

    float GetPregenerationHeight(float wx, float wy, bool riverPreGen) =>
        GetBiomeHeight(GetBiome(wx, wy), wx, wy, preGeneration: true, riverPreGen);

    bool FindStreamEndPoint(UnityRandom random, int iterations, float minHeight, float maxHeight, V2 start, float minLength, float maxLength, out V2 end, bool riverPreGen)
    {
        var step = (float)((maxLength - (double)minLength) / iterations);
        var length = maxLength;
        for (var i = 0; i < iterations; i++)
        {
            length = (float)(length - (double)step);
            var f = random.Range(0f, MathF.PI * 2f);
            var p = start + new V2((float)Math.Sin(f), (float)Math.Cos(f)) * length;
            var h = GetPregenerationHeight(p.X, p.Y, riverPreGen);
            if (h > minHeight && h < maxHeight)
            {
                end = p;
                return true;
            }
        }
        end = default;
        return false;
    }

    bool FindStreamStartPoint(UnityRandom random, int iterations, float minHeight, float maxHeight, out V2 p, bool riverPreGen)
    {
        for (var i = 0; i < iterations; i++)
        {
            var x = random.Range(-10000f, 10000f);
            var y = random.Range(-10000f, 10000f);
            var h = GetPregenerationHeight(x, y, riverPreGen);
            if (h > minHeight && h < maxHeight)
            {
                p = new V2(x, y);
                return true;
            }
        }
        p = default;
        return false;
    }

    // Lays points along each river's wavy course, added to every 64 m grid cell they reach.
    void RenderRivers(List<River> rivers, RiverAdd rule, UnityRandom random)
    {
        var added = new Dictionary<(int, int), List<RiverPoint>>();
        foreach (var river in rivers)
        {
            if (rule != RiverAdd.All)
            {
                var dn = IsDeepNorth(river.P0.X, river.P0.Y);
                if ((dn && rule == RiverAdd.SkipDeepNorth) || (!dn && rule == RiverAdd.OnlyDeepNorth)) continue;
            }
            var step = (float)(river.WidthMin / 8.0);
            var dir = (river.P1 - river.P0).Normalized;
            var side = new V2(-dir.Y, dir.X);
            var length = V2.Distance(river.P0, river.P1);
            for (var d = 0f; d <= length; d = (float)(d + (double)step))
            {
                var t = (float)(d / (double)river.CurveWavelength);
                var bend = (float)(Math.Sin(t) * Math.Sin(t * 0.634119987487793) * Math.Sin(t * 0.3341200053691864) * river.CurveWidth);
                var r = random.Range(river.WidthMin, river.WidthMax);
                var p = river.P0 + dir * d + side * bend;
                AddRiverPoint(added, p, r);
            }
        }
        AddToGrid(added);
    }

    void AddToGrid(Dictionary<(int, int), List<RiverPoint>> added)
    {
        foreach (var (key, list) in added)
            _riverPoints![key] = _riverPoints.TryGetValue(key, out var existing) ? [.. existing, .. list] : [.. list];
    }

    void AddRiverPoint(Dictionary<(int, int), List<RiverPoint>> points, V2 p, float r)
    {
        _riverList.Add(new RiverPoint(p.X, p.Y, r));
        var (gx, gy) = RiverGrid(p.X, p.Y);
        var n = (int)MathF.Ceiling((float)(r / 64.0));
        for (var y = gy - n; y <= gy + n; y++)
        for (var x = gx - n; x <= gx + n; x++)
        {
            float dx = p.X - (float)(x * 64.0), dy = p.Y - (float)(y * 64.0);
            if (!(Math.Abs(dx) < (float)(r + 32.0) && Math.Abs(dy) < (float)(r + 32.0))) continue;
            if (!points.TryGetValue((x, y), out var list)) points[(x, y)] = list = [];
            list.Add(new RiverPoint(p.X, p.Y, r));
        }
    }

    // --- Maths, as Unity and Valheim's DUtils do it ---

    static float WorldAngle(float wx, float wy) => (float)Math.Sin((float)((double)(float)Math.Atan2(wx, wy) * 20.0));
    static float Length(float x, float y) => (float)Math.Sqrt((double)x * x + (double)y * y);
    static float Lerp(float a, float b, float t) => t <= 0f ? a : t >= 1f ? b : (float)(a * (1.0 - t) + b * (double)t);
    static float LerpStep(float l, float h, float v) => (float)Clamp01((v - (double)l) / (h - (double)l));
    static double Clamp01(double v) => v > 1 ? 1 : v < 0 ? 0 : v;
    static float SmoothStep(float min, float max, float x)
    {
        var t = (float)Clamp01((x - (double)min) / (max - (double)min));
        return (float)(t * (double)t * (3.0 - 2.0 * t));
    }
    static double Lerp(double a, double b, double t) => t <= 0 ? a : t >= 1 ? b : a * (1 - t) + b * t;
    static double LerpStep(double l, double h, double v) => Clamp01((v - l) / (h - l));
    static double Length(double x, double y) => Math.Sqrt(x * x + y * y);
    static double BlendOverlay(double a, double b) => a < 0.5 ? 2 * a * b : 1 - 2 * (1 - a) * (1 - b);
    static double Remap(double v, double inLow, double inHigh, double outLow, double outHigh) =>
        Lerp(outLow, outHigh, inLow == inHigh ? 0 : Clamp01((v - inLow) / (inHigh - inLow)));
    static double Fbm(double x, double y, int octaves, double lacunarity, double gain)
    {
        double sum = 0, amplitude = 1;
        for (var i = 0; i < octaves; i++)
        {
            sum += amplitude * Perlin(x, y);
            amplitude *= gain;
            x *= lacunarity;
            y *= lacunarity;
        }
        return sum;
    }
    static double MathfLikeSmoothStep(double from, double to, double t)
    {
        t = Clamp01(t);
        t = -2.0 * t * t * t + 3.0 * t * t;
        return (float)(to * t + from * (1.0 - t));
    }

    // Unity's Vector2, with its approximate ==.
    readonly record struct V2(float X, float Y)
    {
        public float Magnitude => (float)Math.Sqrt(X * X + Y * Y);
        public V2 Normalized => Magnitude is var m && m > 1E-05f ? new V2(X / m, Y / m) : default;
        public static float Distance(V2 a, V2 b) => (a - b).Magnitude;
        public static V2 operator +(V2 a, V2 b) => new(a.X + b.X, a.Y + b.Y);
        public static V2 operator -(V2 a, V2 b) => new(a.X - b.X, a.Y - b.Y);
        public static V2 operator *(V2 a, float s) => new(a.X * s, a.Y * s);
        public bool Equals(V2 o) => (X - o.X) * (X - o.X) + (Y - o.Y) * (Y - o.Y) < 9.99999944E-11f;
        public override int GetHashCode() => 0;
    }

    // Unity's Mathf.PerlinNoise: Ken Perlin's improved noise at z = 0 (so one extra permutation
    // lookup per corner), scaled to roughly 0..1, and mirrored for negative inputs. The mirroring
    // isn't documented; without it only ~78% of a save's locations land in the right biome.
    public static float Perlin(double xd, double yd)
    {
        float x = Math.Abs((float)xd), y = Math.Abs((float)yd);
        int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y);
        int X = xi & 255, Y = yi & 255;
        x -= xi;
        y -= yi;
        float u = Fade(x), v = Fade(y);
        int A = P[X] + Y, B = P[X + 1] + Y;
        int aa = P[P[A]], ab = P[P[A + 1]], ba = P[P[B]], bb = P[P[B + 1]];
        var n = LerpF(v, LerpF(u, Grad(aa, x, y), Grad(ba, x - 1, y)), LerpF(u, Grad(ab, x, y - 1), Grad(bb, x - 1, y - 1)));
        return (n + 0.69f) / 1.483f;
    }

    static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);
    static float LerpF(float t, float a, float b) => a + t * (b - a);
    static float Grad(int hash, float x, float y)
    {
        var h = hash & 15;
        var u = h < 8 ? x : y;
        var v = h < 4 ? y : h is 12 or 14 ? x : 0;
        return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
    }

    static readonly int[] P = BuildPermutation();

    static int[] BuildPermutation()
    {
        int[] p =
        [
            151,160,137,91,90,15,131,13,201,95,96,53,194,233,7,225,140,36,103,30,69,142,8,99,37,240,21,10,23,190,6,148,
            247,120,234,75,0,26,197,62,94,252,219,203,117,35,11,32,57,177,33,88,237,149,56,87,174,20,125,136,171,168,68,175,
            74,165,71,134,139,48,27,166,77,146,158,231,83,111,229,122,60,211,133,230,220,105,92,41,55,46,245,40,244,102,143,54,
            65,25,63,161,1,216,80,73,209,76,132,187,208,89,18,169,200,196,135,130,116,188,159,86,164,100,109,198,173,186,3,64,
            52,217,226,250,124,123,5,202,38,147,118,126,255,82,85,212,207,206,59,227,47,16,58,17,182,189,28,42,223,183,170,213,
            119,248,152,2,44,154,163,70,221,153,101,155,167,43,172,9,129,22,39,253,19,98,108,110,79,113,224,232,178,185,112,104,
            218,246,97,228,251,34,242,193,238,210,144,12,191,179,162,241,81,51,145,235,249,14,239,107,49,192,214,31,181,199,106,157,
            184,84,204,176,115,121,50,45,127,4,150,254,138,236,205,93,222,114,67,29,24,72,243,141,128,195,78,66,215,61,156,180,
        ];
        return [.. p, .. p];
    }

    // Unity's Random: xorshift128, seeded as in Random.InitState; Range as in Random.Range.
    sealed class UnityRandom
    {
        uint _x, _y, _z, _w;

        public UnityRandom(int seed)
        {
            _x = (uint)seed;
            _y = unchecked(_x * 1812433253u + 1);
            _z = unchecked(_y * 1812433253u + 1);
            _w = unchecked(_z * 1812433253u + 1);
        }

        uint Next()
        {
            var t = _x ^ (_x << 11);
            (_x, _y, _z) = (_y, _z, _w);
            return _w = _w ^ (_w >> 19) ^ t ^ (t >> 8);
        }

        public int Range(int min, int max) => max > min ? (int)(min + (long)(Next() % (uint)((long)max - min))) : min;

        // Random.Range(float, float): 23 random bits as t in [0, 1], then lerped from max to min. The
        // order matters for river widths; this way round matches every saved location's height.
        public float Range(float min, float max)
        {
            var t = (Next() & 0x7FFFFF) / 8388607f;
            return (1 - t) * max + t * min;
        }
    }
}
