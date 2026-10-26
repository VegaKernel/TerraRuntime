using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.World;
using TerraRuntime.Core;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal bool TryBeginToolProjectileCuts(HeldToolOwnerPreparation1458 actor,
        in ProjectileSnapshot projectile, VanillaUnifiedRandom1458 live,
        VanillaUnifiedRandom1458 beforeRandom, VanillaUnifiedRandom1458 afterAi,
        bool windows, out ToolProjectileCutsPreparation1458? preparation)
    {
        preparation = null;
        if (tiles is null || actor.Inactive || !actor.IsCurrent ||
            actor.World.WindowsItemPrefixArithmetic != windows ||
            worldItemSpawnRandom is not SystemWorldItemSpawnRandom worldRandom ||
            !ReferenceEquals(worldRandom.SourceRandom, live) || !live.HasSameState(beforeRandom) ||
            projectile.Spawner != actor.Player.Slot.Value ||
            !VanillaToolProjectileDefinition1458.TryGet(projectile.Type, out _) ||
            !TryCaptureRetainedToolWorld(actor, out var context) || context is null ||
            !players.TryCaptureToolTreeActors(out var actors) || actors is null) return false;
        var overlay = new ToolWorldTileOverlay1458(tiles);
        if (!TryCreateToolMutationPlan(overlay, out var plan) || plan is null) return false;
        plan.SourceRandom.CopyStateFrom(afterAi);
        if (!TryCreateToolSourcePass(overlay, plan.SourceRandom, false, context.HardMode,
                context.Facts, actors.Actors, plan, out var source) || source is null)
        { plan.Dispose(); return false; }
        preparation = new(this, actor, projectile, context, actors, overlay, plan, source, afterAi);
        return preparation.IsCurrent;
    }

    internal sealed class ToolProjectileCutsPreparation1458 : IDisposable
    {
        private const int MaximumOuterCalls = 192;
        private readonly WorldTileAuthority owner;
        private readonly HeldToolOwnerPreparation1458 actor;
        private readonly ProjectileSnapshot projectile;
        private readonly ToolProjectileWorldCapture1458 context;
        private readonly PlayerAuthority.ToolTreeActorsPreparation actors;
        private readonly ToolWorldTileOverlay1458 overlay;
        private readonly ToolWorldMutationPlan plan;
        private readonly ToolWorldSourcePass1458 source;
        private readonly VanillaUnifiedRandom1458 actorRandom;
        private readonly List<(TerrariaTileManipulationState State, ToolWorldPublicationOffset1458 End)> boundaries = [];
        private ToolWorldPublication1458? publication;
        private WorldTile[] finalTargets = [];
        private int next;
        private bool sealedPlan, adopted, disposed, failed;

        internal ToolProjectileCutsPreparation1458(WorldTileAuthority owner,
            HeldToolOwnerPreparation1458 actor, ProjectileSnapshot projectile,
            ToolProjectileWorldCapture1458 context, PlayerAuthority.ToolTreeActorsPreparation actors,
            ToolWorldTileOverlay1458 overlay, ToolWorldMutationPlan plan,
            ToolWorldSourcePass1458 source, VanillaUnifiedRandom1458 actorRandom)
        {
            this.owner = owner; this.actor = actor; this.projectile = projectile;
            this.context = context; this.actors = actors; this.overlay = overlay;
            this.plan = plan; this.source = source; this.actorRandom = actorRandom;
        }

        internal bool IsCurrent => !disposed && !failed && !adopted && actor.IsCurrent &&
            context.IsCurrent && actors.IsCurrent && source.IsCurrentOwned && plan.IsCurrentOwned;

        internal bool TryRecordTerrainReads(ProjectileTypeId type, float x, float y, float vx, float vy) =>
            !sealedPlan && IsCurrent && type == projectile.Type &&
            TryRecordToolProjectileTerrain(overlay, type, x, y, vx, vy);

        internal bool TryStageCollisionCuts(in ProjectileStateUpdate body)
        {
            if (sealedPlan || !IsCurrent || body.Type != projectile.Type ||
                !TryGetToolProjectileCutBounds(body.Type, body.PositionX, body.PositionY,
                    body.VelocityX, body.VelocityY, true, overlay.Tiles.Dimensions.WidthTiles,
                    overlay.Tiles.Dimensions.HeightTiles, out var bounds)) return Reject();
            if (body.Damage > 0 && !Stage(in bounds, in body, 1)) return Reject();
            // HandleMovement draws even when damage<=0 or no cracked target is
            // eligible. This is one coupled sourceMain stream with WorldGen.
            int cracked = actorRandom.Next(3);
            if (cracked == 0 && body.Damage > 0 && !Stage(in bounds, in body, 2)) return Reject();
            plan.SourceRandom.CopyStateFrom(actorRandom);
            return IsCurrent;
        }

        internal bool TryStageDamageCuts(in ProjectileStateUpdate body)
        {
            if (sealedPlan || !IsCurrent || body.Type != projectile.Type ||
                !TryGetToolProjectileCutBounds(body.Type, body.PositionX, body.PositionY,
                    body.VelocityX, body.VelocityY, false, overlay.Tiles.Dimensions.WidthTiles,
                    overlay.Tiles.Dimensions.HeightTiles, out var bounds)) return Reject();
            // Damage.CutTiles does not require positive projectile damage.
            return Stage(in bounds, in body, 0);
        }

        private bool Stage(in ToolProjectileCutBounds1458 bounds, in ProjectileStateUpdate body, byte pass)
        {
            for (int x = bounds.MinX; x < bounds.MaxX; x++)
                for (int y = bounds.MinY; y < bounds.MaxY; y++)
                {
                    if (!TryQualifyToolProjectileCut(overlay, x, y, body.Type,
                            body.Ai.Ai0, pass, actor.DontHurtNature, out bool selected)) return false;
                    if (!selected) continue;
                    // Functional equipment remains in the phase's proven family;
                    // main/activeVoid mechanical writers were captured separately.
                    // No manual-tool NoBuilding gate applies to this source cut.
                    if (!actor.HasCurrentPhase || actor.Phase?.DerivedCombat is null ||
                        boundaries.Count == MaximumOuterCalls) return false;
                    plan.SourceRandom.CopyStateFrom(actorRandom);
                    if (!source.TryKillTile(x, y, false, false, false, out _)) return false;
                    actorRandom.CopyStateFrom(plan.SourceRandom);
                    var state = new TerrariaTileManipulationState(0,
                        checked((short)x), checked((short)y), 0, 0);
                    boundaries.Add((state, plan.CapturePublicationOffset()));
                }
            return IsCurrent;
        }

        internal bool TrySeal()
        {
            if (sealedPlan || !IsCurrent) return false;
            plan.SourceRandom.CopyStateFrom(actorRandom);
            // Allocation providers run before the actor's final pure ownership
            // guards. They never run between Dummy/projectile/world adoptions.
            if (!plan.TrySeal() || !IsCurrent) return Reject();
            finalTargets = new WorldTile[boundaries.Count];
            for (int index = 0; index < boundaries.Count; index++)
            {
                var state = boundaries[index].State;
                if (!overlay.TryRead(state.TileX, state.TileY, out finalTargets[index])) return Reject();
            }
            if (!IsCurrent) return Reject();
            sealedPlan = true;
            return true;
        }

        internal bool TrySetSourceActorView(RuntimeProjectileStore.SimulationPreparation phaseActor) =>
            !sealedPlan && IsCurrent && plan.TrySetSourceActorView(phaseActor) && IsCurrent;

        internal bool TryBindSourceActor(RuntimeProjectileStore.SimulationPreparation actorPreparation) =>
            sealedPlan && IsCurrent && plan.TryBindSourceActor(actorPreparation) && IsCurrent;

        internal bool TryAdoptUnpublished()
        {
            // The enclosing actor has just adopted its own Dummy2 writer. Do not
            // re-query the deliberately consumed player beforeimage here.
            if (!sealedPlan || disposed || failed || adopted ||
                !plan.TryAdoptWholeUnpublished(out publication)) return false;
            adopted = true;
            foreach (var boundary in boundaries)
            {
                var state = boundary.State;
                owner.projectileExplosionEchoes.Register(actor.Player, in state);
            }
            return true;
        }

        internal bool TryPublishNext()
        {
            if (!adopted || disposed || next == boundaries.Count) return false;
            var boundary = boundaries[next];
            // Consume all causal inner effects before this OUTER source17.
            // TryPublishThrough drains the boundary after throws before rethrow;
            // next invocation resumes17, without repeating an item/NPC/birth.
            var end = boundary.End;
            if (publication is null || !publication.TryPublishThrough(in end))
                throw new InvalidOperationException("Accepted tool cut lost its ordered publication boundary.");
            int targetIndex = next++;
            var state = boundary.State;
            // A child observer may have changed this target. Preserve unaffected
            // boundaries in the same section, but do not publish an oldkill into
            // a new tile or leave its one-use echo consumable for another state.
            if (!overlay.Tiles.Get(state.TileX, state.TileY).Equals(finalTargets[targetIndex]))
            {
                owner.projectileExplosionEchoes.TryConsume(actor.Player, in state);
                owner.replication?.TryPublishTileSquareToAll(overlay.Tiles, state.TileX, state.TileY);
                return true;
            }
            if (!owner.TryPublishToolTile(GameCommandSourceId.System, actor.Player, in state))
                owner.projectileExplosionEchoes.TryConsume(actor.Player, in state);
            return true;
        }

        private bool Reject() { failed = true; return false; }
        public void Dispose() { if (disposed) return; disposed = true; plan.Dispose(); }
    }

    private bool TryCaptureRetainedToolWorld(HeldToolOwnerPreparation1458 actor,
        out ToolProjectileWorldCapture1458? capture)
    {
        capture = null;
        var world = actor.World;
        if (!actor.IsCurrent || actor.Inactive || tiles is null || toolClock is null ||
            world.MaxTilesX != tiles.Dimensions.WidthTiles || world.MaxTilesY != tiles.Dimensions.HeightTiles ||
            world.GetGoodWorld is not { } good || world.DrunkWorld is not { } drunk ||
            world.NotTheBeesWorld is not { } bees || world.TenthAnniversaryWorld is not { } tenth ||
            world.DontStarveWorld is not { } constant ||
            !npcAuthority.TryCaptureToolSeason(tiles, out var season) || season.TenthAnniversaryWorld != tenth ||
            !float.IsFinite(toolClock.WindSpeedTarget)) return false;
        var facts = new ToolTreeWorldFacts1458(world.MaxTilesX, good, drunk, world.RemixWorld,
            bees, tenth, constant, toolClock.DayTime, toolClock.Raining, season.Halloween,
            MathF.Abs(toolClock.WindSpeedTarget) >= .4f)
        { HallowBackground = world.HallowBackground };
        capture = new(this, actor, toolClock, facts,
            hardModeBaseline || progression.IsCompleted(VanillaWorldProgressionId.Hardmode));
        return capture.IsCurrent;
    }

    internal sealed class ToolProjectileWorldCapture1458(WorldTileAuthority owner,
        HeldToolOwnerPreparation1458 actor, RuntimeWorldClock clock, ToolTreeWorldFacts1458 facts, bool hardMode)
    {
        internal ToolTreeWorldFacts1458 Facts => facts;
        internal bool HardMode => hardMode;
        internal bool IsCurrent => actor.IsCurrent && ReferenceEquals(owner.toolClock, clock) &&
            (owner.hardModeBaseline || owner.progression.IsCompleted(VanillaWorldProgressionId.Hardmode)) == hardMode &&
            clock.DayTime == facts.DayTime && clock.Raining == facts.Raining &&
            float.IsFinite(clock.WindSpeedTarget) &&
            (MathF.Abs(clock.WindSpeedTarget) >= .4f) == facts.TooWindyForButterflies &&
            owner.tiles is { } tiles && owner.npcAuthority.TryCaptureToolSeason(tiles, out var season) &&
            season.Halloween == facts.Halloween && season.TenthAnniversaryWorld == facts.TenthAnniversaryWorld;
    }

    // These are the source caller's bounds, not the terrain collision box.
    // In particular1052 cuts with its physical22x22 body despite colliding10x10.
    private readonly record struct ToolProjectileCutBounds1458(int MinX, int MaxX, int MinY, int MaxY);

    private static bool TryGetToolProjectileCutBounds(ProjectileTypeId type,
        float x, float y, float vx, float vy, bool collision, int worldWidth, int worldHeight,
        out ToolProjectileCutBounds1458 bounds)
    {
        bounds = default;
        if (!VanillaToolProjectileDefinition1458.TryGet(type, out var definition) ||
            !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(vx) || !float.IsFinite(vy) ||
            worldWidth <= 0 || worldHeight <= 0 || MathF.Abs(x) > 16000000f || MathF.Abs(y) > 16000000f ||
            MathF.Abs(vx) > 10000f || MathF.Abs(vy) > 10000f) return false;
        // Collision lower bounds cast BEFORE integer division. Damage bounds
        // divide in Single before truncation. Preserve this distinction at edges.
        int minX = collision ? (int)(x + vx - 1f) / 16 : (int)(x / 16f);
        int minY = collision ? (int)(y + vy - 1f) / 16 : (int)(y / 16f);
        int maxX = collision ? (int)((x + definition.Width + vx + 1f) / 16f) + 1 :
            (int)((x + definition.Width) / 16f) + 1;
        int maxY = collision ? (int)((y + definition.Height + vy + 1f) / 16f) + 1 :
            (int)((y + definition.Height) / 16f) + 1;
        minX = Math.Clamp(minX, 0, worldWidth); maxX = Math.Clamp(maxX, 0, worldWidth);
        minY = Math.Clamp(minY, 0, worldHeight); maxY = Math.Clamp(maxY, 0, worldHeight);
        if (maxX < minX || maxY < minY || (long)(maxX - minX) * (maxY - minY) > 64) return false;
        bounds = new(minX, maxX, minY, maxY);
        return true;
    }

    private static bool TryRecordToolProjectileTerrain(ToolWorldTileOverlay1458 overlay,
        ProjectileTypeId type, float x, float y, float vx, float vy)
    {
        if (!VanillaToolProjectileDefinition1458.TryGet(type, out var definition) ||
            !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(vx) || !float.IsFinite(vy) ||
            MathF.Abs(x) > 16000000f || MathF.Abs(y) > 16000000f ||
            MathF.Abs(vx) > 10000f || MathF.Abs(vy) > 10000f) return false;
        if (definition.IgnoreWater && !definition.TileCollide) return true;
        // Retain every possible liquid/tile/slope query BEFORE calling the
        // concrete motion resolver. The source1052 stride follows this segment;
        // two tiles of margin also cover centered10x10 and slope correction.
        bool collision = type.Value == VanillaThrownAxeProjectile1458.ProjectileType;
        float endX = collision ? x + vx : x, endY = collision ? y + vy : y;
        int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(x, endX) / 16f) - 2);
        int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(y, endY) / 16f) - 2);
        int maxX = Math.Min(overlay.Tiles.Dimensions.WidthTiles,
            (int)MathF.Floor((MathF.Max(x, endX) + definition.Width) / 16f) + 3);
        int maxY = Math.Min(overlay.Tiles.Dimensions.HeightTiles,
            (int)MathF.Floor((MathF.Max(y, endY) + definition.Height) / 16f) + 3);
        if (maxX < minX || maxY < minY || (long)(maxX - minX) * (maxY - minY) > 512) return false;
        for (int tx = minX; tx < maxX; tx++)
            for (int ty = minY; ty < maxY; ty++)
                if (!overlay.RecordRead(tx, ty)) return false;
        return overlay.IsCurrentOwned;
    }

    private static bool TryQualifyToolProjectileCut(ToolWorldTileOverlay1458 overlay,
        int x, int y, ProjectileTypeId projectile, float ai0, byte pass, bool dontHurtNature, out bool selected)
    {
        selected = false;
        if (pass > 2 || !overlay.TryRead(x, y, out var tile) ||
            !VanillaToolTileCatalog1458.TryGet(tile.TileType, out var facts)) return false;
        // Damage.CutTilesAt tests active. Collision source loops do not: source
        // WorldGen may still frame a retained inactive484/cracked type.
        if (pass == 0 && !tile.IsActive) return true;
        // Numeric TileID.Sets.TileCutIgnore.IgnoreDontHurtNature (1458):
        // default true; only these source exceptions remain cuttable.
        if (dontHurtNature && tile.Type is not
            (654 or 444 or 485 or 231 or 484 or 32 or 352 or 655 or 69 or 51 or 481 or 482 or 483)) return true;
        bool candidate = pass switch
        {
            1 => tile.Type == 484,
            2 => tile.Type is 481 or 482 or 483,
            _ => VanillaProjectileTileCutFacts.IsCuttable(tile.TileType) ||
                projectile.Value == VanillaThrownAxeProjectile1458.ProjectileType && ai0 == 0f &&
                (facts.IsTreeTrunk || tile.Type == 323)
        };
        if (!candidate || VanillaProjectileTileCutFacts.IsBlockedByWall(tile.WallType) ||
            VanillaProjectileTileCutFacts.RequiresMinimumFrame(tile.TileType) &&
                tile.FrameX < VanillaProjectileTileCutFacts.FrameGatedMinimumFrameX) return true;
        if (y + 1 >= overlay.Tiles.Dimensions.HeightTiles || !overlay.TryRead(x, y + 1, out var below))
            return false;
        selected = !VanillaProjectileTileCutFacts.IsBlockedBySupport(below.TileType);
        return true;
    }
}
