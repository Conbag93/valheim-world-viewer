namespace ValheimDiagrams.Core;

// Saves store prefabs as StableHash(name). prefab-names.txt is the vanilla prefab list published by
// the Jötunn modding project (valheim-modding.github.io/Jotunn/data/prefabs/prefab-list.html);
// hashing every name gives the reverse lookup.
public static class PrefabNames
{
    static readonly Lazy<Dictionary<int, string>> ByHash = new(Load);

    public static string? Find(int hash) => ByHash.Value.GetValueOrDefault(hash);

    static Dictionary<int, string> Load()
    {
        using var stream = typeof(PrefabNames).Assembly.GetManifestResourceStream("ValheimDiagrams.Core.prefab-names.txt")
            ?? throw new InvalidOperationException("prefab-names.txt is not embedded");
        using var reader = new StreamReader(stream);
        var map = new Dictionary<int, string>();
        while (reader.ReadLine() is { } line)
        {
            var name = line.Trim();
            if (name.Length > 0) map.TryAdd(StableHash.Of(name), name);
        }
        return map;
    }
}
