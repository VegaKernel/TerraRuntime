using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Tiles;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    private bool framingRopeEnds;

    private void FrameTrack(int x, int y)
    {
        MinecartTrackNeighbors1458 neighbours = MinecartTrackNeighbors1458.None;
        for (int side = 0; side < 2; side++)
        for (int row = 0; row < 3; row++)
            // Minecart reads the type of all six neighbours even when they are inactive.
            if (At(x + (side == 0 ? -1 : 1), y + row - 1).Type == 314)
                neighbours |= (MinecartTrackNeighbors1458)(1 << (side * 3 + row));
        var input = new MinecartTrackFrameInput1458(At(x, y).FrameX, At(x, y).FrameY, neighbours);
        if (VanillaMinecartTrackFrame1458.TryPlan(in input, pound: false, mute: false, out var plan))
        {
            At(x, y).FrameX = plan.Front;
            At(x, y).FrameY = plan.Back;
        }
        FrameRopeEnds(x, y);
    }

    private static bool IsGenerationRope(ushort type) => type is 213 or 214 or 353 or 365 or 366 or 504 or 449 or 450 or 451;

    private void FrameRopeEnds(int x, int y)
    {
        FrameRopeEndsCore(x, y);
        // Source HandleRopeEndFraming resets this even when the nested call returned early.
        framingRopeEnds = false;
    }

    private void FrameRopeEndsCore(int x, int y)
    {
        if (framingRopeEnds)
            return;
        framingRopeEnds = true;
        if (x < 1 || y < 1 || x >= width - 1 || y >= height - 1)
            return;
        var above = At(x, y - 1);
        var below = At(x, y + 1);
        // The first source arm deliberately combines above.active with BELOW.type == 314.
        bool upperEdge = above.IsActive && (below.Type == 314 ||
            VanillaTileIds.IsPlatform(above.TileType) || above.Type == 380 || IsGenerationRope(above.Type));
        bool lowerEdge = !upperEdge && below.IsActive && (below.Type == 314 ||
            VanillaTileIds.IsPlatform(below.TileType) || below.Type == 380 || IsGenerationRope(below.Type));
        if (!upperEdge && !lowerEdge)
            return;

        int top = -1, bottom = -1;
        for (int distance = 1; distance <= 5 && Contains(x, y - distance); distance++)
        {
            var candidate = At(x, y - distance);
            if (!candidate.IsActive || IsGenerationRope(candidate.Type))
            {
                top = y - distance;
                break;
            }
        }
        for (int distance = 1 + y - top; distance <= 6 && Contains(x, top + distance); distance++)
        {
            var candidate = At(x, top + distance);
            if (!candidate.IsActive || IsGenerationRope(candidate.Type))
            {
                bottom = top + distance;
                break;
            }
        }
        if (top != -1 && top != y)
            TileFrame(x, top);
        if (bottom != -1 && bottom != y)
            TileFrame(x, bottom);
    }
}
