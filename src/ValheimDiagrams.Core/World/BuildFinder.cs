using ValheimDiagrams.Shared;

namespace ValheimDiagrams.Core;

// Groups a world's pieces into builds: clusters of construction with less than CellSize of open
// ground between them. Buckets pieces into cells and joins neighbouring occupied cells.
// Dungeon interiors are kept apart: the game places them about 5 km up, above their entrances.
public static class BuildFinder
{
    const double CellSize = 32;

    // Anything this high is inside a dungeon or crypt interior, not on the surface.
    public const double InteriorHeight = 1000;

    // Room left around a build's pieces when it's opened.
    const double Margin = 12;

    public readonly record struct Piece(double X, double Y, double Z, string DisplayName);

    public static List<BuildDto> Find(IReadOnlyList<Piece> pieces)
    {
        var cells = pieces.GroupBy(p => (X: (int)Math.Floor(p.X / CellSize), Z: (int)Math.Floor(p.Z / CellSize), Interior: p.Y > InteriorHeight))
            .ToDictionary(g => g.Key, g => g.ToList());

        var seen = new HashSet<(int X, int Z, bool Interior)>();
        var builds = new List<List<Piece>>();
        foreach (var start in cells.Keys)
        {
            if (!seen.Add(start)) continue;
            var members = new List<Piece>();
            var queue = new Queue<(int X, int Z, bool Interior)>([start]);
            while (queue.TryDequeue(out var c))
            {
                members.AddRange(cells[c]);
                for (var dx = -1; dx <= 1; dx++)
                for (var dz = -1; dz <= 1; dz++)
                {
                    var n = (c.X + dx, c.Z + dz, c.Interior);
                    if (cells.ContainsKey(n) && seen.Add(n)) queue.Enqueue(n);
                }
            }
            builds.Add(members);
        }

        return [.. builds
            .OrderByDescending(b => b.Count)
            .Select((b, i) => new BuildDto(
                i + 1,
                new AreaDto(b.Min(p => p.X) - Margin, b.Max(p => p.X) + Margin, b.Min(p => p.Z) - Margin, b.Max(p => p.Z) + Margin),
                b.Count,
                Math.Round(b.Min(p => p.Y), 1),
                Math.Round(b.Max(p => p.Y), 1),
                [.. b.GroupBy(p => p.DisplayName).OrderByDescending(g => g.Count()).Take(3).Select(g => g.Key)],
                b[0].Y > InteriorHeight))];
    }
}
