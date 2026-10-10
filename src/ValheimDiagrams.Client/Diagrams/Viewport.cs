namespace ValheimDiagrams.Client.Diagrams;

// Pan/zoom state for the canvas, in SVG units (metres, y flipped).
public class Viewport
{
    public double Width { get; private set; } = 800;   // element size in CSS px
    public double Height { get; private set; } = 600;
    public double Scale { get; private set; } = 14;    // px per metre
    public double CentreX { get; private set; }
    public double CentreY { get; private set; }

    // 0.02 px/m fits a whole Valheim world (about 20 km across) on a 400 px screen.
    public const double MinScale = 0.02, MaxScale = 200;

    public double Left => CentreX - Width / 2 / Scale;
    public double Top => CentreY - Height / 2 / Scale;

    public string ViewBox =>
        $"{Geometry.Svg(Left)} {Geometry.Svg(Top)} {Geometry.Svg(Width / Scale)} {Geometry.Svg(Height / Scale)}";

    public void Resize(double width, double height)
    {
        if (width > 0) Width = width;
        if (height > 0) Height = height;
    }

    public void Fit(Bounds world, double padding = 0.95)
    {
        Scale = Math.Clamp(Math.Min(Width / world.Width, Height / world.Height) * padding, MinScale, MaxScale);
        CentreX = world.Centre.X;
        CentreY = -world.Centre.Y;
    }

    // Zoom keeping the point under (offsetX, offsetY) fixed on screen.
    public void ZoomAt(double offsetX, double offsetY, double factor)
    {
        var (sx, sy) = ToSvg(offsetX, offsetY);
        Scale = Math.Clamp(Scale * factor, MinScale, MaxScale);
        CentreX = sx - (offsetX - Width / 2) / Scale;
        CentreY = sy - (offsetY - Height / 2) / Scale;
    }

    public void ZoomCentre(double factor) => ZoomAt(Width / 2, Height / 2, factor);

    public void PanBy(double dxPx, double dyPx)
    {
        CentreX -= dxPx / Scale;
        CentreY -= dyPx / Scale;
    }

    public (double X, double Y) ToSvg(double offsetX, double offsetY) =>
        (Left + offsetX / Scale, Top + offsetY / Scale);

    public (double X, double Y) ToWorld(double offsetX, double offsetY)
    {
        var (x, y) = ToSvg(offsetX, offsetY);
        return (x, -y);
    }
}
