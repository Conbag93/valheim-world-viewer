using ValheimDiagrams.Shared;

namespace ValheimDiagrams.Client.Diagrams;

// A horizontal cut through a 3D build, after Minutor's depth slider: everything at or below
// Ceiling is drawn, highest on top, and pieces below ShadeFrom (the current storey's floor)
// get darker the further down they are. Floor, when set, hides everything beneath it.
// The canvas applies it in diagram-canvas.js (applySlice); Contains is for counting in .NET.
public sealed record Slice(double Ceiling, double ShadeFrom, double? Floor = null)
{
    // Pieces this far below the current storey reach the maximum darkening.
    public const double ShadeDepth = 12;
    public const double MaxShade = 0.55;

    // Shapes without an elevation (hand-drawn plans) are always shown.
    public bool Contains(ElementDto e) =>
        e.Elevation is not { } y || (y <= Ceiling && (Floor is not { } f || y >= f));
}
