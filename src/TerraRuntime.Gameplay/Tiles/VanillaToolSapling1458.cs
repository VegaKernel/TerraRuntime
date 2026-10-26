using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Tiles;

/// <summary>TileObjectData20 and Check1x2 source placement-style identity; no world permission or inventory authority.</summary>
public static class VanillaToolSapling1458
{
    public const ushort TileType = 20;
    public const int Width = 1;
    public const int Height = 2;
    public const int OriginY = 1;
    public const int RandomStyleRange = 3;
    public const int FrameCell = 18;
    public const int FrameGroup = 54;
    public const int VerticalFrameGroup = 40;
    public static bool IsBodyReplaceable(in ToolTileDefinition1458 facts, TileTypeId type) =>
        (facts.IsCuttable && type.Value is not (484 or 654)) || facts.BreakableWhenPlacing;
    public static bool IsKilledDuringPlace(in ToolTileDefinition1458 facts, TileTypeId type) =>
        type.Value != 484 && (facts.IsCuttable || facts.BreakableWhenPlacing);
    public static bool TryGetAlternate(TileTypeId ground, out byte alternate)
    {
        int selected = ground.Value switch
        {
            2 or 477 => 0, 147 => 1, 60 => 2, 23 or 661 => 3, 199 or 662 => 4,
            109 or 492 => 5, 53 => 6, 116 => 7, 234 => 8, 112 => 9, 633 => 10, _ => -1
        };
        alternate = selected >= 0 ? (byte)selected : default;
        return selected >= 0;
    }
}
