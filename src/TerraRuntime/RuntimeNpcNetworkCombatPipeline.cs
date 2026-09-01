using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime;

internal enum RuntimeNpcNetworkDamageResult : byte
{
    Rejected = 0,
    Relayed = 1,
    Committed = 2,
    Killed = 3
}

/// <summary>
/// Authoritative packet-28 bridge. It resolves wrapped wire generations against the live slot, preserves the
/// TerrariaServer ordering PlayerInteraction -> StrikeNPC -> imported loot -> boss death effects -> despawn -> packet 28
/// -> packet 23, including shared Eater-of-Worlds interaction credit and last-segment boss promotion. Socket threads never
/// mutate runtime entity state directly.
/// </summary>
internal sealed class RuntimeNpcNetworkCombatPipeline : IRuntimeTownNpcMeleeDamageSink1458
{
    private const int MaxOrdinaryDrops = 16;
    private const float VanillaPlayerWidth = 20f;
    private const float VanillaPlayerHeight = 42f;

    private readonly RuntimeNpcStore npcs;
    private readonly RuntimeWorldItemStore worldItems;
    private readonly RuntimeNpcPlayerInteractionLedger interactions;
    private readonly RuntimeNpcDamageExecutor damage;
    private readonly RuntimeNpcReplicationRegistry? npcReplication;
    private readonly IRuntimePlayerSlotSnapshotLookup players;
    private readonly RuntimeKingSlimeDifficultyLootDeliverySink? difficultyLoot;
    private readonly RuntimeEaterOfWorldsLootDeliverySink eaterLoot;
    private readonly RuntimeBrainOfCthulhuLootDeliverySink brainLoot;
    private readonly VanillaNpcLootWorldItemMaterializer materializer = VanillaNpcLootWorldItemMaterializer.Instance;
    private readonly SystemNpcCombatRandom random = new();
    private readonly bool expertMode;
    private readonly bool masterMode;
    private readonly WorldTileStore? worldTiles;
    private readonly RuntimeWorldClock? worldClock;
    private readonly PlayerSlotId[] interactionSlots =
        new PlayerSlotId[RuntimeNpcPlayerInteractionLedger.VanillaInteractablePlayerSlots];
    private readonly VanillaKingSlimeLootPlayer[] activeLootPlayers =
        new VanillaKingSlimeLootPlayer[RuntimeNpcPlayerInteractionLedger.VanillaInteractablePlayerSlots];
    private readonly VanillaEaterOfWorldsLootPlayer[] activeEaterLootPlayers =
        new VanillaEaterOfWorldsLootPlayer[RuntimeNpcPlayerInteractionLedger.VanillaInteractablePlayerSlots];
    private readonly VanillaBrainOfCthulhuLootPlayer[] activeBrainLootPlayers =
        new VanillaBrainOfCthulhuLootPlayer[RuntimeNpcPlayerInteractionLedger.VanillaInteractablePlayerSlots];
    private readonly NpcSnapshot[] npcFamilyBuffer;

    public RuntimeNpcNetworkCombatPipeline(
        RuntimeNpcStore npcs,
        RuntimeWorldItemStore worldItems,
        IRuntimePlayerSlotSnapshotLookup players,
        RuntimeNpcReplicationRegistry? npcReplication,
        RuntimeWorldItemInstancedLeaseStore instancedLeases,
        RuntimeWorldItemReplicationRegistry? worldItemReplication,
        WorldTileStore? worldTiles,
        RuntimeWorldClock? worldClock,
        bool expertMode,
        bool masterMode)
    {
        this.npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        this.worldItems = worldItems ?? throw new ArgumentNullException(nameof(worldItems));
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.npcReplication = npcReplication;
        this.worldTiles = worldTiles;
        this.worldClock = worldClock;
        this.expertMode = expertMode;
        this.masterMode = masterMode;
        if (masterMode && !expertMode)
            throw new ArgumentException("Master mode is a strict subset of Expert mode.", nameof(masterMode));

        interactions = new RuntimeNpcPlayerInteractionLedger(npcs);
        damage = new RuntimeNpcDamageExecutor(npcs, expertMode, interactions);
        npcFamilyBuffer = new NpcSnapshot[npcs.Capacity];
        eaterLoot = new RuntimeEaterOfWorldsLootDeliverySink(
            worldItems,
            instancedLeases,
            worldItemReplication);
        brainLoot = new RuntimeBrainOfCthulhuLootDeliverySink(
            worldItems,
            instancedLeases,
            worldItemReplication);
        if (worldItemReplication is not null)
        {
            difficultyLoot = new RuntimeKingSlimeDifficultyLootDeliverySink(
                worldItems,
                instancedLeases ?? throw new ArgumentNullException(nameof(instancedLeases)),
                worldItemReplication);
        }
    }

    public RuntimeNpcNetworkDamageResult TryApply(
        ConnectionHandle connection,
        in TerrariaNpcDamageState wireState)
    {
        if (!connection.IsAssigned || !wireState.IsStructurallyValid)
            return RuntimeNpcNetworkDamageResult.Rejected;

        // TerrariaServer emits packet 162 before resolving packet-28 NPC generation.
        npcReplication?.TryAcknowledgeDamage(connection.Source);

        if (wireState.NpcSlot >= TerrariaNpcDamageCodec.VanillaNpcSlots ||
            !npcs.TryGetActive(wireState.NpcSlot, out NpcSnapshot current) ||
            RuntimeNpcPacketProjection.ToProtocolGeneration(current.Handle.Generation) != wireState.Generation)
        {
            return RuntimeNpcNetworkDamageResult.Rejected;
        }

        short normalizedDamage = Math.Max(wireState.Damage, (short)0);
        float normalizedKnockBack = Math.Max(wireState.KnockBack, 0f);
        int normalizedHitDirection = Math.Clamp(wireState.HitDirection, -1, 1);
        var normalizedWire = new TerrariaNpcDamageState(
            wireState.NpcSlot,
            wireState.Generation,
            normalizedDamage,
            normalizedKnockBack,
            checked((byte)(normalizedHitDirection + 1)),
            wireState.Critical ? (byte)1 : (byte)0);

        // PlayerInteraction occurs before StrikeNPC in MessageBuffer case 28. Keep credit even when the strike itself
        // is rejected by invulnerability or another authoritative combat guard.
        if (VanillaEaterOfWorldsLifecycle.IsSegment(current.TypeIdentity))
        {
            VanillaEaterOfWorldsLifecycle.MarkPlayerInteractionAcrossActiveSegments(
                npcs,
                interactions,
                connection.Player,
                npcFamilyBuffer);
        }
        else
        {
            interactions.TryMark(current.Handle, connection.Player);
        }

        var request = new NpcDamageRequest(
            current.Handle,
            DamageSource.FromPlayerItem(connection.Player),
            normalizedDamage,
            Critical: normalizedWire.Critical,
            KnockBack: normalizedWire.KnockBack,
            HitDirection: normalizedHitDirection);

        bool suppressing = npcReplication?.TryBeginClientDamage(current.Handle) == true;
        try
        {
            if (!damage.TryApply(in request, out NpcDamageResult result))
            {
                if (suppressing)
                    npcReplication!.CompleteClientDamage(current.Handle);
                npcReplication?.TryPublishDamage(connection.Source, in normalizedWire);
                return RuntimeNpcNetworkDamageResult.Relayed;
            }

            if (!result.Lethal)
            {
                if (suppressing)
                    npcReplication!.CompleteClientDamage(current.Handle);
                npcReplication?.TryPublishDamage(connection.Source, in normalizedWire);
                return RuntimeNpcNetworkDamageResult.Committed;
            }

            if (!npcs.TryGet(current.Handle, out NpcSnapshot dead))
                throw new InvalidOperationException("A lethal packet-28 commit disappeared before death finalization.");

            bool eaterBoss =
                VanillaEaterOfWorldsLifecycle.IsSegment(dead.TypeIdentity) &&
                VanillaEaterOfWorldsLifecycle.IsLastActiveSegment(npcs, in dead, npcFamilyBuffer);

            if (!TryExecuteImportedLoot(in dead, eaterBoss))
                throw new InvalidOperationException("Imported NPC loot could not be finalized after a lethal packet-28 commit.");

            if (dead.TypeIdentity == VanillaNpcIds.KingSlime)
                ApplyKingSlimeDeathEffects(in dead);
            else if (eaterBoss || dead.TypeIdentity == VanillaNpcIds.BrainOfCthulhu)
                ApplyEvilBossDeathEffects();

            if (!npcs.TryDespawn(dead.Handle))
                throw new InvalidOperationException("A lethal packet-28 NPC could not be despawned after death effects.");
            interactions.Forget(dead.Handle);

            if (suppressing)
                npcReplication!.CompleteClientDamage(dead.Handle);
            npcReplication?.TryPublishDamage(connection.Source, in normalizedWire);
            npcReplication?.TryPublishDeath(in dead);
            return RuntimeNpcNetworkDamageResult.Killed;
        }
        catch
        {
            if (suppressing)
                npcReplication!.AbortClientDamage(current.Handle);
            throw;
        }
    }


    public RuntimeTownNpcMeleeDamageResult1458 TryStrike(
        NpcHandle attacker,
        NpcHandle target,
        int baseDamage,
        float knockBack,
        int hitDirection)
    {
        if (!attacker.IsAssigned || !target.IsAssigned || baseDamage < 0 ||
            !float.IsFinite(knockBack) || knockBack < 0f || hitDirection is not (-1 or 1) ||
            !npcs.TryGet(attacker, out NpcSnapshot liveAttacker) || !liveAttacker.IsActive ||
            !npcs.TryGet(target, out NpcSnapshot liveTarget) || !liveTarget.IsActive)
        {
            return RuntimeTownNpcMeleeDamageResult1458.Rejected;
        }

        var request = new NpcDamageRequest(
            liveTarget.Handle,
            DamageSource.FromNpcContact(liveAttacker.Handle),
            baseDamage,
            KnockBack: knockBack,
            HitDirection: hitDirection);
        if (!damage.TryApply(in request, out NpcDamageResult result))
            return RuntimeTownNpcMeleeDamageResult1458.Rejected;
        if (!result.Lethal)
            return RuntimeTownNpcMeleeDamageResult1458.Committed;

        if (!npcs.TryGet(liveTarget.Handle, out NpcSnapshot dead))
            throw new InvalidOperationException("A lethal Town NPC melee commit disappeared before death finalization.");

        bool eaterBoss =
            VanillaEaterOfWorldsLifecycle.IsSegment(dead.TypeIdentity) &&
            VanillaEaterOfWorldsLifecycle.IsLastActiveSegment(npcs, in dead, npcFamilyBuffer);
        if (!TryExecuteImportedLoot(in dead, eaterBoss))
            throw new InvalidOperationException("Imported NPC loot could not be finalized after Town NPC melee.");

        if (dead.TypeIdentity == VanillaNpcIds.KingSlime)
            ApplyKingSlimeDeathEffects(in dead);
        else if (eaterBoss || dead.TypeIdentity == VanillaNpcIds.BrainOfCthulhu)
            ApplyEvilBossDeathEffects();

        if (!npcs.TryDespawn(dead.Handle))
            throw new InvalidOperationException("A Town NPC melee kill could not despawn the exact NPC generation.");
        interactions.Forget(dead.Handle);
        npcReplication?.TryPublishDeath(in dead);
        return RuntimeTownNpcMeleeDamageResult1458.Killed;
    }

    private bool TryExecuteImportedLoot(in NpcSnapshot npc, bool eaterBoss)
    {
        if (VanillaEaterOfWorldsLifecycle.IsSegment(npc.TypeIdentity))
            return TryExecuteEaterOfWorldsLoot(in npc, eaterBoss);
        if (npc.TypeIdentity == VanillaNpcIds.BrainOfCthulhu || npc.TypeIdentity == VanillaNpcIds.BrainCreeper)
            return TryExecuteBrainOfCthulhuLoot(in npc);

        if (npc.TypeIdentity == VanillaNpcIds.KingSlime && expertMode)
            return TryExecuteKingSlimeDifficultyLoot(in npc);

        bool kingSlimeNormal = npc.TypeIdentity == VanillaNpcIds.KingSlime;
        VanillaNpcLootTable genericTable = default;
        if (!kingSlimeNormal && !VanillaNpcLootRuleCatalog.TryGetNpcSpecificTable(npc.TypeIdentity, out genericTable))
            return true;

        int maximumDropCount = kingSlimeNormal
            ? VanillaKingSlimeNormalLootCatalog.MaximumDropCount
            : genericTable.MaximumDropCount;
        if (maximumDropCount > MaxOrdinaryDrops ||
            !VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out VanillaNpcDefinition definition))
        {
            return false;
        }

        Span<WorldItemDropReservation> capacity = stackalloc WorldItemDropReservation[MaxOrdinaryDrops];
        Span<WorldItemDropReservation> staged = stackalloc WorldItemDropReservation[MaxOrdinaryDrops];
        int reserved = 0;
        for (; reserved < maximumDropCount; reserved++)
        {
            if (worldItems.TryReserveDropSlot(out capacity[reserved]))
                continue;
            ReleaseReservations(capacity[..reserved]);
            return false;
        }

        var origin = new NpcLootWorldItemOrigin(
            (int)npc.PositionX + definition.Width * 0.5f,
            (int)npc.PositionY + definition.Height * 0.5f);
        int stagedCount = 0;
        var context = new VanillaNpcLootContext(expertMode, DropExtraGel: false);

        if (kingSlimeNormal)
        {
            ReadOnlySpan<VanillaKingSlimeNormalLootRule> rules = VanillaKingSlimeNormalLootCatalog.Rules;
            for (int index = 0; index < rules.Length; index++)
            {
                if (!VanillaKingSlimeNormalLootEvaluator.TryEvaluateRule(
                        in rules[index], random, out bool dropped, out NpcLootDrop drop))
                {
                    ReleaseReservations(capacity);
                    ReleaseReservations(staged[..stagedCount]);
                    return false;
                }
                if (dropped && !StageDrop(in origin, in drop, capacity, staged, ref stagedCount))
                    return false;
            }
        }
        else
        {
            ReadOnlySpan<VanillaNpcLootRule> rules = genericTable.Rules;
            for (int index = 0; index < rules.Length; index++)
            {
                if (!VanillaNpcLootEvaluator.TryEvaluateRule(
                        in rules[index], in context, random, out bool dropped, out NpcLootDrop drop))
                {
                    ReleaseReservations(capacity);
                    ReleaseReservations(staged[..stagedCount]);
                    return false;
                }
                if (dropped && !StageDrop(in origin, in drop, capacity, staged, ref stagedCount))
                    return false;
            }
        }

        ReleaseReservations(capacity[stagedCount..maximumDropCount]);
        for (int index = 0; index < stagedCount; index++)
        {
            if (!worldItems.TryCommitReservedDrop(in staged[index], out _))
                throw new InvalidOperationException("A staged NPC-loot reservation failed after source-ordered evaluation.");
        }
        return true;
    }

    private bool StageDrop(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        Span<WorldItemDropReservation> capacity,
        Span<WorldItemDropReservation> staged,
        ref int stagedCount)
    {
        if (!materializer.TryMaterialize(in origin, in drop, random, out WorldItemDropStateUpdate materialized))
        {
            ReleaseReservations(capacity);
            ReleaseReservations(staged[..stagedCount]);
            return false;
        }

        int capacityIndex = stagedCount;
        if (!worldItems.TryReleaseDropReservation(in capacity[capacityIndex]))
            throw new InvalidOperationException("Failed to release an exact NPC-loot capacity reservation.");
        capacity[capacityIndex] = default;
        if (!worldItems.TryReserveDrop(in materialized, out staged[stagedCount]))
            throw new InvalidOperationException("Preflighted NPC loot lost reserved world-item capacity.");
        stagedCount++;
        return true;
    }

    private bool TryExecuteEaterOfWorldsLoot(in NpcSnapshot npc, bool isBoss)
    {
        if (!interactions.TryCopyInteractingSlots(npc.Handle, interactionSlots, out int interactionCount) ||
            !VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out VanillaNpcDefinition definition))
        {
            return false;
        }

        int activeCount = 0;
        for (int index = 0; index < interactionCount; index++)
        {
            PlayerSlotId slot = interactionSlots[index];
            if (!players.TryGetPlayer(slot, out PlayerStateSnapshot player))
                continue;
            activeEaterLootPlayers[activeCount++] = new VanillaEaterOfWorldsLootPlayer(
                slot,
                player.PositionX + VanillaPlayerWidth * 0.5f,
                player.PositionY + VanillaPlayerHeight * 0.5f);
        }

        var origin = new NpcLootWorldItemOrigin(
            (int)npc.PositionX + definition.Width * 0.5f,
            (int)npc.PositionY + definition.Height * 0.5f);
        var context = new VanillaEaterOfWorldsLootContext(expertMode, masterMode, isBoss);
        return VanillaEaterOfWorldsLootEvaluator.TryExecute(
            in context,
            in origin,
            activeEaterLootPlayers.AsSpan(0, activeCount),
            random,
            eaterLoot,
            out _);
    }

    private bool TryExecuteBrainOfCthulhuLoot(in NpcSnapshot npc)
    {
        if (!VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out VanillaNpcDefinition definition))
            return false;

        int activeCount = 0;
        if (npc.TypeIdentity == VanillaNpcIds.BrainOfCthulhu)
        {
            if (!interactions.TryCopyInteractingSlots(npc.Handle, interactionSlots, out int interactionCount))
                return false;

            for (int index = 0; index < interactionCount; index++)
            {
                PlayerSlotId slot = interactionSlots[index];
                if (!players.TryGetPlayer(slot, out PlayerStateSnapshot player))
                    continue;
                activeBrainLootPlayers[activeCount++] = new VanillaBrainOfCthulhuLootPlayer(
                    slot,
                    player.PositionX + VanillaPlayerWidth * 0.5f,
                    player.PositionY + VanillaPlayerHeight * 0.5f);
            }
        }

        var origin = new NpcLootWorldItemOrigin(
            (int)npc.PositionX + definition.Width * 0.5f,
            (int)npc.PositionY + definition.Height * 0.5f);
        var context = new VanillaBrainOfCthulhuLootContext(expertMode, masterMode, npc.TypeIdentity);
        return VanillaBrainOfCthulhuLootEvaluator.TryExecute(
            in context,
            in origin,
            activeBrainLootPlayers.AsSpan(0, activeCount),
            random,
            brainLoot,
            out _);
    }

    private bool TryExecuteKingSlimeDifficultyLoot(in NpcSnapshot npc)
    {
        if (difficultyLoot is null ||
            !interactions.TryCopyInteractingSlots(npc.Handle, interactionSlots, out int interactionCount) ||
            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.KingSlime, out VanillaNpcDefinition definition))
        {
            return false;
        }

        int activeCount = 0;
        for (int index = 0; index < interactionCount; index++)
        {
            PlayerSlotId slot = interactionSlots[index];
            if (!players.TryGetPlayer(slot, out PlayerStateSnapshot player))
                continue;
            activeLootPlayers[activeCount++] = new VanillaKingSlimeLootPlayer(
                slot,
                player.PositionX + VanillaPlayerWidth * 0.5f,
                player.PositionY + VanillaPlayerHeight * 0.5f);
        }

        var origin = new NpcLootWorldItemOrigin(
            (int)npc.PositionX + definition.Width * 0.5f,
            (int)npc.PositionY + definition.Height * 0.5f);
        var context = new VanillaKingSlimeDifficultyLootContext(expertMode, masterMode);
        return VanillaKingSlimeDifficultyLootEvaluator.TryExecute(
            in context,
            in origin,
            activeLootPlayers.AsSpan(0, activeCount),
            random,
            difficultyLoot,
            out _);
    }

    private void ApplyEvilBossDeathEffects()
    {
        if (worldTiles is null)
            return;
        RuntimeWorldProgressionRegistry.GetOrCreate(worldTiles)
            .MarkCompleted(VanillaWorldProgressionId.EvilBoss);
    }

    private void ApplyKingSlimeDeathEffects(in NpcSnapshot kingSlime)
    {
        RuntimeWorldProgressionMutations? progression = worldTiles is null
            ? null
            : RuntimeWorldProgressionRegistry.GetOrCreate(worldTiles);
        progression?.SetSlimeBlueSpawnBaseline(worldClock?.SlimeBlueSpawnUnlocked == true);

        worldClock?.TryStopSlimeRain(random);
        if (worldClock is not null && progression?.MarkSlimeBlueSpawnUnlocked() == true)
        {
            worldClock.MarkSlimeBlueSpawnUnlocked();
            if (TryCreateNerdySlimeSpawnIntent(in kingSlime, out NpcAiSpawnIntent intent) &&
                TryApplySpawnIntent(in intent, out NpcSnapshot nerdy))
            {
                float velocityX = random.NextFloatDirection() * 3f;
                var update = new NpcStateUpdate(
                    nerdy.Type,
                    nerdy.NetId,
                    nerdy.PositionX,
                    nerdy.PositionY,
                    velocityX,
                    -10f,
                    nerdy.Target,
                    nerdy.Ai,
                    nerdy.Simulation);
                if (!npcs.TryUpdate(nerdy.Handle, in update, out _))
                    throw new InvalidOperationException("Nerdy Slime death spawn could not receive launch velocity.");
            }
        }
        progression?.MarkCompleted(VanillaWorldProgressionId.KingSlime);
    }

    private static bool TryCreateNerdySlimeSpawnIntent(in NpcSnapshot source, out NpcAiSpawnIntent intent)
    {
        intent = default;
        if (!VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.KingSlime, out VanillaNpcDefinition definition) ||
            !definition.TryResolveHitbox(source.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
        {
            return false;
        }

        float centerX = source.PositionX + hitbox.Width * 0.5f;
        float centerY = source.PositionY + hitbox.Height * 0.5f;
        intent = new NpcAiSpawnIntent(
            VanillaNpcIds.TownSlimeBlue,
            BottomX: (int)centerX - 10,
            BottomY: (int)centerY,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: checked((ushort)VanillaNpcDefinitionCatalog.DefaultTarget));
        return true;
    }

    private bool TryApplySpawnIntent(in NpcAiSpawnIntent intent, out NpcSnapshot spawned)
    {
        if (!VanillaNpcDefinitionCatalog.TryGet(intent.Type, out VanillaNpcDefinition definition) ||
            !float.IsFinite(intent.VelocityX) ||
            !float.IsFinite(intent.VelocityY) ||
            !intent.InitialAi.IsFinite)
        {
            spawned = default;
            return false;
        }

        var update = new NpcStateUpdate(
            Type: intent.Type.Value,
            NetId: checked((short)intent.Type.Value),
            PositionX: intent.BottomX - definition.Width * 0.5f,
            PositionY: intent.BottomY - definition.Height,
            VelocityX: intent.VelocityX,
            VelocityY: intent.VelocityY,
            Target: intent.Target,
            Ai: intent.InitialAi,
            Simulation: NpcSimulationState.Initial with
            {
                TimeLeft = VanillaNpcSpawnFacts.NewNpcTimeLeft
            });
        return npcs.TrySpawnVanilla(in update, out spawned);
    }

    private void ReleaseReservations(Span<WorldItemDropReservation> reservations)
    {
        for (int index = 0; index < reservations.Length; index++)
        {
            if (reservations[index].IsAssigned)
                worldItems.TryReleaseDropReservation(in reservations[index]);
        }
    }

    private sealed class SystemNpcCombatRandom : INpcLootRollSource, IKingSlimeDeathRandom
    {
        private readonly Random random = new();

        public int RollLuck(int chanceDenominator)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(chanceDenominator, 1);
            return random.Next(chanceDenominator);
        }

        public int NextInt32(int inclusiveMin, int exclusiveMax) => random.Next(inclusiveMin, exclusiveMax);

        public float NextFloatDirection() => random.NextSingle() * 2f - 1f;
    }
}
