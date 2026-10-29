using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal readonly record struct GroundCombatWorldFacts1458(
    bool Owned, bool Day, bool Eclipse, bool Expert, bool Good, bool Remix, int? Invasion);

internal sealed class VanillaGroundCombatEnvironment1458(WorldTileStore tiles, RuntimeNpcStore npcs, RuntimeProjectileStore projectiles,
    RuntimeNpcBuffStatus1458 status, VanillaNpcWorldMotionAiStepper motion,
    Func<GroundCombatWorldFacts1458> factsOwner, RuntimeNpcReplicationRegistry? replication)
    : IVanillaGroundCombatEnvironment1458
{
    private readonly NpcSnapshot[] peerScratch = new NpcSnapshot[npcs.Capacity];

    public bool TryCapture(in NpcSnapshot source, VanillaNpcBehaviorContext context,
        out IVanillaGroundCombatWorld1458 world)
    {
        world = default!;
        var facts = factsOwner();
        if (!facts.Owned || facts.Day != context.DayTime || facts.Eclipse != context.EclipseActive ||
            facts.Expert != context.ExpertMode || facts.Good != context.GoodWorld || facts.Remix != context.RemixWorld ||
            facts.Invasion != context.InvasionType ||
            !VanillaGroundCombatPresentation1458.Supports(in source) || source.Simulation.Confused ||
            source.Simulation.LiquidContact is NpcLiquidContactKind.Lava or NpcLiquidContactKind.Shimmer ||
            !VanillaNpcDefinitionCatalog.TryGet(source.TypeIdentity, source.NetIdentity, out var definition) ||
            !definition.TryResolveHitbox(source.Simulation, out var body) ||
            !status.TryPlan(in source, out var phase) || phase.LifeAfter != source.Simulation.Life ||
            phase.Flags.Poisoned || phase.Flags.OnFire || phase.LoveStruck)
            return false;
        var terrain = new VanillaGroundCritterTerrain1458(tiles);
        terrain.FenceBody(source.PositionX, source.PositionY, source.VelocityX, source.VelocityY,
            body.Width, body.Height);
        var capture = new Capture(tiles, npcs, projectiles, status, motion, factsOwner, replication, source,
            phase, facts, terrain, peerScratch);
        if (!capture.SupportsMotion(in source, body.Width, body.Height)) return false;
        world = capture;
        return capture.IsCurrent;
    }

    private sealed class Capture(WorldTileStore tiles, RuntimeNpcStore npcs, RuntimeProjectileStore projectiles, RuntimeNpcBuffStatus1458 status,
        VanillaNpcWorldMotionAiStepper motion, Func<GroundCombatWorldFacts1458> factsOwner,
        RuntimeNpcReplicationRegistry? replication, NpcSnapshot source, RuntimeNpcBuffPlan1458 phase,
        GroundCombatWorldFacts1458 facts, VanillaGroundCritterTerrain1458 terrain, NpcSnapshot[] peerScratch)
        : IVanillaGroundCombatWorld1458
    {
        private readonly RuntimeNpcBuffPlan1458 phase = phase;
        private RuntimeNpcStinkyVisualOffer1458 visual;
        private RuntimeNpcBuffPlan1458 finalPhase;
        private byte[]? wetFrame;
        private RuntimeProjectileStore.SpawnBatchPreparation? projectileBatch;
        private bool adopted;

        public bool IsCurrent
        {
            get
            {
                return !adopted && (projectileBatch is null || projectileBatch.IsCurrentOwned) &&
                    terrain.IsCurrent && factsOwner() == facts &&
                    status.IsCurrentGroundCombatPhase1458(in phase) && terrain.IsCurrent;
            }
        }

        public bool TryStageStatus(IVanillaNpcRandom random, out NpcSnapshot staged)
        {
            staged = source with { Simulation = source.Simulation with { LifeRegenCounter = phase.CounterAfter } };
            if (!IsCurrent) return false;
            visual = RuntimeNpcBuffStatus1458.PlanVisualOffers(in phase,
                new NpcRuntimeTownCombatRandom1458(random), facts.Good);
            if (source.Type == 590 && VanillaNpcDefinitionCatalog.TryGet(source.TypeIdentity, source.NetIdentity,
                    out var definition) && definition.TryResolveHitbox(source.Simulation, out var body))
            {
                // NPC.AI Torch Zombie's dry-flame offer precedes AI_003; Dust itself is
                // a dedicated-server no-op. Framing.GetTileSafely permits an adjacent edge cell.
                int y = (int)(source.PositionY + 6f) / 16;
                bool left = source.Simulation.SpriteDirection < 0;
                int x = (int)(source.PositionX + body.Width * .5f + (left ? -22f : 14f)) / 16;
                if ((uint)x < tiles.Dimensions.WidthTiles && (uint)y < tiles.Dimensions.HeightTiles)
                {
                    int adjacent = x + (left ? 1 : -1);
                    terrain.FenceBody(x * 16f, y * 16f, 0f, 0f, 16, 16);
                    byte amount = (uint)adjacent < tiles.Dimensions.WidthTiles ? tiles.Get(adjacent, y).LiquidAmount : (byte)0;
                    if (tiles.Get(x, y).LiquidAmount == 0 && amount == 0) random.NextInt32(0, 30);
                }
            }
            return IsCurrent;
        }

        public bool TryStageProjectiles(ReadOnlySpan<NpcAiProjectileIntent> shots)
        {
            if (!IsCurrent || projectileBatch is not null || shots.Length > 1) return false;
            if (shots.IsEmpty) return true;
            var shot = shots[0];
            if (shot.Type.Value is not (436 or 438 or 471 or 508 or 592) ||
                shot.Damage is < 0 or > short.MaxValue ||
                !TerraRuntime.Gameplay.Projectiles.VanillaDefinitionCatalog.TryGet(shot.Type, out var shape)) return false;
            terrain.FenceBody(shot.PositionX, shot.PositionY, 0f, 0f, shape.Width, shape.Height);
            bool wet = VanillaWorldCollision.TryGetWetContact(tiles, shot.PositionX, shot.PositionY,
                shape.Width, shape.Height, out var kind);
            // NewProjectile copies WetCollision's first contact, including honey
            // even for ignoreWater. LavaWet and wetCount still start at zero.
            // Projectile.Shimmer immediately returns for these hostile AI1/AI2
            // types; its observed contact flag still belongs to the birth.
            if (!IsCurrent) return false;
            var liquid = new ProjectileLiquidState(wet && !shape.IgnoreWater, false,
                wet && kind == WorldLiquidKind.Honey, wet && kind == WorldLiquidKind.Shimmer);
            // NewProjectile receives the source center; the planner has already applied
            // SetDefaults' half-hitbox conversion. Runtime stores always receive top-left.
            var state = new ProjectileStateUpdate(shot.Type, byte.MaxValue, shot.PositionX, shot.PositionY,
                shot.VelocityX, shot.VelocityY, shot.InitialAi, 0, (short)shot.Damage, shot.KnockBack, 0);
            Span<RuntimeProjectileStore.SpawnRequest> requests = stackalloc RuntimeProjectileStore.SpawnRequest[1];
            requests[0] = new(state, shot.TimeLeftOverride > 0 ? shot.TimeLeftOverride : null) { InitialLiquid = liquid };
            return projectiles.TryPrepareVanillaSpawnBatch(requests, out projectileBatch) && IsCurrent;
        }

        public bool CanHit(float x, float y, int width, int height,
            float targetX, float targetY, int targetWidth, int targetHeight) =>
            terrain.FenceLineOfSight(x, y, width, height, targetX, targetY, targetWidth, targetHeight) &&
            VanillaWorldCanHit.HasLineOfSight(tiles, x, y, width, height, targetX, targetY, targetWidth, targetHeight);

        public bool CanHitLine(float x, float y, int width, int height,
            float targetX, float targetY, int targetWidth, int targetHeight) =>
            terrain.FenceLineOfSight(x, y, width, height, targetX, targetY, targetWidth, targetHeight) &&
            VanillaWorldLineOfSight.CanHitLine(tiles, x + width / 2, y + height / 2,
                targetX + targetWidth / 2, targetY + targetHeight / 2);

        public bool TryFinishMotion(in NpcSnapshot before, in NpcStateUpdate ai,
            in VanillaNpcRawPlayer1458 rawTarget, out NpcStateUpdate final)
        {
            final = default;
            if (!IsCurrent || !VanillaNpcDefinitionCatalog.TryGet(before.TypeIdentity, before.NetIdentity, out var definition) ||
                !definition.TryResolveHitbox(ai.Simulation, out var body)) return false;
            terrain.FenceBody(ai.PositionX, ai.PositionY, ai.VelocityX, ai.VelocityY, body.Width, body.Height);
            if (facts.Expert && rawTarget.PositionY + rawTarget.Height < ai.PositionY &&
                MathF.Abs(ai.PositionX + body.Width * .5f - (rawTarget.PositionX + rawTarget.Width * .5f)) < rawTarget.Width * 3f)
            {
                // The elevated-target CanHit reads the corridor to the stored player body,
                // including inactive/dead array slots. Bind those sections before collision.
                float left = Math.Clamp(MathF.Min(ai.PositionX, rawTarget.PositionX), 0f, tiles.Dimensions.WidthTiles * 16f);
                float top = Math.Clamp(MathF.Min(ai.PositionY, rawTarget.PositionY), 0f, tiles.Dimensions.HeightTiles * 16f);
                float right = Math.Clamp(MathF.Max(ai.PositionX + body.Width, rawTarget.PositionX + rawTarget.Width), left, tiles.Dimensions.WidthTiles * 16f);
                float bottom = Math.Clamp(MathF.Max(ai.PositionY + body.Height, rawTarget.PositionY + rawTarget.Height), top, tiles.Dimensions.HeightTiles * 16f);
                terrain.FenceBody(left, top, 0f, 0f, (int)MathF.Ceiling(right - left), (int)MathF.Ceiling(bottom - top));
            }
            var proposed = before with { PositionX = ai.PositionX, PositionY = ai.PositionY,
                VelocityX = ai.VelocityX, VelocityY = ai.VelocityY, Simulation = ai.Simulation };
            if (!SupportsMotion(in proposed, body.Width, body.Height) ||
                !motion.TryApplyWorldMotion(in before, in ai, out var physics, rawTarget) ||
                !VanillaGroundCombatPresentation1458.TryFinish(in physics, out final)) return false;
            var endpoint = before with { PositionX = final.PositionX, PositionY = final.PositionY,
                VelocityX = final.VelocityX, VelocityY = final.VelocityY, Simulation = final.Simulation };
            if (!SupportsMotion(in endpoint, body.Width, body.Height)) return false;
            var removed = RuntimeNpcBuffStatus1458.PlanGemBunnyWetRemoval(phase.Slots, phase.Stinky, final.Simulation.Wet);
            bool changed = false;
            for (int i = 0; i < RuntimeNpcBuffStatus1458.Capacity; i++) changed |= removed[i] != phase.Slots[i];
            finalPhase = phase with { Slots = removed };
            if (changed && !RuntimeNpcBuffStatus1458.TryPrepareGemBunnyBuffFrame(source.Handle.Slot, in removed, out wetFrame))
                return false;
            return IsCurrent;
        }

        internal bool SupportsMotion(in NpcSnapshot state, int width, int height)
        {
            terrain.FenceBody(state.PositionX, state.PositionY, state.VelocityX, state.VelocityY, width, height);
            if (!terrain.IsCurrent || terrain.HasConveyorAtBody(state.PositionX, state.PositionY, width, height) ||
                VanillaWorldCollision.TryGetWetContact(tiles, state.PositionX, state.PositionY, width, height,
                    out var liquid) && liquid is WorldLiquidKind.Lava or WorldLiquidKind.Shimmer) return false;
            int left = Math.Clamp((int)((state.PositionX - MathF.Abs(state.VelocityX)) / 16f) - 2, 0, tiles.Dimensions.WidthTiles - 1);
            int right = Math.Clamp((int)((state.PositionX + width + MathF.Abs(state.VelocityX)) / 16f) + 2, 0, tiles.Dimensions.WidthTiles - 1);
            int top = Math.Clamp((int)((state.PositionY - MathF.Abs(state.VelocityY)) / 16f) - 4, 0, tiles.Dimensions.HeightTiles - 1);
            int bottom = Math.Clamp((int)((state.PositionY + height + MathF.Abs(state.VelocityY)) / 16f) + 2, 0, tiles.Dimensions.HeightTiles - 1);
            for (int x = left; x <= right; x++)
                for (int y = top; y <= bottom; y++)
                {
                    WorldTile tile = tiles.Get(x, y);
                    // Door opening/destruction and wiring require their own publication transactions.
                    if (tile.IsActive && (tile.TileType.Value is 135 or 210 ||
                        !tile.IsActuated && tile.TileType.Value is 10 or 388))
                        return false;
                }
            // AiSpawnPreview also guards the store-wide mutation serial. This reusable scratch
            // supplies contact admission without retaining a second NPC table for every plan.
            int peerCount = npcs.CopyActive(peerScratch);
            foreach (var peer in peerScratch.AsSpan(0, peerCount))
                if (peer.Handle != source.Handle && peer.Simulation.Friendly == true &&
                    VanillaNpcDefinitionCatalog.TryGet(peer.TypeIdentity, peer.NetIdentity, out var definition) &&
                    definition.TryResolveHitbox(peer.Simulation, out var other) &&
                    state.PositionX - MathF.Abs(state.VelocityX) < peer.PositionX + other.Width &&
                    state.PositionX + width + MathF.Abs(state.VelocityX) > peer.PositionX &&
                    state.PositionY - MathF.Abs(state.VelocityY) < peer.PositionY + other.Height &&
                    state.PositionY + height + MathF.Abs(state.VelocityY) > peer.PositionY)
                    return false;
            return terrain.IsCurrent;
        }

        public bool TryAdoptStatus(in NpcSnapshot accepted)
        {
            if (!IsCurrent || !status.Commit(in finalPhase, in accepted)) return false;
            if (projectileBatch is not null)
            {
                if (!projectileBatch.TryCommitUnpublished()) return false;
                foreach (var birth in projectileBatch.FinalBirths)
                    if (!projectiles.TrySetServerNpcSource(birth.Handle, accepted.Handle)) return false;
            }
            adopted = true;
            return status.ObserveVisualOffer(accepted.Handle, in visual);
        }

        public void PublishStatus()
        {
            try { projectileBatch?.TryPublishBirthJournal(); }
            finally
            {
                status.PublishExpired(in phase);
                // Birth/expiration observers may replace the physical NPC or
                // apply a newer buff list. Never send old slot-only54 bytes to it.
                var committedStatus = finalPhase with { StatusRevision = phase.StatusRevision + 1 };
                if (wetFrame is not null && adopted && status.IsCurrentGroundCombatPhase1458(in committedStatus))
                    replication?.PublishGemBunnyBuffs(wetFrame);
            }
        }

        public void Dispose() => projectileBatch?.Dispose();
    }
}
