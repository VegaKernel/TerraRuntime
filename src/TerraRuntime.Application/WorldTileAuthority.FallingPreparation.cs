using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal bool TryBeginFallingActor(in ProjectileSnapshot projectile,
        VanillaUnifiedRandom1458 liveRandom, out FallingActorPreparation1458? preparation)
    {
        preparation = null;
        if (tiles is null || ToolProjectileOwner is null ||
            !VanillaFallingBlock1458.TryGetTile(projectile.Type, out _) || projectile.Spawner != byte.MaxValue ||
            worldItemSpawnRandom is not SystemWorldItemSpawnRandom random ||
            !ReferenceEquals(random.SourceRandom, liveRandom) ||
            !TryCaptureToolTreeWorld(out var context) || context is null ||
            !players.TryCaptureToolTreeActors(out var actors) || actors is null) return false;
        var overlay = new ToolWorldTileOverlay1458(tiles);
        if (!context.IsCurrent || !TryCreateToolMutationPlan(overlay, out var plan) || plan is null) return false;
        if (!TryCreateToolSourcePass(overlay, plan.SourceRandom, false, context.HardMode,
            context.Facts, actors.Actors, plan, out var source) || source is null)
        { plan.Dispose(); return false; }
        preparation = new(this, in projectile, context, actors, plan, source);
        return preparation.IsCurrent;
    }

    internal sealed class FallingActorPreparation1458 : IDisposable
    {
        private readonly WorldTileAuthority owner;
        private readonly ProjectileSnapshot projectile;
        private readonly ToolTreeWorldCapture1458 context;
        private readonly PlayerAuthority.ToolTreeActorsPreparation actors;
        private readonly ToolWorldMutationPlan plan;
        private readonly ToolWorldSourcePass1458 source;
        private readonly List<FallingWorldBoundary1458> boundaries = [];
        private WorldTile[] finalTargets = [];
        private PlayerAuthority.ToolShapePlayerPreparation? shape;
        private ToolWorldPublication1458? publication;
        private ToolWorldPublicationOffset1458 finalEffects;
        private int killBoundary = -1, next;
        private bool sealedPlan, adopted, disposed, finalEffectsPublished;

        internal FallingActorPreparation1458(WorldTileAuthority owner, in ProjectileSnapshot projectile,
            ToolTreeWorldCapture1458 context, PlayerAuthority.ToolTreeActorsPreparation actors,
            ToolWorldMutationPlan plan, ToolWorldSourcePass1458 source)
        {
            this.owner = owner; this.projectile = projectile; this.context = context;
            this.actors = actors; this.plan = plan; this.source = source;
        }

        internal VanillaUnifiedRandom1458 Random => plan.SourceRandom;
        internal ToolWorldTileOverlay1458 Terrain => plan.Overlay;
        internal bool IsCurrent => !disposed && !adopted && context.IsCurrent && actors.IsCurrent &&
            source.IsCurrentOwned && plan.IsCurrentOwned && (shape is null || shape.IsCurrent);

        internal bool TrySetSourceActorView(in ProjectileStateUpdate body,
            in ProjectileLifecycleState lifecycle, bool active) =>
            !sealedPlan && IsCurrent && plan.TrySetSourceActorView(in projectile, in body, in lifecycle, active) && IsCurrent;

        internal bool TryStageAiTiles(bool tileCollide, in ProjectileStateUpdate body,
            in ProjectileLifecycleState life, ref float y) =>
            !sealedPlan && IsCurrent && (!tileCollide ||
                source.TryStageFallingAiTiles(in projectile, in body, in life, ref y, boundaries)) && IsCurrent;

        internal bool TryStageCuts(in ProjectileStateUpdate body, bool collision, bool npcProjectile) =>
            !sealedPlan && IsCurrent && source.TryStageFallingCuts(in body, collision, npcProjectile, boundaries) && IsCurrent;

        internal bool TryStageLanding(in ProjectileSnapshot killed)
        {
            if (sealedPlan || !IsCurrent || killBoundary >= 0 || killed.Handle != projectile.Handle ||
                killed.Type != projectile.Type || killed.Spawner != byte.MaxValue) return false;
            killBoundary = boundaries.Count;
            return source.TryStageFallingLanding(in killed, context.World.OnlyShimmerOceanWorlds, boundaries) && IsCurrent;
        }

        internal bool TrySeal()
        {
            if (sealedPlan || !IsCurrent || !plan.TrySeal() || !IsCurrent ||
                !owner.players.TryPrepareToolShapeEffects(plan.ShapeEffects, out shape) || shape is null ||
                !shape.TrySealForWorldAdoption()) return false;
            if (killBoundary < 0) killBoundary = boundaries.Count;
            finalEffects = plan.CapturePublicationOffset();
            finalTargets = new WorldTile[boundaries.Count];
            for (int index = 0; index < boundaries.Count; index++)
            {
                var wire = boundaries[index].State;
                if (!plan.Overlay.TryRead(wire.TileX, wire.TileY, out finalTargets[index])) return false;
            }
            sealedPlan = true;
            return IsCurrent;
        }

        internal bool TryBindSourceActor(RuntimeProjectileStore.SimulationPreparation actor) =>
            sealedPlan && IsCurrent && plan.TryBindSourceActor(actor) && IsCurrent;

        internal bool TryAdoptUnpublished()
        {
            if (!sealedPlan || !IsCurrent || !plan.TryAdoptWholeUnpublished(out publication)) return false;
            if (!shape!.TryAdoptUnpublished())
                throw new InvalidOperationException("A falling world participant lost callback-free adoption.");
            adopted = true;
            return true;
        }

        // AI world calls precede source29; landing calls/items follow it. All
        // accepted owners have already adopted before either phase can publish.
        internal bool TryPublishNext(bool afterKill)
        {
            if (!adopted || publication is null) return false;
            int limit = afterKill ? boundaries.Count : killBoundary;
            if (next < limit)
            {
                int index = next++;
                var boundary = boundaries[index];
                var end = boundary.End;
                var state = boundary.State;
                Exception? first = null;
                try
                {
                    if (!publication.TryPublishThrough(in end))
                        throw new InvalidOperationException("Accepted falling publication boundary is inconsistent.");
                }
                catch (Exception error) { first = error; }
                try
                {
                    if (owner.tiles is { } tiles && tiles.Get(state.TileX, state.TileY).Equals(finalTargets[index]))
                        owner.TryPublishToolTile(GameCommandSourceId.System, default, in state);
                    else owner.replication?.TryPublishTileSquareToAll(owner.tiles!, state.TileX, state.TileY);
                }
                catch (Exception error) { first ??= error; }
                if (first is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(first).Throw();
                return true;
            }
            if (!afterKill) return false;
            if (!finalEffectsPublished)
            {
                finalEffectsPublished = true;
                if (!publication.TryPublishThrough(in finalEffects))
                    throw new InvalidOperationException("Accepted falling final effects are inconsistent.");
                return true;
            }
            return shape!.TryPublishNext();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            plan.Dispose();
        }
    }
}
