using ValheimDiagrams.Shared;

namespace ValheimDiagrams.Client.Diagrams;

public readonly record struct Bounds(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
    public Point2 Centre => new((MinX + MaxX) / 2, (MinY + MaxY) / 2);

    public Bounds Union(Bounds o) => new(Math.Min(MinX, o.MinX), Math.Min(MinY, o.MinY), Math.Max(MaxX, o.MaxX), Math.Max(MaxY, o.MaxY));
    public Bounds Inflate(double m) => new(MinX - m, MinY - m, MaxX + m, MaxY + m);

    public static Bounds Of(IEnumerable<Point2> points)
    {
        var b = new Bounds(double.MaxValue, double.MaxValue, double.MinValue, double.MinValue);
        foreach (var p in points)
            b = new(Math.Min(b.MinX, p.X), Math.Min(b.MinY, p.Y), Math.Max(b.MaxX, p.X), Math.Max(b.MaxY, p.Y));
        return b;
    }
}

// What the canvas is showing: the visible area (diagram coordinates) and the zoom in px per metre.
public readonly record struct ViewInfo(Bounds Visible, double Scale);

public static class Geometry
{
    // Label pills were sized in the original at 14 px/m from an estimated text width.
    public static (double Width, double Height, double FontSize) PillSize(ElementDto e)
    {
        bool emph = e.Style.Emphasis;
        double w = ((e.Text?.Length ?? 0) * (emph ? 7.6 : 6.6) + 14) / 14;
        return (w, 20.0 / 14, (emph ? 14.0 : 12.0) / 14);
    }

    public static Bounds BoundsOf(ElementDto e)
    {
        var p0 = e.Points.Count > 0 ? e.Points[0] : default;
        return e.Kind switch
        {
            ElementKind.Rect => new(p0.X, p0.Y, p0.X + e.Width, p0.Y + e.Height),
            ElementKind.Circle => new(p0.X - e.Radius, p0.Y - e.Radius, p0.X + e.Radius, p0.Y + e.Radius),
            ElementKind.Label => PillBounds(e, p0),
            _ => Bounds.Of(e.Points),
        };
    }

    static Bounds PillBounds(ElementDto e, Point2 c)
    {
        var (w, h, _) = PillSize(e);
        return new(c.X - w / 2, c.Y - h / 2, c.X + w / 2, c.Y + h / 2);
    }

    public static Bounds BoundsOf(IEnumerable<ElementDto> elements) =>
        elements.Select(BoundsOf).Aggregate((a, b) => a.Union(b));

    public static double Distance(Point2 a, Point2 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    public static IEnumerable<double> EdgeLengths(ElementDto e)
    {
        var pts = e.Points;
        for (int i = 0; i + 1 < pts.Count; i++)
            yield return Distance(pts[i], pts[i + 1]);
        if (e.Kind == ElementKind.Polygon && pts.Count > 2)
            yield return Distance(pts[^1], pts[0]);
    }

    // SVG's y axis points down; the diagram's points north (up).
    public static string Svg(double v) => v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
    public static string SvgPoints(IEnumerable<Point2> pts) => string.Join(' ', pts.Select(p => $"{Svg(p.X)},{Svg(-p.Y)}"));
}
