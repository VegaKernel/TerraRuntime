using TerraRuntime.World;
using TerraRuntime.Gameplay.Tiles;

namespace TerraRuntime.Application;

internal enum ToolTreeTopRoll1458 : byte { None, Palm, Mushroom, PalmAfterKill }
internal readonly record struct ToolTreeFrame1458(
    bool Kill, short FrameX, short FrameY, bool FrameOrthogonalNeighbours, ToolTreeTopRoll1458 TopRoll);

/// <summary>
/// Source tree support/frame decisions over the already captured working image.
/// A Kill is executed before a PalmAfterKill continuation; it is not a deferred
/// ordinary tile clear. RNG offers are executed by the ordered world plan.
/// </summary>
internal static class VanillaToolTreeFrames1458
{
    private const ushort Common = 5, Mushroom = 72, Palm = 323;
    private const ushort FirstGem = 583, LastGem = 589, Sakura = 596, Willow = 616, Ash = 634;
    private const ushort MushroomGrass = 70;
    private const int FrameStep = 22, VariantRows = 66, RootRows = 132, LeafRows = 198;

    public static bool TryGetGroundValidity(ushort treeType, in WorldTile below, out bool accepted)
    {
        accepted = false;
        if (!below.IsActive) return true;
        if (!VanillaToolTileCatalog1458.TryGet(below.TileType, out var facts)) return false;
        const ushort Grass = 2, CorruptGrass = 23, GolfGrass = 477, JungleGrass = 60, MushroomGrass = 70;
        const ushort HallowedGrass = 109, Snow = 147, CrimsonGrass = 199, HallowedGolfGrass = 492;
        const ushort CorruptJungleGrass = 661, CrimsonJungleGrass = 662, AshGrass = 633;
        const ushort Sand = 53, Ebonsand = 112, Pearlsand = 116, Crimsand = 234;
        if (treeType == Common)
            accepted = below.Type is Grass or CorruptGrass or GolfGrass or JungleGrass or MushroomGrass or
                HallowedGrass or Snow or CrimsonGrass or HallowedGolfGrass or CorruptJungleGrass or CrimsonJungleGrass;
        else if (treeType is >= FirstGem and <= LastGem)
            accepted = facts.ConvertsAsStone || facts.ConvertsAsMoss;
        else if (treeType is Sakura or Willow)
            accepted = facts.ConvertsAsGrass && below.Type is not (CorruptGrass or CrimsonGrass);
        else if (treeType == Ash)
            accepted = below.Type == AshGrass;
        else if (treeType == Palm)
            accepted = below.Type is Sand or Ebonsand or Pearlsand or Crimsand;
        else if (treeType == Mushroom)
            accepted = below.Type == MushroomGrass;
        else return false;
        return true;
    }

    public static bool TryPlan(in WorldTile current, in WorldTile above, in WorldTile below,
        in WorldTile left, in WorldTile right, bool acceptedGround, out ToolTreeFrame1458 result)
    {
        result = default;
        if (!current.IsActive || current.FrameX < 0 || current.FrameY < 0) return false;
        short fx = current.FrameX, fy = current.FrameY;
        bool up = above.IsActive && above.Type == current.Type;
        bool down = below.IsActive && below.Type == current.Type;
        bool l = left.IsActive && left.Type == current.Type;
        bool r = right.IsActive && right.Type == current.Type;
        if (current.Type == Mushroom)
        {
            bool supported = down || (below.IsActive && below.Type == MushroomGrass);
            result = !supported ? Killed() : new(false, fx, fy, false,
                !up && fx == 0 ? ToolTreeTopRoll1458.Mushroom : ToolTreeTopRoll1458.None);
            return true;
        }
        if (current.Type == Palm)
        {
            bool kill = (!acceptedGround && !down) || ((fx is 66 or 220) && !acceptedGround);
            if (kill)
                result = new(true, 0, 0, false, !up ? ToolTreeTopRoll1458.PalmAfterKill : ToolTreeTopRoll1458.None);
            else if (!up && fx <= 44)
                result = new(false, fx, fy, false, ToolTreeTopRoll1458.Palm);
            else
                result = new(false, !up && fx == 66 ? (short)220 : fx, fy, false, ToolTreeTopRoll1458.None);
            return true;
        }
        if (current.Type is not (Common or Sakura or Willow or Ash) &&
            current.Type is not (>= FirstGem and <= LastGem)) return false;

        bool straightBase = (fx is 0 or 22 or 44) && fy <= 130;
        bool rootWing = (fx is >= 22 and <= 44) && (fy is >= RootRows and <= 176);
        if ((!acceptedGround && !down && straightBase) ||
            (rootWing && (!acceptedGround || !((fx == FrameStep && l) || (fx == 2 * FrameStep && r)))))
        {
            result = Killed();
            return true;
        }
        if (!rootWing && IsBranchConnector(fx, fy))
        {
            int variant = fy % VariantRows;
            (fx, fy) = (l, r) switch
            {
                (true, true) => ((short)110, (short)(VariantRows + variant)),
                (true, false) => ((short)88, (short)variant),
                (false, true) => ((short)66, (short)(VariantRows + variant)),
                _ => ((short)0, (short)variant)
            };
        }
        if ((fy is >= RootRows and <= 176) && (fx is 0 or 66 or 88))
        {
            if (!acceptedGround) { result = Killed(); return true; }
            int variant = fy % VariantRows;
            (fx, fy) = (l, r) switch
            {
                (false, false) => ((short)0, (short)variant),
                (false, true) => ((short)0, (short)(RootRows + variant)),
                (true, false) => ((short)66, (short)(RootRows + variant)),
                _ => ((short)88, (short)(RootRows + variant))
            };
        }
        bool rightAttached = (fx == 66 && fy is 0 or 22 or 44) || (fx == 44 && fy is 198 or 220 or 242);
        bool leftAttached = (fx == 88 && fy is 66 or 88 or 110) || (fx == 66 && fy is 198 or 220 or 242);
        if ((rightAttached && !r) || (leftAttached && !l) || (!rightAttached && !leftAttached && !below.IsActive))
        {
            result = Killed();
            return true;
        }
        if (!rightAttached && !leftAttached && !up && fy < LeafRows &&
            ((fx != 22 && fx != 44) || fy < RootRows))
        {
            int variant = fy % VariantRows;
            if (l || r)
            {
                fx = down ? (short)132 : (short)154;
                fy = checked((short)((l && r ? RootRows : l ? 0 : VariantRows) + variant));
            }
            else { fx = 110; fy = checked((short)variant); }
        }
        result = new(false, fx, fy,
            fx != current.FrameX && fy != current.FrameY, ToolTreeTopRoll1458.None);
        return true;
    }

    private static bool IsBranchConnector(short x, short y) =>
        (x == 88 && y is >= 0 and <= 44) || (x == 66 && y is >= 66 and <= 130) ||
        (x == 110 && y is >= 66 and <= 110) || (x == 132 && y is >= 0 and <= 176);

    private static ToolTreeFrame1458 Killed() => new(true, 0, 0, false, ToolTreeTopRoll1458.None);
}
