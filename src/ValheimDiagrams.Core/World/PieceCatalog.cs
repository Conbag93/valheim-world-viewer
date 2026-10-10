using System.Globalization;

namespace ValheimDiagrams.Core;

public enum PieceCategory { Floors, Blocks, Walls, Stairs, Storage, Crafting, Furnishings, RoofsAndBeams, Other }

// Footprint is the piece's top-down size: Width along its local X, Depth along local Z, in metres.
// Round pieces (barrels, spiral stairs) are drawn as circles of diameter Width.
public sealed record PieceInfo(string DisplayName, PieceCategory Category, double Width, double Depth, double Height, bool Round = false, bool Measured = false);

// Hand-kept knowledge of build pieces. Sizes marked Measured are encoded in the prefab name or the
// 2 m build grid; the rest are eyeballed and only good enough for a top-down sketch.
public static class PieceCatalog
{
    static readonly Dictionary<string, PieceInfo> Known = new(StringComparer.Ordinal)
    {
        ["stone_floor_2x2"] = new("Stone floor 2x2", PieceCategory.Floors, 2, 2, 1, Measured: true),
        ["blackmarble_floor"] = new("Black marble floor 2x2", PieceCategory.Floors, 2, 2, 0.5, Measured: true),
        ["iron_floor_2x2"] = new("Iron floor 2x2", PieceCategory.Floors, 2, 2, 0.2, Measured: true),
        ["iron_floor_1x1_v2"] = new("Iron floor 1x1", PieceCategory.Floors, 1, 1, 0.2, Measured: true),
        ["wood_floor"] = new("Wood floor 2x2", PieceCategory.Floors, 2, 2, 0.2, Measured: true),
        ["wood_floor_1x1"] = new("Wood floor 1x1", PieceCategory.Floors, 1, 1, 0.2, Measured: true),

        ["stone_wall_1x1"] = new("Stone wall 1x1", PieceCategory.Walls, 1, 1, 1, Measured: true),
        ["stone_wall_2x1"] = new("Stone wall 2x1", PieceCategory.Walls, 2, 1, 1, Measured: true),
        ["stone_wall_4x2"] = new("Stone wall 4x2", PieceCategory.Walls, 4, 1, 2, Measured: true),
        ["stone_fence"] = new("Stone fence", PieceCategory.Walls, 2, 0.5, 1),
        ["stone_arch"] = new("Stone arch", PieceCategory.Walls, 2, 1, 2),
        ["woodwall"] = new("Wood wall", PieceCategory.Walls, 2, 0.2, 2),
        ["wood_wall_half"] = new("Wood wall (half)", PieceCategory.Walls, 2, 0.2, 1),
        ["wood_wall_quarter"] = new("Wood wall (quarter)", PieceCategory.Walls, 2, 0.2, 0.5),
        ["wood_wall_log"] = new("Wood log wall", PieceCategory.Walls, 2, 0.4, 2),
        ["wood_wall_log_4x0.5"] = new("Wood log wall 4x0.5", PieceCategory.Walls, 4, 0.5, 0.5),
        ["stake_wall"] = new("Stake wall", PieceCategory.Walls, 2, 0.5, 2),
        ["darkwood_decowall"] = new("Darkwood decorative wall", PieceCategory.Walls, 2, 0.2, 2),
        ["crystal_wall_1x1"] = new("Crystal wall 1x1", PieceCategory.Walls, 1, 0.2, 1),
        ["piece_dvergr_metal_wall_2x2"] = new("Dvergr metal wall 2x2", PieceCategory.Walls, 2, 0.2, 2),

        ["blackmarble_1x1"] = new("Black marble 1x1", PieceCategory.Blocks, 1, 1, 1, Measured: true),
        ["blackmarble_2x1x1"] = new("Black marble 2x1x1", PieceCategory.Blocks, 2, 1, 1, Measured: true),
        ["blackmarble_2x2x1"] = new("Black marble 2x2x1", PieceCategory.Blocks, 2, 2, 1, Measured: true),
        ["blackmarble_2x2x2"] = new("Black marble 2x2x2", PieceCategory.Blocks, 2, 2, 2, Measured: true),
        ["blackmarble_base_1"] = new("Black marble base", PieceCategory.Blocks, 2, 2, 1),
        ["blackmarble_basecorner"] = new("Black marble base corner", PieceCategory.Blocks, 1, 1, 1),
        ["blackmarble_out_1"] = new("Black marble outer edge", PieceCategory.Blocks, 2, 1, 1),
        ["blackmarble_arch"] = new("Black marble arch", PieceCategory.Blocks, 2, 1, 2),

        ["stone_stair"] = new("Stone stair", PieceCategory.Stairs, 2, 2, 2),
        ["blackmarble_stair"] = new("Black marble stair", PieceCategory.Stairs, 2, 2, 1),
        ["wood_stair"] = new("Wood stair", PieceCategory.Stairs, 1, 2, 2),
        ["piece_dvergr_spiralstair_right"] = new("Dvergr spiral stair", PieceCategory.Stairs, 4, 4, 1, Round: true),

        ["piece_chest_blackmetal"] = new("Black metal chest", PieceCategory.Storage, 2, 1, 1),
        ["piece_chest_barrel"] = new("Barrel", PieceCategory.Storage, 1, 1, 1, Round: true),
        ["piece_chest"] = new("Reinforced chest", PieceCategory.Storage, 1.6, 0.9, 1),
        ["piece_chest_wood"] = new("Wood chest", PieceCategory.Storage, 1.4, 0.8, 0.8),
    };

    static readonly (string Contains, PieceCategory Category, double W, double D)[] Rules =
    [
        ("floor", PieceCategory.Floors, 2, 2),
        ("stair", PieceCategory.Stairs, 2, 2),
        ("ladder", PieceCategory.Stairs, 1, 0.5),
        ("chest", PieceCategory.Storage, 1.4, 0.8),
        ("barrel", PieceCategory.Storage, 1, 1),
        ("workbench", PieceCategory.Crafting, 2, 1),
        ("forge", PieceCategory.Crafting, 2, 1.5),
        ("smelter", PieceCategory.Crafting, 2, 2),
        ("furnace", PieceCategory.Crafting, 2, 2),
        ("kiln", PieceCategory.Crafting, 2, 2),
        ("cauldron", PieceCategory.Crafting, 1.5, 1.5),
        ("stonecutter", PieceCategory.Crafting, 2, 1),
        ("artisan", PieceCategory.Crafting, 2, 2),
        ("fermenter", PieceCategory.Crafting, 1.5, 1.5),
        ("spinningwheel", PieceCategory.Crafting, 1.5, 1),
        ("windmill", PieceCategory.Crafting, 3, 3),
        ("roof", PieceCategory.RoofsAndBeams, 2, 2),
        ("beam", PieceCategory.RoofsAndBeams, 2, 0.2),
        ("pole", PieceCategory.RoofsAndBeams, 0.3, 0.3),
        ("wood_log", PieceCategory.RoofsAndBeams, 2, 0.3),
        ("wall", PieceCategory.Walls, 2, 0.2),
        ("fence", PieceCategory.Walls, 2, 0.2),
        ("gate", PieceCategory.Walls, 4, 0.3),
        ("door", PieceCategory.Walls, 2, 0.3),
        ("arch", PieceCategory.Walls, 2, 1),
        ("blackmarble", PieceCategory.Blocks, 1, 1),
        ("stone_", PieceCategory.Blocks, 1, 1),
        ("bed", PieceCategory.Furnishings, 2, 1),
        ("table", PieceCategory.Furnishings, 2, 1),
        ("bench", PieceCategory.Furnishings, 2, 0.6),
        ("chair", PieceCategory.Furnishings, 0.8, 0.8),
        ("throne", PieceCategory.Furnishings, 1, 1),
        ("rug", PieceCategory.Furnishings, 2, 2),
        ("itemstand", PieceCategory.Furnishings, 0.6, 0.3),
        ("sign", PieceCategory.Furnishings, 1, 0.2),
        ("torch", PieceCategory.Furnishings, 0.4, 0.4),
        ("lantern", PieceCategory.Furnishings, 0.4, 0.4),
        ("brazier", PieceCategory.Furnishings, 0.8, 0.8),
        ("banner", PieceCategory.Furnishings, 1, 0.2),
        ("garland", PieceCategory.Furnishings, 2, 0.2),
        ("hearth", PieceCategory.Furnishings, 3, 2),
        ("fire_pit", PieceCategory.Furnishings, 1.5, 1.5),
        ("bonfire", PieceCategory.Furnishings, 2, 2),
        ("portal", PieceCategory.Other, 2, 0.6),
    ];

    public static PieceInfo Get(string prefab)
    {
        if (Known.TryGetValue(prefab, out var known)) return known;
        var lower = prefab.ToLowerInvariant();
        foreach (var (contains, category, w, d) in Rules)
            if (lower.Contains(contains))
                return new(DisplayName(prefab), category, w, d, 1);
        return new(DisplayName(prefab), PieceCategory.Other, 0.6, 0.6, 1);
    }

    // "piece_groundtorch_wood" -> "Groundtorch wood"
    static string DisplayName(string prefab)
    {
        var name = prefab.StartsWith("piece_", StringComparison.Ordinal) ? prefab[6..] : prefab;
        name = name.Replace('_', ' ').Trim();
        return name.Length == 0 ? prefab : char.ToUpper(name[0], CultureInfo.InvariantCulture) + name[1..];
    }

    // Fill colour by material, so marble, stone, wood and iron read differently on the plan.
    public static string MaterialColour(string prefab)
    {
        var p = prefab.ToLowerInvariant();
        if (p.Contains("blackmarble")) return "#2C2C2A";
        if (p.Contains("crystal")) return "#9FD3E6";
        if (p.Contains("iron") || p.Contains("dvergr") || p.Contains("blackmetal")) return "#6E7B85";
        if (p.Contains("darkwood")) return "#6B4A2B";
        if (p.Contains("stone")) return "#B4B2A9";
        if (p.Contains("wood") || p.Contains("log") || p.Contains("stake")) return "#C9A26B";
        return "#888780";
    }

    public static string Label(this PieceCategory c) => c switch
    {
        PieceCategory.RoofsAndBeams => "Roofs and beams",
        PieceCategory.Furnishings => "Furnishings and lights",
        _ => c.ToString(),
    };
}
