using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal readonly record struct OrdinaryMeleeWorldFacts1458(
    bool Owned, bool Day, bool Eclipse, bool Expert, bool Good, bool Remix);

internal sealed class VanillaOrdinaryMeleeEnvironment1458(WorldTileStore tiles, RuntimeNpcStore npcs,
    RuntimeNpcBuffStatus1458 status, VanillaNpcWorldMotionAiStepper motion,
    Func<OrdinaryMeleeWorldFacts1458> factsOwner, RuntimeNpcReplicationRegistry? replication)
    : IVanillaOrdinaryMeleeEnvironment1458
{
    private readonly NpcSnapshot[] peerScratch = new NpcSnapshot[npcs.Capacity];

    public bool TryCapture(in NpcSnapshot source, VanillaNpcBehaviorContext context,
        out IVanillaOrdinaryMeleeWorld1458 world)
    {
        world = default!;
        var facts = factsOwner();
        if (!facts.Owned || facts.Day != context.DayTime || facts.Eclipse != context.EclipseActive ||
            facts.Expert != context.ExpertMode || facts.Good != context.GoodWorld || facts.Remix != context.RemixWorld ||
            !VanillaOrdinaryMeleePresentation1458.Supports(in source) || source.Simulation.Confused ||
            source.Simulation.LiquidContact is NpcLiquidContactKind.Lava or NpcLiquidContactKind.Shimmer ||
            !VanillaNpcDefinitionCatalog.TryGet(source.TypeIdentity, source.NetIdentity, out var definition) ||
            !definition.TryResolveHitbox(source.Simulation, out var body) ||
            !status.TryPlan(in source, out var phase) || phase.LifeAfter != source.Simulation.Life ||
            phase.Flags.Poisoned || phase.Flags.OnFire || phase.LoveStruck)
            return false;
        var terrain = new VanillaGroundCritterTerrain1458(tiles);
        terrain.FenceBody(source.PositionX, source.PositionY, source.VelocityX, source.VelocityY,
            body.Width, body.Height);
        var capture = new Capture(tiles, npcs, status, motion, factsOwner, replication, source,
            phase, facts, terrain, peerScratch);
        if (!capture.SupportsMotion(in source, body.Width, body.Height)) return false;
        world = capture;
        return capture.IsCurrent;
    }

    private sealed class Capture(WorldTileStore tiles, RuntimeNpcStore npcs, RuntimeNpcBuffStatus1458 status,
        VanillaNpcWorldMotionAiStepper motion, Func<OrdinaryMeleeWorldFacts1458> factsOwner,
        RuntimeNpcReplicationRegistry? replication, NpcSnapshot source, RuntimeNpcBuffPlan1458 phase,
        OrdinaryMeleeWorldFacts1458 facts, VanillaGroundCritterTerrain1458 terrain, NpcSnapshot[] peerScratch)
        : IVanillaOrdinaryMeleeWorld1458
    {
        private readonly RuntimeNpcBuffPlan1458 phase = phase;
        private RuntimeNpcStinkyVisualOffer1458 visual;
        private RuntimeNpcBuffPlan1458 finalPhase;
        private byte[]? wetFrame;

        public bool IsCurrent
        {
            get
            {
                return terrain.IsCurrent && factsOwner() == facts &&
                    status.IsCurrentOrdinaryMeleePhase1458(in phase) && terrain.IsCurrent;
            }
        }

        public bool TryStageStatus(IVanillaNpcRandom random, out NpcSnapshot staged)
        {
            staged = source with { Simulation = source.Simulation with { LifeRegenCounter = phase.CounterAfter } };
            if (!IsCurrent) return false;
            visual = RuntimeNpcBuffStatus1458.PlanVisualOffers(in phase,
                new NpcRuntimeTownCombatRandom1458(random), facts.Good);
            return IsCurrent;
        }

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
                !VanillaOrdinaryMeleePresentation1458.TryFinish(in physics, out final)) return false;
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

        public bool TryAdoptStatus(in NpcSnapshot accepted) => status.Commit(in finalPhase, in accepted) &&
            status.ObserveVisualOffer(accepted.Handle, in visual);

        public void PublishStatus()
        {
            status.PublishExpired(in phase);
            if (wetFrame is not null) replication?.PublishGemBunnyBuffs(wetFrame);
        }
    }
}
