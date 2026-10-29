using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal readonly record struct NpcPhaseWorldFacts1458(
    bool Owned, bool Day, bool Eclipse, bool Expert, bool Good, bool Remix, int? Invasion)
{
    internal bool? BloodMoon { get; init; }
    internal bool? Raining { get; init; }
    internal bool? Crimson { get; init; }
}

internal sealed partial class VanillaNpcPhaseEnvironment1458(WorldTileStore tiles, RuntimeNpcStore npcs, RuntimeProjectileStore projectiles,
    RuntimeNpcBuffStatus1458 status, VanillaNpcWorldMotionAiStepper motion,
    Func<NpcPhaseWorldFacts1458> factsOwner, RuntimeNpcReplicationRegistry? replication,
    Func<NpcSnapshot, RuntimeTownNpcScheduleConditions1458>? conditions = null,
    Func<RuntimeTownPlayerDanger1458[]>? playerDanger = null)
    : IVanillaNpcPhaseEnvironment1458
{
    private readonly NpcSnapshot[] peerScratch = new NpcSnapshot[npcs.Capacity];

    public bool TryCapture(in NpcSnapshot source, VanillaNpcBehaviorContext context,
        out IVanillaNpcPhaseWorld1458 world)
    {
        world = default!;
        var facts = factsOwner();
        if (!facts.Owned || facts.Day != context.DayTime || facts.Eclipse != context.EclipseActive ||
            facts.Expert != context.ExpertMode || facts.Good != context.GoodWorld || facts.Remix != context.RemixWorld ||
            facts.Invasion != context.InvasionType ||
            !SupportsPresentation(in source) || source.Simulation.Confused ||
            source.Simulation.LiquidContact is NpcLiquidContactKind.Lava or NpcLiquidContactKind.Shimmer ||
            source.Simulation.WetCount is not (>= 0 and <= 10) || source.Simulation.LavaWet is null ||
            source.Simulation.HoneyWet is null || source.Simulation.ShimmerWet is not false ||
            !SupportsTeleportHistory(in source) ||
            !VanillaNpcDefinitionCatalog.TryGet(source.TypeIdentity, source.NetIdentity, out var definition) ||
            !definition.TryResolveHitbox(source.Simulation, out var body) ||
            !status.TryPlan(in source, out var phase) || phase.LifeAfter != source.Simulation.Life ||
            phase.Flags.Poisoned || phase.Flags.OnFire || phase.LoveStruck)
            return false;
        var terrain = new VanillaGroundCritterTerrain1458(tiles);
        terrain.FenceBody(source.PositionX, source.PositionY, source.VelocityX, source.VelocityY,
            body.Width, body.Height);
        bool fishWeather = source.Type is 55 or 230 or 592 or 593;
        if (fishWeather && (facts.BloodMoon is null || facts.Raining is null ||
            facts.BloodMoon == true && facts.Crimson is null)) return false;
        CritterInputs? critter = null;
        if (VanillaNpcTransformation1458.TryGetGroundCritterFamily(source.TypeIdentity, out _))
        {
            if (conditions is null || playerDanger is null || facts.Raining is null) return false;
            var weather = conditions(source);
            if (weather.DayTime != context.DayTime || weather.Eclipse != context.EclipseActive ||
                weather.SlimeRain != context.SlimeRainActive || weather.Raining != facts.Raining) return false;
            int count = npcs.CopyActive(peerScratch);
            var peers = peerScratch.AsSpan(0, count).ToArray();
            var players = playerDanger();
            terrain.FenceBody(source.PositionX - 224f, source.PositionY - 224f, 0f, 0f, 480, 480);
            if (!RuntimeTownNpcDangerScanner1458.TryScan(tiles, in source, peers, players,
                    status, false, false, out var danger)) return false;
            critter = new(peers, players, weather, danger, conditions, playerDanger);
        }
        var capture = new Capture(tiles, npcs, projectiles, status, motion, factsOwner, replication, source,
            phase, facts, terrain, peerScratch, context, critter);
        if (!capture.SupportsMotion(in source, body.Width, body.Height)) return false;
        world = capture;
        return capture.IsCurrent;
    }

    private sealed record CritterInputs(NpcSnapshot[] Peers, RuntimeTownPlayerDanger1458[] Players,
        RuntimeTownNpcScheduleConditions1458 Weather, RuntimeTownNpcDanger1458 Danger,
        Func<NpcSnapshot, RuntimeTownNpcScheduleConditions1458> Conditions,
        Func<RuntimeTownPlayerDanger1458[]> PlayerDanger);

    private static bool SupportsPresentation(in NpcSnapshot source) =>
        VanillaGroundCombatPresentation1458.Supports(in source) ||
        SupportsChildPresentation(in source) ||
        VanillaNpcTransformation1458.TryGetGroundCritterFamily(source.TypeIdentity, out _) &&
        VanillaGroundCritterPresentation1458.TryFinish(new(source.Type, source.NetId, source.PositionX, source.PositionY,
            source.VelocityX, source.VelocityY, source.Target, source.Ai, source.Simulation), out _);

    private static bool SupportsTeleportHistory(in NpcSnapshot source) =>
        source.Simulation.TeleportTime is null or 0f || source.Simulation.TeleportTime == 1f &&
        source.Simulation.TeleportStyle == 13;

    private sealed partial class Capture(WorldTileStore tiles, RuntimeNpcStore npcs, RuntimeProjectileStore projectiles, RuntimeNpcBuffStatus1458 status,
        VanillaNpcWorldMotionAiStepper motion, Func<NpcPhaseWorldFacts1458> factsOwner,
        RuntimeNpcReplicationRegistry? replication, NpcSnapshot source, RuntimeNpcBuffPlan1458 phase,
        NpcPhaseWorldFacts1458 facts, VanillaGroundCritterTerrain1458 terrain, NpcSnapshot[] peerScratch,
        VanillaNpcBehaviorContext context, CritterInputs? critter)
        : IVanillaNpcPhaseWorld1458
    {
        private readonly RuntimeNpcBuffPlan1458 initialPhase = phase;
        private RuntimeNpcBuffPlan1458 phase = phase;
        private RuntimeNpcStinkyVisualOffer1458 visual;
        private RuntimeNpcBuffPlan1458 finalPhase;
        private byte[]? wetFrame;
        private RuntimeProjectileStore.SpawnBatchPreparation? projectileBatch;
        private bool adopted;
        private bool networkSyncRequested = npcs.HasPendingBirth(source.Handle);
        private readonly int initialNetworkSpam = replication?.CaptureNpcPhaseSpam(source.Handle) ?? 0;
        private int networkSpamAdjustment;
        private bool ignoreSpamLimitStaged;
        private bool aiRetirementNetworkStaged;

        public bool IsCurrent
        {
            get
            {
                return !adopted && terrain.IsCurrent && CritterInputsCurrent() && factsOwner() == facts && ChildInputsCurrent() &&
                    (projectileBatch is null || projectileBatch.IsCurrentOwned) &&
                    status.IsCurrentNpcPhase1458(in initialPhase) &&
                    (replication is null || replication.IsNpcPhaseSpamCurrent(source.Handle, initialNetworkSpam)) && terrain.IsCurrent;
            }
        }

        public bool TryStageIgnoreSpamLimitUpdate(bool aiAlreadyRequested)
        {
            if (!IsCurrent || ignoreSpamLimitStaged || initialNetworkSpam < 0) return false;
            ignoreSpamLimitStaged = true;
            // NPC.NetUpdateIgnoreSpamLimit: a fresh or excessive request receives
            // one thirty-tick debit, even when that temporarily makes spam negative.
            if (!(networkSyncRequested || aiAlreadyRequested) || initialNetworkSpam > 90)
                networkSpamAdjustment = -30;
            networkSyncRequested = true;
            return true;
        }

        public bool TryPrepareAiRetirementNetwork()
        {
            if (aiRetirementNetworkStaged || replication is null || !IsCurrent) return false;
            aiRetirementNetworkStaged = true;
            return true;
        }

        public bool TryStageStatus(RuntimeNpcStore.AiSpawnPreview preview, IVanillaNpcRandom random, out NpcSnapshot staged)
        {
            staged = source with { Simulation = source.Simulation with { LifeRegenCounter = phase.CounterAfter } };
            if (!IsCurrent) return false;
            visual = RuntimeNpcBuffStatus1458.PlanVisualOffers(in phase,
                new NpcRuntimeTownCombatRandom1458(random), facts.Good);
            if (staged.Simulation.TeleportTime == 1f)
                staged = staged with { Simulation = staged.Simulation with { TeleportTime = 0f } };
            if (facts.BloodMoon == true && source.Type is 55 or 230 or 592 or 593)
            {
                var target = facts.Crimson == true ? VanillaNpcIds.CrimsonGoldfish : VanillaNpcIds.CorruptGoldfish;
                if (!TryStageTransform(preview, in staged, target, default, out var transformed)) return false;
                staged = transformed;
            }
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
            if (!TerraRuntime.Gameplay.Projectiles.VanillaGroundCombatPlayerHit1458.IsSupported(shot.Type) ||
                shot.Damage is < 0 or > short.MaxValue ||
                !TerraRuntime.Gameplay.Projectiles.VanillaDefinitionCatalog.TryGet(shot.Type, out var shape) ||
                !VanillaNpcDeathPreludeCatalog1458.TryGet(source.TypeIdentity, source.NetIdentity, out var banner) ||
                banner.BannerId is < 0 or > ushort.MaxValue) return false;
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
            // Projectile.FindBannerToAssociateTo uses this NPC parent's BannerID mapping;
            // packet27 carries the association independently of its damage and NPC-proj flag.
            var state = new ProjectileStateUpdate(shot.Type, byte.MaxValue, shot.PositionX, shot.PositionY,
                shot.VelocityX, shot.VelocityY, shot.InitialAi, (ushort)banner.BannerId,
                (short)shot.Damage, shot.KnockBack, 0);
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

        public bool TryFinishMotion(RuntimeNpcStore.AiSpawnPreview preview, in NpcSnapshot before, in NpcStateUpdate ai,
            in VanillaNpcRawPlayer1458 rawTarget, out NpcStateUpdate final)
        {
            final = default;
            if (!IsCurrent || !VanillaNpcDefinitionCatalog.TryGet(new(ai.Type), new(ai.NetId), out var definition) ||
                !definition.TryResolveHitbox(ai.Simulation, out var body)) return false;
            terrain.FenceBody(ai.PositionX, ai.PositionY, ai.VelocityX, ai.VelocityY, body.Width, body.Height);
            if ((facts.Expert || VanillaNpcTransformation1458.IsZombieMerman(definition.Type)) &&
                rawTarget.PositionY + rawTarget.Height < ai.PositionY &&
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
            networkSyncRequested |= ai.RequiresImmediateSync;
            var networkAi = ai with { RequiresImmediateSync = networkSyncRequested };
            var proposed = before with { Type = ai.Type, NetId = ai.NetId, Ai = ai.Ai,
                PositionX = ai.PositionX, PositionY = ai.PositionY,
                VelocityX = ai.VelocityX, VelocityY = ai.VelocityY, Simulation = ai.Simulation };
            if (VanillaNpcTransformation1458.TryGetGroundCritterFamily(before.TypeIdentity, out _) &&
                ai.Type == before.Type)
            {
                if (!RuntimeNpcReplicationRegistry.TryPrepareGemBunnyHome(in before, in ai, out var homeFrame)) return false;
                if (homeFrame is not null) RecordSourceFrame(homeFrame, home: true);
            }
            var home = new VanillaNpcTownCollisionContext1458(ai.Simulation.HomeTileY ?? -1,
                facts.Day, facts.Eclipse, facts.Invasion is > 0);
            bool mountedScutlix = false;
            if (ai.Type == VanillaNpcIds.Scutlix.Value)
            {
                if (!TryHasScutlixRider(before.Handle.Slot, out mountedScutlix)) return false;
                if (mountedScutlix)
                    terrain.FenceBody(ai.PositionX, ai.PositionY + body.Height - 62f,
                        ai.VelocityX, ai.VelocityY, body.Width, 62);
            }
            if (!SupportsMotion(in proposed, body.Width, body.Height) ||
                !motion.TryApplyNpcPhaseWorldMotion(in before, in networkAi, in rawTarget, ContinueCollision,
                    in home, out var physics, mountedScutlix)) return false;
            if (ChildFrameCount(new(physics.Type)) > 0)
            {
                if (!TryFinishChildPresentation(in physics, out final)) return false;
            }
            else if (VanillaNpcTransformation1458.TryGetGroundCritterFamily(new(physics.Type), out _))
            {
                if (!VanillaGroundCritterPresentation1458.TryFinish(in physics, out final)) return false;
            }
            else if (!VanillaGroundCombatPresentation1458.TryFinish(in physics, out final,
                rawTarget, RockFrameVisibility(in physics, in rawTarget))) return false;
            if (!VanillaNpcDefinitionCatalog.TryGet(new(final.Type), new(final.NetId), out var finalDefinition) ||
                !finalDefinition.TryResolveHitbox(final.Simulation, out var finalBody)) return false;
            var endpoint = before with { Type = final.Type, NetId = final.NetId, Ai = final.Ai,
                PositionX = final.PositionX, PositionY = final.PositionY,
                VelocityX = final.VelocityX, VelocityY = final.VelocityY, Simulation = final.Simulation };
            if (!SupportsMotion(in endpoint, finalBody.Width, finalBody.Height)) return false;
            finalPhase = phase;
            return IsCurrent;

            bool ContinueCollision(in NpcStateUpdate collision, out NpcStateUpdate continued) =>
                TryContinueCollision(preview, in collision, out continued);
        }

        internal bool SupportsMotion(in NpcSnapshot state, int width, int height)
        {
            terrain.FenceBody(state.PositionX, state.PositionY, state.VelocityX, state.VelocityY, width, height);
            if (!terrain.IsCurrent || terrain.HasConveyorAtBody(state.PositionX, state.PositionY, width, height) ||
                VanillaWorldCollision.LavaCollision(tiles, state.PositionX, state.PositionY, width, height) ||
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
            bool unownedCritterContact = VanillaNpcTransformation1458.TryGetGroundCritterFamily(source.TypeIdentity, out _) ||
                VanillaNpcTransformation1458.IsRetainedFish(source.TypeIdentity) || source.TypeIdentity == VanillaNpcIds.Maggot;
            int peerCount = npcs.CopyActive(peerScratch);
            foreach (var peer in peerScratch.AsSpan(0, peerCount))
                if (peer.Handle != source.Handle && (peer.Simulation.Friendly == true || unownedCritterContact) &&
                    !IsHarmlessMaggotPair(in state, in peer) &&
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
            // Core has already adopted the NPC and RNG without invoking a callback.
            // Do not sample public context delegates after that point.
            if (!terrain.IsCurrent || !status.Commit(in finalPhase, in accepted)) return false;
            if (projectileBatch is not null)
            {
                if (!projectileBatch.TryCommitUnpublished()) return false;
                foreach (var birth in projectileBatch.FinalBirths)
                    if (!projectiles.TrySetServerNpcSource(birth.Handle, accepted.Handle)) return false;
            }
            if (transformCheckpoint.HasValue)
            {
                if (historicalPublication is not null && !historicalPublication.TryAdoptBaseline(npcs, in accepted))
                    return false;
                replication?.ObserveMaterializedTransform(in accepted);
            }
            if (replication is not null)
            {
                bool networkAccepted = aiRetirementNetworkStaged
                    ? replication.TryAdoptNpcPhaseAiRetirement(in accepted, initialNetworkSpam)
                    : replication.TryAdoptNpcPhaseSpamAdjustment(in accepted, initialNetworkSpam, networkSpamAdjustment);
                if (!networkAccepted) return false;
            }
            if (replication is not null && source.TypeIdentity == VanillaNpcIds.MysticFrog)
            {
                foreach (var frame in beforeTransformFrames)
                    if (frame.Home && !replication.TryAdoptSourceHomeUpdate(in accepted, frame.Bytes)) return false;
                foreach (var frame in afterTransformFrames)
                    if (frame.Home && !replication.TryAdoptSourceHomeUpdate(in accepted, frame.Bytes)) return false;
            }
            committed = accepted;
            adopted = true;
            return status.ObserveVisualOffer(accepted.Handle, in visual);
        }

        public void PublishStatus()
        {
            try
            {
                if (transformCheckpoint is { } checkpoint)
                {
                    if (historicalPublication is not null)
                        historicalPublication.Publish(() => npcs.TryPublishHistoricalCheckpoint(in checkpoint, in committed),
                            CurrentCommittedStatus);
                    else
                    {
                        status.PublishExpired(in initialPhase);
                        npcs.TryPublishHistoricalCheckpoint(in checkpoint, in committed);
                    }
                }
                projectileBatch?.TryPublishBirthJournal();
            }
            finally
            {
                if (!transformCheckpoint.HasValue) status.PublishExpired(in initialPhase);
                // Birth/expiration observers may replace the physical NPC or
                // apply a newer buff list. Never send old slot-only54 bytes to it.
                if (!transformCheckpoint.HasValue)
                    foreach (var frame in beforeTransformFrames)
                    {
                        if (!CurrentCommittedStatus()) break;
                        if (frame.Home) replication?.PublishGemBunnyHome(frame.Bytes);
                        else replication?.PublishGemBunnyBuffs(frame.Bytes);
                    }
            }
        }

        public void Dispose() => projectileBatch?.Dispose();
    }
}
