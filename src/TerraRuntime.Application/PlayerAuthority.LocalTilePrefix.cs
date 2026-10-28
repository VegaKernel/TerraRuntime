using TerraRuntime.Core;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    private bool TryAdvanceLocalTilePrefix(RemoteItemPhasePlan1458 plan,
        in RuntimePlayerUpdateWorld1458 world, VanillaUnifiedRandom1458 random)
    {
        if (plan.BeforeItemUseCaller != RuntimeItemUseCaller1458.LocalOwnerProjection) return true;
        // Player.Update calls CheckIceBreak/CheckCrackedBrickBreak before dry
        // movement and ItemCheck. This ordinary phase owns their entropy only
        // when these missing tile-mutation branches cannot participate.
        var tiles = worldTiles;
        if (tiles is null) return false;
        int x = (int)((plan.X + 10f) / 16f), y = (int)((plan.Y + 21f) / 16f);
        // Both helpers inspect the current bottom rows at predicted horizontal
        // position. Include the current feet (the ordinary-support veto), the
        // cracked expansion24px and a tile for pre-collision acceleration.
        float predictedX = plan.X + plan.Vx;
        double predictedLeft = ((double)predictedX - 40d) / 16d;
        double predictedRight = ((double)predictedX + 60d) / 16d;
        if (!float.IsFinite(predictedX) || predictedLeft < int.MinValue || predictedRight > int.MaxValue) return false;
        int minY = Math.Max(0, (int)((plan.Y + 26f) / 16f));
        int maxY = Math.Min(world.MaxTilesY - 1, (int)((plan.Y + 90f) / 16f));
        int width = world.MaxTilesX;
        bool UnsafeFootprint(int left, int right)
        {
            for (int tileX = Math.Max(0, left); tileX <= Math.Min(width - 1, right); tileX++)
            for (int tileY = minY; tileY <= maxY; tileY++)
            {
                var tile = tiles.Get(tileX, tileY);
                if (tile.IsActive && tile.Type is 162 or 481 or 482 or 483) return true;
            }
            return false;
        }
        // Check the two fixed-size footprints separately; a fast source actor
        // does not inspect every tile between its current and predicted feet.
        if (UnsafeFootprint((int)((plan.X - 16f) / 16f), (int)((plan.X + 36f) / 16f)) ||
            UnsafeFootprint((int)predictedLeft, (int)predictedRight)) return false;
        // WorldGen.InWorld(center,10) precedes both offers. Empty terrain and
        // insufficient speed still spend both; skipping them shifts paid ammo.
        var placementHistory = plan.BeforeItem?.ToolUse?.PlacementAlternative;
        // Only a retained placement branch needs a replay witness. Verify its
        // exact prior endpoint before spending caller entropy; never recover a
        // branch whose owner has already consumed unrelated samples.
        bool placementPreimage = placementHistory is not null && placementHistory.MatchesCanonical(random);
        bool offers = x >= 10 && x < world.MaxTilesX - 10 && y >= 10 && y < world.MaxTilesY - 10;
        if (offers)
        {
            random.Next(2, 12);
            random.Next(2, 12);
        }
        if (placementPreimage)
        {
            plan.PlacementPrefixHistory = placementHistory;
            plan.PlacementPrefixAfter = random.Clone();
            plan.PlacementPrefixOffers = offers;
        }
        return true;
    }
}
