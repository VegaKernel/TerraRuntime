using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    /// <summary>
    /// CheckSign for55/425/510/511/573 in fresh generation, TerrariaServer1.4.5.8.
    /// The current Workspace has no generated sign-text surface and fresh materialization publishes
    /// an empty sign table, so KillSign has nothing to remove. Admitting generated text must first
    /// bind removal to that same retained metadata owner; this is not an imported-world sign framer.
    /// Tombstone85 can spawn NPC316 without a generation guard; ItemFrame395 owns a tile entity.
    /// </summary>
    private void SignCheckFraming(int i, int j, ushort type)
    {
        if (destroyingObject)
            return;
        WorldTile observed = At(i, j);
        int left = i - observed.FrameX / 18 % 2;
        int top = j - observed.FrameY / 18 % 2;
        if (!Contains(left - 1, top - 1) || !Contains(left + 2, top + 2))
            return;
        WorldTile origin = At(left, top);
        int orientation = origin.FrameX / 18 / 2;
        int verticalStyle = origin.FrameY / 18 / 2;
        bool broken = false;
        for (int column = 0; column < 2; column++)
        for (int row = 0; row < 2; row++)
        {
            WorldTile member = At(left + column, top + row);
            // The source compares frame quotients, then normalizes pixel remainders on success.
            if (!member.IsActive || member.Type != type ||
                member.FrameX / 18 != orientation * 2 + column ||
                member.FrameY / 18 != verticalStyle * 2 + row)
                broken = true;
        }
        if (!broken)
        {
            int previous = Math.Max(0, observed.FrameX / 18 / 2);
            if (!SignHasAnchor(previous, left, top))
            {
                orientation = 0;
                while (orientation < 5 && !SignHasAnchor(orientation, left, top))
                    orientation++;
                broken = orientation == 5;
            }
        }
        if (broken)
        {
            destroyingObject = true;
            for (int x = left; x < left + 2; x++)
            for (int y = top; y < top + 2; y++)
                if (At(x, y).Type == type)
                    KillTile(x, y);
            destroyingObject = false;
            // Source CheckSign adds no separate halo after the matching-member KillTile calls.
            return;
        }
        for (int column = 0; column < 2; column++)
        for (int row = 0; row < 2; row++)
        {
            ref WorldTile member = ref At(left + column, top + row);
            member.Flags |= WorldTileFlags.Active;
            member.Type = type;
            member.FrameX = (short)(orientation * 36 + column * 18);
            member.FrameY = (short)(verticalStyle * 36 + row * 18);
        }
    }

    private bool SignHasAnchor(int orientation, int x, int y) => orientation switch
    {
        0 => SignFloorEdge(x, y + 2) && SignFloorEdge(x + 1, y + 2),
        1 => SignCeilingEdge(x, y - 1) && SignCeilingEdge(x + 1, y - 1),
        2 => SignSideEdge(x - 1, y, leftOfSign: true) && SignSideEdge(x - 1, y + 1, leftOfSign: true),
        3 => SignSideEdge(x + 2, y, leftOfSign: false) && SignSideEdge(x + 2, y + 1, leftOfSign: false),
        4 => At(x, y).Wall > 0 && At(x + 1, y).Wall > 0 && At(x, y + 1).Wall > 0 && At(x + 1, y + 1).Wall > 0,
        _ => false
    };

    private bool SignFloorEdge(int x, int y)
    {
        WorldTile tile = At(x, y);
        return tile.IsActive && !tile.IsActuated && !tile.IsHalfBrick &&
            (IsFramingSolid(tile.TileType) || VanillaTileCollisionCatalog.IsSolidTop(tile.TileType)) &&
            (tile.SourceSlope is not (1 or 2) ||
                VanillaTileIds.IsPlatform(tile.TileType) && PlatformProperTopFrame(tile.FrameX));
    }

    private bool SignCeilingEdge(int x, int y)
    {
        WorldTile tile = At(x, y);
        // BottomEdgeCanBeAttachedTo intentionally permits a half-brick ceiling.
        return tile.IsActive && !tile.IsActuated && tile.SourceSlope is not (3 or 4) &&
            IsFramingSolid(tile.TileType) &&
            !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType) &&
            !GenerationObjectSupport1458.DisallowsAttachment(tile.Type);
    }

    private bool SignSideEdge(int x, int y, bool leftOfSign)
    {
        WorldTile tile = At(x, y);
        bool blockedSlope = leftOfSign ? tile.SourceSlope is 1 or 3 : tile.SourceSlope is 2 or 4;
        return tile.IsActive && !tile.IsActuated && !tile.IsHalfBrick && !blockedSlope &&
            IsFramingSolid(tile.TileType) &&
            !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType) &&
            !GenerationObjectSupport1458.DisallowsAttachment(tile.Type);
    }
}
