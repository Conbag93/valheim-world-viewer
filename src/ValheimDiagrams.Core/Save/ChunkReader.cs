using System.Text.RegularExpressions;

namespace ValheimDiagrams.Core;

// One saved object. Valheim's axes: +X east, +Y up, +Z north. Rotation is Euler degrees.
public readonly record struct Zdo(int Prefab, float X, float Y, float Z, float RotX, float RotY, float RotZ, long? Creator);

// Reads Valheim 1.0 world chunk files (worlds_local/<World>/*.chunk), which hold ZDO.Save records.
public static partial class ChunkReader
{
    public const short SupportedVersion = 41;

    static readonly int CreatorKey = StableHash.Of("creator");

    // A save can briefly leave an older version of a chunk next to the new one; keep the newest.
    public static IEnumerable<string> LatestChunkFiles(IEnumerable<string> fileNames) =>
        fileNames
            .Select(name => (name, m: ChunkName().Match(name)))
            .Where(x => x.m.Success)
            .GroupBy(x => x.m.Groups["key"].Value)
            .Select(g => g.MaxBy(x => int.Parse(x.m.Groups["ver"].Value)).name)
            .Order();

    public static List<Zdo> Read(Stream stream, string name)
    {
        using var reader = new BinaryReader(stream);
        var version = reader.ReadInt16();
        if (version != SupportedVersion)
            throw new NotSupportedException($"{name}: chunk version {version} (expected {SupportedVersion})");

        var count = reader.ReadInt32();
        var zdos = new List<Zdo>(count);
        for (var i = 0; i < count; i++) zdos.Add(ReadZdo(reader));

        if (reader.BaseStream.Position != reader.BaseStream.Length)
            throw new InvalidDataException($"{name}: {reader.BaseStream.Length - reader.BaseStream.Position} unread bytes");
        return zdos;
    }

    // Mirrors ZDO.Load for world versions >= ChunkedSave.
    static Zdo ReadZdo(BinaryReader r)
    {
        var flags = (Flags)r.ReadUInt16();

        float x, y, z;
        if (flags.HasFlag(Flags.SmallPosition)) (x, y, z) = (r.ReadInt16(), 0, r.ReadInt16());
        else (x, y, z) = (r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        var prefab = r.ReadInt32();
        var (rx, ry, rz) = flags.HasFlag(Flags.Rotation) ? ReadSmallRotation(r) : (0f, 0f, 0f);

        long? creator = null;
        if (flags.HasFlag(Flags.Connections)) { r.ReadByte(); r.ReadInt32(); }
        Skip(r, flags, Flags.Floats, 4);
        Skip(r, flags, Flags.Vec3, 12);
        Skip(r, flags, Flags.Quaternions, 16);
        Skip(r, flags, Flags.Ints, 4);
        if (flags.HasFlag(Flags.Longs))
        {
            for (int n = ReadNumItems(r), i = 0; i < n; i++)
            {
                var key = r.ReadInt32();
                var value = r.ReadInt64();
                if (key == CreatorKey) creator = value;
            }
        }
        if (flags.HasFlag(Flags.Strings))
            for (int n = ReadNumItems(r), i = 0; i < n; i++) { r.ReadInt32(); r.ReadString(); }
        if (flags.HasFlag(Flags.ByteArrays))
            for (int n = ReadNumItems(r), i = 0; i < n; i++) { r.ReadInt32(); r.BaseStream.Seek(r.ReadInt32(), SeekOrigin.Current); }

        return new Zdo(prefab, x, y, z, rx, ry, rz, creator);
    }

    static void Skip(BinaryReader r, Flags flags, Flags flag, int valueSize)
    {
        if (!flags.HasFlag(flag)) return;
        var n = ReadNumItems(r);
        r.BaseStream.Seek(n * (4L + valueSize), SeekOrigin.Current);
    }

    static int ReadNumItems(BinaryReader r)
    {
        int n = r.ReadByte();
        return (n & 0x80) != 0 ? ((n & 0x7F) << 8) | r.ReadByte() : n;
    }

    // ZPackage.ReadSmallRotation: half-degree steps, Y-only rotations packed into one ushort.
    static (float, float, float) ReadSmallRotation(BinaryReader r)
    {
        uint n = r.ReadUInt16();
        if ((n & 0x8000) != 0) return (0, (n & 0x7FFF) * 0.5f, 0);
        n = (n << 16) | r.ReadUInt16();
        return ((n & 0x3FF) * 0.5f, ((n >> 10) & 0x3FF) * 0.5f, ((n >> 20) & 0x3FF) * 0.5f);
    }

    [Flags]
    enum Flags : ushort
    {
        Connections = 1,
        Floats = 2,
        Vec3 = 4,
        Quaternions = 8,
        Ints = 0x10,
        Longs = 0x20,
        Strings = 0x40,
        ByteArrays = 0x80,
        Rotation = 0x1000,
        SmallPosition = 0x2000,
    }

    // e.g. "12_1c__1_315.chunk": chunk 12_1c, size 1, saved version 315.
    [GeneratedRegex(@"^(?<key>[0-9a-f]+_[0-9a-f]+__\d+)_(?<ver>\d+)\.chunk$")]
    private static partial Regex ChunkName();
}

public static class StableHash
{
    // Valheim's string.GetStableHashCode, used for prefab names and ZDO keys.
    public static int Of(string s)
    {
        unchecked
        {
            int a = 5381, b = 5381;
            for (var i = 0; i < s.Length && s[i] != '\0'; i += 2)
            {
                a = ((a << 5) + a) ^ s[i];
                if (i == s.Length - 1 || s[i + 1] == '\0') break;
                b = ((b << 5) + b) ^ s[i + 1];
            }
            return a + b * 1566083941;
        }
    }
}
