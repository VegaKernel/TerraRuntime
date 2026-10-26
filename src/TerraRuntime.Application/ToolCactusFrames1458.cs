using TerraRuntime.World;

namespace TerraRuntime.Application;

internal readonly record struct ToolCactusFrame1458(short X, short Y, bool ReframeCosmeticNeighbours);

/// <summary>Source CheckCactus/CactusFrame decisions after the ordered plan resolves the root column.</summary>
internal static class VanillaToolCactusFrames1458
{
    private const ushort Cactus = 80, CactusFruit = 227;
    private const ushort Sand = 53, Ebonsand = 112, Pearlsand = 116, Crimsand = 234;

    public static bool ShouldKill(in WorldTile rootGround, int relativeColumn,
        in WorldTile below, in WorldTile left, in WorldTile right)
    {
        bool rootValid = rootGround.IsActive && (rootGround.Flags & WorldTileFlags.Inactive) == 0 &&
            !rootGround.IsHalfBrick && rootGround.SourceSlope == 0 && IsSand(rootGround.Type);
        if (!rootValid) return true;
        if (relativeColumn != 0)
            return !IsCactus(in below) && !IsCactus(in left) && !IsCactus(in right);
        return !below.IsActive || (below.Type != Cactus && !IsSand(below.Type));
    }

    public static ToolCactusFrame1458 PlanFrame(in WorldTile current, int relativeColumn,
        in WorldTile twoLeft, in WorldTile left, in WorldTile right, in WorldTile above,
        in WorldTile below, in WorldTile leftBelow, in WorldTile rightBelow, bool belowSolid)
    {
        bool l = IsCactus(in left), r = IsCactus(in right), d = IsCactus(in below);
        bool u = above.IsActive && above.Type is Cactus or CactusFruit;
        short x = current.FrameX, y = current.FrameY;
        if (relativeColumn == 0)
        {
            bool leftArm = l && !IsCactus(in leftBelow) && !IsCactus(in twoLeft);
            bool rightArm = r && !IsCactus(in rightBelow);
            x = (leftArm, rightArm) switch
            {
                (true, true) => 90,
                (true, false) => 72,
                (false, true) => 18,
                _ => 0
            };
            y = !u ? (short)0 : x != 0 || (below.IsActive && belowSolid) ? (short)36 : (short)18;
        }
        else if (relativeColumn == -1)
        {
            x = r && !u && !d ? (short)108 : (short)54;
            y = r && !d ? (short)36 : !u ? (short)0 : (short)18;
        }
        else if (relativeColumn == 1)
        {
            x = l && !u && !d ? (short)108 : (short)36;
            y = l && !u && !d ? (short)18 : l && !d ? (short)36 : !u ? (short)0 : (short)18;
        }
        // Cactus uses OR and cosmetic recursion in left/up/down/right order;
        // ordinary trees use AND and full framing in left/right/up/down order.
        return new(x, y, x != current.FrameX || y != current.FrameY);
    }

    private static bool IsCactus(in WorldTile tile) => tile.IsActive && tile.Type == Cactus;
    private static bool IsSand(ushort type) => type is Sand or Ebonsand or Pearlsand or Crimsand;
}
