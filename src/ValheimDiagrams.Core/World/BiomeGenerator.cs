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

// Valheim 1.0's WorldGenerator.GetBiome, which isn't saved anywhere: the game recomputes it from the
// seed. Ported from the decompiled 1.0.17 server; research/worldgen/biome.py is the reference port.
// Checked on import against the save's own locations (see LocationCheck).
public sealed class BiomeGenerator
{
    public const float WorldRadius = 10500;

    readonly float _offset0, _offset1, _offset2, _offset4;
    readonly float _minMountainDistance = 1000, _minDarklandNoise = 0.4f, _maxMarshDistance = 6000;

    public BiomeGenerator(int seed, int worldGenVersion)
    {
        // WorldGenerator's constructor: Random.InitState(seed), then these draws in this order.
        var random = new UnityRandom(seed);
        _offset0 = random.Range(-10000, 10000);
        _offset1 = random.Range(-10000, 10000);
        _offset2 = random.Range(-10000, 10000);
        random.Range(-10000, 10000); // offset3: used for terrain height, not biomes
        random.Range(int.MinValue, int.MaxValue); // river seed
        random.Range(int.MinValue, int.MaxValue); // stream seed
        _offset4 = random.Range(-10000, 10000);

        if (worldGenVersion <= 0) _minMountainDistance = 1500;
        if (worldGenVersion <= 1) (_minDarklandNoise, _maxMarshDistance) = (0.5f, 8000);
    }

    // x east, z north, in metres.
    public Biome GetBiome(float wx, float wy)
    {
        var distance = Length(wx, wy);
        var baseHeight = GetBaseHeight(wx, wy);
        var angle = WorldAngle(wx, wy) * 100;

        if (Length(wx, wy - 4000) > 12000 + angle) return Biome.AshLands;
        if (baseHeight <= 0.02f) return Biome.Ocean;
        if (Length(wx, wy + 4000) > 12000 + angle) return Biome.DeepNorth;
        if (baseHeight > 0.4f) return Biome.Mountain;
        if (Noise(_offset0, wx, wy) > 0.6f && distance > 2000 && distance < _maxMarshDistance && baseHeight > 0.05f && baseHeight < 0.25f) return Biome.Swamp;
        if (Noise(_offset4, wx, wy) > _minDarklandNoise && distance > 6000 + angle && distance < 10000) return Biome.Mistlands;
        if (Noise(_offset1, wx, wy) > 0.4f && distance > 3000 + angle && distance < 8000) return Biome.Plains;
        if (Noise(_offset2, wx, wy) > 0.4f && distance > 600 + angle && distance < 6000) return Biome.BlackForest;
        if (distance > 5000 + angle) return Biome.BlackForest;
        return Biome.Meadows;
    }

    static float Noise(float offset, float wx, float wy) =>
        Perlin((double)(float)(offset + (double)wx) * 0.001f, (double)(float)(offset + (double)wy) * 0.001f);

    float GetBaseHeight(float wx, float wy)
    {
        var distance = Length(wx, wy);
        double x = wx + 100000.0 + _offset0, y = wy + 100000.0 + _offset1;
        double h = Perlin(x * 0.002f * 0.5, y * 0.002f * 0.5) * Perlin(x * 0.003f * 0.5, y * 0.003f * 0.5);
        h += Perlin(x * 0.002f, y * 0.002f) * Perlin(x * 0.003f, y * 0.003f) * h * 0.9f;
        h += Perlin(x * 0.005f, y * 0.005f) * Perlin(x * 0.01f, y * 0.01f) * 0.5 * h;
        h -= 0.07f;

        // River valleys carved into the base height.
        double a = Perlin(x * 0.002f * 0.25 + 0.123f, y * 0.002f * 0.25 + 0.15123f);
        double b = Perlin(x * 0.002f * 0.25 + 0.321f, y * 0.002f * 0.25 + 0.231f);
        var river = (1 - LerpStep(0.02, 0.12, Math.Abs(a - b))) * SmoothStep(744, 1000, distance);
        h *= 1 - river;

        if (distance > 10000)
        {
            h = Lerp(h, -0.2, LerpStep(10000, 10500, distance));
            if (distance > 10490) h = Lerp(h, -2, LerpStep(10490, 10500, distance));
            return (float)h;
        }
        if (distance < _minMountainDistance && h > 0.28)
        {
            var t = Math.Clamp((h - 0.28) / 0.1, 0, 1);
            h = Lerp(Lerp(0.28, 0.38, t), h, LerpStep(_minMountainDistance - 400, _minMountainDistance, distance));
        }
        return (float)h;
    }

    static float WorldAngle(float wx, float wy) => (float)Math.Sin((float)Math.Atan2(wx, wy) * 20.0);
    static float Length(float x, float y) => (float)Math.Sqrt((double)x * x + (double)y * y);
    static double Lerp(double a, double b, double t) => t <= 0 ? a : t >= 1 ? b : a + (b - a) * t;
    static double LerpStep(double l, double h, double v) => Math.Clamp((v - l) / (h - l), 0, 1);
    static double SmoothStep(double min, double max, double x)
    {
        var t = Math.Clamp((x - min) / (max - min), 0, 1);
        return t * t * (3 - 2 * t);
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

    // Unity's Random: xorshift128, seeded as in Random.InitState; Range(int, int) as in Random.Range.
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
    }
}
