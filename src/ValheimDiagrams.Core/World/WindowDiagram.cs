using ValheimDiagrams.Shared;

namespace ValheimDiagrams.Core;

// Turns the pieces inside one window of a world into the same DiagramDto the plan viewer draws:
// one layer per piece category, each piece a footprint polygon in world X (east) / Z (north) metres.
public static class WindowDiagram
{
    // A height counts as a floor level once this many floor tiles sit at it.
    const int MinFloorsPerLevel = 15;

    // Pieces resting on a floor sit at or just above it; a storey starts a little below.
    const double StoreyBelow = 0.6;

    // pieces: those inside area, from world.Pieces.
    public static WindowDto Build(LoadedWorld world, AreaDto area, IReadOnlyList<WorldPiece> pieces, bool truncated)
    {
        PieceInfo Info(WorldPiece p) => PieceCatalog.Get(world.Prefabs[p.PrefabIndex]);
        PieceCategory Category(WorldPiece p) => Info(p).Category;
        string Prefab(WorldPiece p) => world.Prefabs[p.PrefabIndex];

        var layers = new List<LayerDto>();
        foreach (var category in Enum.GetValues<PieceCategory>())
        {
            var elements = pieces.Where(p => Category(p) == category).OrderBy(p => p.Y)
                .Select(p => Element(p, Prefab(p), (int)category + 1)).ToList();
            if (elements.Count == 0) continue;
            layers.Add(new LayerDto((int)category + 1, category.Label(), (int)category, category != PieceCategory.RoofsAndBeams,
                LayerColour(category), LegendSwatch.Fill, elements));
        }

        // Piece types are identified by their index in world.Prefabs (+1, so 0 is never used).
        var defs = pieces.Select(p => p.PrefabIndex).Distinct()
            .Select(i => (i, info: PieceCatalog.Get(world.Prefabs[i])))
            .Select(x => new PieceDto(x.i + 1, world.Prefabs[x.i], x.info.DisplayName, x.info.Category.Label(), x.info.Width, x.info.Depth, x.info.Height))
            .ToList();

        var diagram = new DiagramDto(0, world.Meta.WorldName, null, 1, layers, defs, GridOffset(pieces, Info));
        return new WindowDto(area, diagram, Levels(pieces.Where(p => Category(p) == PieceCategory.Floors).ToList()), pieces.Count, truncated);
    }

    static List<LevelDto> Levels(List<WorldPiece> floors)
    {
        var heights = floors
            .GroupBy(p => Math.Round(p.Y, 1))
            .Where(g => g.Count() >= MinFloorsPerLevel)
            .OrderByDescending(g => g.Count())
            .ToList();

        // Keep the busiest height in any metre, so a slightly uneven floor counts once.
        var kept = new List<double>();
        foreach (var g in heights)
            if (kept.All(y => Math.Abs(y - g.Key) >= 1)) kept.Add(g.Key);
        kept.Sort();

        return [.. kept.Select((y, i) =>
        {
            double lo = i == 0 ? double.NegativeInfinity : y - StoreyBelow;
            double hi = i == kept.Count - 1 ? double.PositiveInfinity : kept[i + 1] - StoreyBelow;
            return new LevelDto(i, y, floors.Count(p => p.Y >= lo && p.Y < hi));
        })];
    }

    // Floor tiles snap to a 2 m grid with an arbitrary offset; report the most common tile edge
    // so the diagram's grid lines up with the build.
    static Point2? GridOffset(IReadOnlyList<WorldPiece> pieces, Func<WorldPiece, PieceInfo> info)
    {
        var floors = pieces.Where(p => info(p) is { Category: PieceCategory.Floors, Width: 2 }).ToList();
        if (floors.Count == 0) return null;
        static double EdgeOffset(IEnumerable<double> centres) => centres
            .GroupBy(c => Math.Round(Mod2(c - 1), 1))
            .MaxBy(g => g.Count())!.Key;
        return new Point2(EdgeOffset(floors.Select(p => (double)p.X)), EdgeOffset(floors.Select(p => (double)p.Z)));
    }

    static double Mod2(double v) => ((v % 2) + 2) % 2;

    static ElementDto Element(WorldPiece p, string prefab, int layerId)
    {
        var info = PieceCatalog.Get(prefab);
        double w = info.Width, d = info.Depth;

        var points = info.Round ? Circle(p.X, p.Z, w / 2) : Footprint(p.X, p.Z, w, d, p.RotY);
        var fill = info.Category is PieceCategory.Floors or PieceCategory.Blocks or PieceCategory.Walls or PieceCategory.RoofsAndBeams
            ? PieceCatalog.MaterialColour(prefab)
            : LayerColour(info.Category);
        var note = $"height {p.Y:0.0} · facing {p.RotY:0.#}°" + (info.Measured ? "" : " · footprint approximate");

        return new ElementDto(p.Id, layerId, ElementKind.Polygon, points, 0, 0, 0, null, note, p.PrefabIndex + 1,
            new ElementStyle("#2C2C2A", 0.04, fill), Math.Round(p.Y, 2));
    }

    // Unity yaw turns +Z (north) towards +X (east): local (x, z) -> (x cos + z sin, -x sin + z cos).
    static List<Point2> Footprint(double cx, double cy, double w, double d, double yawDegrees)
    {
        var a = yawDegrees * Math.PI / 180;
        double c = Math.Cos(a), s = Math.Sin(a);
        return [.. new (double X, double Z)[] { (-w / 2, -d / 2), (w / 2, -d / 2), (w / 2, d / 2), (-w / 2, d / 2) }
            .Select(v => new Point2(Math.Round(cx + v.X * c + v.Z * s, 3), Math.Round(cy - v.X * s + v.Z * c, 3)))];
    }

    static List<Point2> Circle(double cx, double cy, double r) =>
        [.. Enumerable.Range(0, 16).Select(i => i * Math.PI / 8)
            .Select(a => new Point2(Math.Round(cx + r * Math.Cos(a), 3), Math.Round(cy + r * Math.Sin(a), 3)))];

    static string LayerColour(PieceCategory c) => c switch
    {
        PieceCategory.Floors => "#B4B2A9",
        PieceCategory.Blocks => "#2C2C2A",
        PieceCategory.Walls => "#5F5E5A",
        PieceCategory.Stairs => "#1D9E75",
        PieceCategory.Storage => "#EF9F27",
        PieceCategory.Crafting => "#D4537E",
        PieceCategory.Furnishings => "#378ADD",
        PieceCategory.RoofsAndBeams => "#BA7517",
        _ => "#888780",
    };
}
