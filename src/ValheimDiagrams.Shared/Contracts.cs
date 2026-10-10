namespace ValheimDiagrams.Shared;

// Wire contracts between the server API and the WebAssembly client.
// World coordinates are in metres: +X is east, +Y is north.

public readonly record struct Point2(double X, double Y);

public enum ElementKind
{
    Rect,     // Points[0] = south-west corner, Width x Height
    Polygon,  // closed shape through Points
    Polyline, // open path through Points
    Circle,   // Points[0] = centre, Radius
    Label,    // Points[0] = centre of the text
}

public enum LegendSwatch { Line, Fill, Hatch }

public record ElementStyle(
    string? Stroke = null,
    double StrokeWidth = 0.1,
    string? Fill = null,
    double FillOpacity = 1,
    string? Dash = null,
    bool Hatch = false,
    bool RoundCaps = false,
    bool ArrowEnd = false,
    double CornerRadius = 0,
    bool Emphasis = false);

public record ElementDto(
    int Id,
    int LayerId,
    ElementKind Kind,
    IReadOnlyList<Point2> Points,
    double Width,
    double Height,
    double Radius,
    string? Text,
    string? Note,
    int? PieceId,
    ElementStyle Style,
    // Height of the shape's base in metres (Valheim's Y), when it comes from a 3D build.
    double? Elevation = null);

public record LayerDto(
    int Id,
    string Name,
    int SortOrder,
    bool VisibleByDefault,
    string LegendColor,
    LegendSwatch LegendSwatch,
    IReadOnlyList<ElementDto> Elements);

public record PieceDto(
    int Id,
    string PrefabName,
    string DisplayName,
    string Category,
    double Width,
    double Depth,
    double Height);


public record DiagramDto(
    int Id,
    string Name,
    string? Description,
    double GridSize,
    IReadOnlyList<LayerDto> Layers,
    IReadOnlyList<PieceDto> Pieces,
    // Where grid lines fall (mod GridSize). Builds in world coordinates sit on a grid with an
    // arbitrary offset, e.g. Valheim floor tiles edged at x ≡ 1.6, z ≡ 1.5 (mod 2).
    Point2? GridOffset = null);

// A floor level found among a window's pieces: the height its floor tiles sit at.
public record LevelDto(int Index, double Y, int Floors);

// A rectangle of the world in Valheim's X (east) and Z (north), in metres.
public record AreaDto(double MinX, double MaxX, double MinZ, double MaxZ);

// A named place to jump to, like Minutor's saved locations. Kept per world across re-imports.
public record SavedViewDto(int Id, string Name, AreaDto Area);


// One import of a world's player-built pieces, in world coordinates.
// HasTerrain: explored zones are known, so a biome map can be drawn. BiomeCheck*: how many of the
// save's locations sit in an allowed biome by the viewer's generator (all of them, if it's right).
public record WorldDto(int Id, string WorldName, string SeedName, string Source, DateTime SavedUtc, DateTime ImportedUtc,
    int PieceCount, AreaDto? Extent, IReadOnlyList<SavedViewDto> Views,
    bool HasTerrain = false, int BiomeCheckLocations = 0, int BiomeCheckMatched = 0);


// A cluster of construction found in a world, largest first. Index is its rank (1 = most pieces).
// Interior builds are inside dungeons, which the game places about 5 km up.
public record BuildDto(int Index, AreaDto Area, int PieceCount, double MinY, double MaxY, IReadOnlyList<string> CommonPieces, bool Interior);

// The pieces inside one area of a world, drawn as a diagram, with the storeys found in them.
public record WindowDto(AreaDto Area, DiagramDto Diagram, IReadOnlyList<LevelDto> Levels, int PieceCount, bool Truncated);

public record ImportResultDto(int WorldId, string WorldName, int ChunkFiles, int ObjectsRead, int PlayerBuilt, IReadOnlyList<string> UnknownPrefabs);
