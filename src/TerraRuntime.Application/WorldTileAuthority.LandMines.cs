using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal static class VanillaLandMine1458
{
    internal const int BodySize = 128;
    internal const int RetiredBodySize = 22;
    internal const short Damage = 250;
    internal const float KnockBack = 10f;
}

internal sealed partial class WorldTileAuthority
{
    private void ApplyNonTerminatingWiringContact(ushort type, int x, int y)
    {
        if (type is WorldWiringDeviceExecutor1458.LandMineTileType or WorldWiringDeviceExecutor1458.GeyserTileType &&
            (uint)x <= short.MaxValue && (uint)y <= short.MaxValue && wiring?.HitSwitch(x, y) == true)
            replication?.TryPublishHitSwitch(default, new((short)x, (short)y));
    }

    private bool TryExplodeLandMine(int x, int y)
    {
        if (tiles is null || ToolProjectileOwner is not { } projectiles ||
            (uint)x > short.MaxValue || (uint)y > short.MaxValue ||
            !TryCaptureToolTreeWorld(out var context) || context is null ||
            !players.TryCaptureToolTreeActors(out var actors) || actors is null) return false;
        var overlay = new ToolWorldTileOverlay1458(tiles);
        if (!overlay.TryReadSourceSafely(x, y, out var mine) ||
            !mine.IsActive || mine.Type != 210 || mine.FrameX != 0 || mine.FrameY != 0 ||
            !TryCreateToolMutationPlan(overlay, out var plan) || plan is null) return false;
        using (plan)
        {
            var state = new ProjectileStateUpdate(new(164), byte.MaxValue,
                x * 16 + 8 - VanillaLandMine1458.BodySize / 2,
                y * 16 + 8 - VanillaLandMine1458.BodySize / 2,
                0, 0, default, 0, VanillaLandMine1458.Damage, VanillaLandMine1458.KnockBack, 0);
            // ExplodeMine runs KillTile(noItem), sends the cleared cell, then
            // creates the projectile. The journal keeps this literal 20/27 order.
            if (!TryCreateToolSourcePass(overlay, plan.SourceRandom, false, context.HardMode,
                    context.Facts, actors.Actors, plan, out var source) || source is null ||
                !source.TryKillTile(x, y, false, false, true, out var kill) || !kill.Changed || kill.SourceFailed ||
                !overlay.TryRead(x, y, out var cleared) || cleared.IsActive ||
                !CanProcessLandMineDamageTerrain(x, y, overlay) ||
                !plan.TryStageSourceTileSquare(x, y) ||
                !plan.TryStageSourceProjectile(projectiles, in state, false, out var birth) ||
                !plan.TrySeal() || !context.IsCurrent || !actors.IsCurrent || !source.IsCurrentOwned ||
                !plan.TryAdoptWholeUnpublished(out var published) || published is null) return false;
            if (toolProjectileAuthority?.TryScheduleWiringTrapBirth(in birth) == false)
                throw new InvalidOperationException("Accepted landmine lost its projectile census entry.");
            var end = plan.CapturePublicationOffset();
            if (!published.TryPublishThrough(in end))
                throw new InvalidOperationException("Accepted landmine lost its ordered destruction and birth publication.");
            return true;
        }
    }

    // The source bomb Damage phase can cut tiles. This bounded owner admits its
    // dry, no-cut branch over the exact CutTilesAt stencil.
    internal bool CanProcessLandMineDamageTerrain(int x, int y, ToolWorldTileOverlay1458? overlay = null)
    {
        const int tileRadius = VanillaLandMine1458.BodySize / 32;
        if (tiles is null || x < tileRadius || y < tileRadius ||
            x >= tiles.Dimensions.WidthTiles - tileRadius || y >= tiles.Dimensions.HeightTiles - tileRadius) return false;
        for (int tx = x - tileRadius; tx <= x + tileRadius; tx++)
        for (int ty = y - tileRadius; ty <= y + tileRadius; ty++)
        {
            WorldTile tile;
            if (overlay is null) tile = tiles.Get(tx, ty);
            else if (!overlay.TryReadSourceSafely(tx, ty, out tile)) return false;
            if (tile.LiquidAmount != 0 || tile.IsActive &&
                (!VanillaToolTileCatalog1458.TryGet(tile.TileType, out var facts) || facts.IsCuttable)) return false;
        }
        return true;
    }
}
