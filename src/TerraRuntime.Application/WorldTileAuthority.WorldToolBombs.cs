using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal bool TryBeginWorldToolBombStep(RuntimeProjectileStore.SimulationPreparation actor,
        VanillaUnifiedRandom1458 live, VanillaUnifiedRandom1458 before,
        out WorldToolBombPreparation? preparation)
    {
        preparation = null;
        if (tiles is null || ToolProjectileOwner is null || !actor.IsCurrent || !actor.CombatTrusted ||
            actor.TrustedOwner.IsAssigned || actor.SourceNpc.IsAssigned || actor.Snapshot.Spawner != byte.MaxValue ||
            !VanillaWorldToolBomb1458.IsSupported(actor.Snapshot.Type) || !actor.Lifecycle.NpcProjectile ||
            worldItemSpawnRandom is not SystemWorldItemSpawnRandom bound || !ReferenceEquals(bound.SourceRandom, live) ||
            !live.HasSameState(before) || !TryCaptureToolTreeWorld(out var context) || context is null ||
            context.World.NoTrapsWorld is null || !players.TryCaptureToolTreeActors(out var actors) || actors is null) return false;
        var overlay = new ToolWorldTileOverlay1458(tiles);
        if (!TryCreateToolMutationPlan(overlay, out var plan) || plan is null) return false;
        if (!TryCreateToolSourcePass(overlay, plan.SourceRandom, false, context.HardMode, context.Facts,
            actors.Actors, plan, out var source) || source is null || !plan.TrySetSourceActorView(actor) ||
            !actor.IsCurrent || !context.IsCurrent || !actors.IsCurrent || !live.HasSameState(before))
        { plan.Dispose(); return false; }
        var candidate = new WorldToolBombPreparation(this, actor, context, actors, plan, source);
        if (!candidate.IsCurrent) { candidate.Dispose(); return false; }
        preparation = candidate;
        return true;
    }

    internal sealed class WorldToolBombPreparation : IDisposable
    {
        private readonly WorldTileAuthority owner;
        private readonly RuntimeProjectileStore.SimulationPreparation actor;
        private readonly ToolTreeWorldCapture1458 context;
        private readonly PlayerAuthority.ToolTreeActorsPreparation actors;
        private readonly ToolWorldMutationPlan plan;
        private readonly ToolWorldSourcePass1458 source;
        private readonly bool golem;
        private ToolWorldPublication1458? publication;
        private bool sealedPlan;
        private bool adopted;
        internal WorldToolBombPreparation(WorldTileAuthority owner, RuntimeProjectileStore.SimulationPreparation actor,
            ToolTreeWorldCapture1458 context, PlayerAuthority.ToolTreeActorsPreparation actors,
            ToolWorldMutationPlan plan, ToolWorldSourcePass1458 source)
        {
            this.owner = owner; this.actor = actor; this.context = context; this.actors = actors;
            this.plan = plan; this.source = source;
            golem = owner.golemDownedBaseline || owner.progression.IsCompleted(VanillaWorldProgressionId.Golem);
        }
        internal VanillaUnifiedRandom1458 Random => plan.SourceRandom;
        internal RuntimePlayerUpdateWorld1458 World => context.World;
        internal bool TryHasHappyBombProximity(float centerX, float centerY, out bool found) =>
            actors.TryHasWorldBombProximity(centerX, centerY, context.World.WindowsItemPrefixArithmetic, out found);
        internal bool IsCurrent => !adopted && actor.IsCurrent && context.IsCurrent && actors.IsCurrent &&
            source.IsCurrentOwned && plan.IsCurrentOwned;

        internal bool TrySetSourceActorView(in ProjectileStateUpdate body, in ProjectileLifecycleState life) =>
            !sealedPlan && IsCurrent && plan.TrySetSourceActorView(actor.Snapshot, in body, in life, true) && IsCurrent;

        internal bool TryApplyPostAiWind(float x, float y, in VanillaProjectileBehaviorContext wind, ref float vx)
        {
            if (!IsCurrent) return false;
            float cx = x + 11f, cy = y + 11f;
            if (!wind.WindPhysics || cy >= context.World.WorldSurface * 16d) return true;
            if (!plan.Overlay.TryRead((int)cx / 16, (int)cy / 16, out var tile)) return false;
            if (tile.Wall != 0) return true;
            float delta = wind.WindSpeedCurrent * wind.WindPhysicsStrength;
            if (((vx > 0f && wind.WindSpeedCurrent < 0f) || (vx < 0f && wind.WindSpeedCurrent > 0f) ||
                Math.Abs(vx) < Math.Abs(delta) * 180f) && Math.Abs(vx) < 16f) vx += delta;
            return float.IsFinite(vx) && IsCurrent;
        }

        internal bool TryRecordMovementReads(in ProjectileSnapshot before, in ProjectileStateUpdate after)
        {
            if (sealedPlan || !IsCurrent) return false;
            const int sourceBody = VanillaWorldToolBomb1458.PhysicalBody;
            float minFx = MathF.Min(before.PositionX, after.PositionX), minFy = MathF.Min(before.PositionY, after.PositionY);
            float maxFx = MathF.Max(before.PositionX, after.PositionX) + sourceBody;
            float maxFy = MathF.Max(before.PositionY, after.PositionY) + sourceBody;
            if (!float.IsFinite(minFx) || !float.IsFinite(minFy) || !float.IsFinite(maxFx) || !float.IsFinite(maxFy)) return false;
            int left = Math.Max(0, (int)(minFx / 16f) - 3), top = Math.Max(0, (int)(minFy / 16f) - 3);
            int right = Math.Min(plan.Overlay.Tiles.Dimensions.WidthTiles - 1, (int)(maxFx / 16f) + 3);
            int bottom = Math.Min(plan.Overlay.Tiles.Dimensions.HeightTiles - 1, (int)(maxFy / 16f) + 3);
            if ((long)(right - left + 1) * (bottom - top + 1) > ToolWorldTileOverlay1458.MaximumChangedCells) return false;
            for (int x = left; x <= right; x++) for (int y = top; y <= bottom; y++)
                if (!plan.Overlay.TryRead(x, y, out _)) return false;
            return IsCurrent;
        }

        internal bool TryCaptureLiquid(float x, float y, in ProjectileLifecycleState life,
            out ProjectileLiquidState liquid, out byte wetCount, out byte colourDraws)
        {
            liquid = default; wetCount = 0; colourDraws = 0;
            if (sealedPlan || !IsCurrent) return false;
            var overlay = plan.Overlay;
            WorldTile Read(int tx, int ty) => overlay.TryRead(tx, ty, out var tile) ? tile : default;
            var contact = VanillaWorldCollision.GetLiquidContacts(overlay.Tiles, x, y,
                VanillaWorldToolBomb1458.PhysicalBody, VanillaWorldToolBomb1458.PhysicalBody, Read);
            bool lava = VanillaWorldCollision.LavaCollision(overlay.Tiles, x, y,
                VanillaWorldToolBomb1458.PhysicalBody, VanillaWorldToolBomb1458.PhysicalBody, Read);
            var before = life.Liquid; int counter = life.SourceWetCount;
            bool shimmer = before.ShimmerWet || contact.Shimmer;
            if (contact.Wet)
            {
                if (!before.Wet && counter == 0 && !lava && shimmer) colourDraws = 10;
                liquid = new(true, before.LavaWet || lava, before.HoneyWet || contact.Honey, shimmer);
            }
            else
            {
                if (before.Wet && counter == 0) { counter = 10; if (!before.LavaWet && before.ShimmerWet) colourDraws = 10; }
                liquid = default;
            }
            wetCount = (byte)Math.Max(0, counter - 1);
            return IsCurrent;
        }

        internal bool TryStageTerrain(in ProjectileKillOrigin origin, ProjectileTypeId type)
        {
            if (sealedPlan || !IsCurrent || !origin.IsValid || !VanillaWorldToolBomb1458.IsSupported(type)) return false;
            if (type.Value == 75) return true; // source HappyBomb Kill has no terrain explosion
            if (!VanillaProjectileTileExplosionFacts1458.TryGet(type, out var definition)) return false;
            int radius = definition.RadiusTiles;
            float originX = origin.X, originY = origin.Y;
            var dimensions = plan.Overlay.Tiles.Dimensions;
            int minX = Math.Max(0, (int)(origin.X / 16f - radius)), maxX = Math.Min(dimensions.WidthTiles - 1, (int)(origin.X / 16f + radius));
            int minY = Math.Max(0, (int)(origin.Y / 16f - radius)), maxY = Math.Min(dimensions.HeightTiles - 1, (int)(origin.Y / 16f + radius));
            bool wallSplode = false;
            for (int x = minX; x <= maxX; x++) for (int y = minY; y <= maxY; y++)
                if (Inside(x, y) && (!plan.Overlay.TryRead(x, y, out var cell) || cell.Wall == 0))
                { if (!plan.Overlay.IsCurrentOwned) return false; wallSplode = true; }
            for (int x = minX; x <= maxX; x++) for (int y = minY; y <= maxY; y++)
            {
                if (!Inside(x, y)) continue;
                if (!plan.Overlay.TryRead(x, y, out var before)) return false;
                bool allowed = !before.IsActive || VanillaProjectileTileExplosionRules1458.CanExplodeTile(in before, y,
                    dimensions.HeightTiles, definition.ExplodeHardmodeOres, context.HardMode, context.Facts.GoodWorld, golem);
                if (before.IsActive && allowed)
                {
                    if (!source.TryKillTile(x, y, false, false, false, out _) || !plan.Overlay.TryRead(x, y, out var after)) return false;
                    if (!after.IsActive && !plan.TryStageSourceTileManipulation(x, y, 0)) return false;
                }
                if (!wallSplode || !allowed) continue;
                for (int wx = x - 1; wx <= x + 1; wx++) for (int wy = y - 1; wy <= y + 1; wy++)
                {
                    if (!plan.Overlay.TryRead(wx, wy, out var wall)) return false;
                    if (wall.Wall == 0 || wall.WallType == VanillaWallIds.UnbreakableTemple) continue;
                    if (!source.TryKillWall(wx, wy, false, out _) || !plan.Overlay.TryRead(wx, wy, out var after)) return false;
                    if (after.Wall == 0 && !plan.TryStageSourceTileManipulation(wx, wy, 2)) return false;
                }
            }
            return IsCurrent;
            bool Inside(int x, int y)
            {
                float dx = MathF.Abs(x - originX / 16f), dy = MathF.Abs(y - originY / 16f);
                double square = context.World.WindowsItemPrefixArithmetic ? (double)dx * dx + (double)dy * dy : dx * dx + dy * dy;
                return Math.Sqrt(square) < radius;
            }
        }

        internal bool TrySeal()
        {
            if (sealedPlan || !IsCurrent || !plan.TrySeal() || !plan.TryBindSourceActor(actor) || !IsCurrent) return false;
            sealedPlan = true; return true;
        }
        internal bool TryAdoptUnpublished()
        {
            if (!sealedPlan || adopted || !actor.IsPublicationCurrent || !context.IsCurrent || !actors.IsCurrent ||
                !source.IsCurrentOwned || !plan.IsCurrentOwned || !plan.TryAdoptWholeUnpublished(out publication)) return false;
            adopted = true; return true;
        }
        internal bool TryPublish()
        {
            if (!adopted || publication is null) return false;
            return publication.TryPublishThrough(plan.CapturePublicationOffset());
        }
        public void Dispose() => plan.Dispose();
    }
}
