using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    private bool TryExecuteImportedLoot(in NpcSnapshot npc, bool eaterBoss)
    {
        if (VanillaEaterOfWorldsLifecycle.IsSegment(npc.TypeIdentity))
            return TryExecuteEaterOfWorldsLoot(in npc, eaterBoss);
        if (npc.TypeIdentity == VanillaNpcIds.BrainOfCthulhu || npc.TypeIdentity == VanillaNpcIds.BrainCreeper)
            return TryExecuteBrainOfCthulhuLoot(in npc);
        if (npc.TypeIdentity == VanillaNpcIds.SkeletronHead)
            return TryExecuteSkeletronLoot(in npc);
        if (npc.TypeIdentity == VanillaNpcIds.QueenBee)
            return TryExecuteQueenBeeLoot(in npc);
        if (npc.TypeIdentity == VanillaNpcIds.Deerclops)
            return TryExecuteDeerclopsLoot(in npc);
        if (npc.TypeIdentity == VanillaNpcIds.WallOfFlesh)
            return TryExecuteWallOfFleshLoot(in npc);

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

    private bool TryExecuteSkeletronLoot(in NpcSnapshot npc)
    {
        if (!interactions.TryCopyInteractingSlots(npc.Handle, interactionSlots, out int interactionCount) ||
            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.SkeletronHead, out VanillaNpcDefinition definition))
        {
            return false;
        }

        int activeCount = 0;
        for (int index = 0; index < interactionCount; index++)
        {
            PlayerSlotId slot = interactionSlots[index];
            if (!players.TryGetPlayer(slot, out PlayerStateSnapshot player))
                continue;
            activeSkeletronLootPlayers[activeCount++] = new VanillaSkeletronLootPlayer(
                slot,
                player.PositionX + VanillaPlayerWidth * 0.5f,
                player.PositionY + VanillaPlayerHeight * 0.5f);
        }

        var origin = new NpcLootWorldItemOrigin(
            (int)npc.PositionX + definition.Width * 0.5f,
            (int)npc.PositionY + definition.Height * 0.5f);
        var context = new VanillaSkeletronLootContext(
            expertMode,
            masterMode,
            RedHatAdjustmentsEnabled: false);
        return VanillaSkeletronLootEvaluator.TryExecute(
            in context,
            in origin,
            activeSkeletronLootPlayers.AsSpan(0, activeCount),
            random,
            skeletronLoot,
            out _);
    }

    private bool TryExecuteQueenBeeLoot(in NpcSnapshot npc)
    {
        if (!interactions.TryCopyInteractingSlots(npc.Handle, interactionSlots, out int interactionCount) ||
            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.QueenBee, out VanillaNpcDefinition definition))
            return false;

        int activeCount = 0;
        for (int index = 0; index < interactionCount; index++)
        {
            PlayerSlotId slot = interactionSlots[index];
            if (!players.TryGetPlayer(slot, out PlayerStateSnapshot player))
                continue;
            activeQueenBeeLootPlayers[activeCount++] = new VanillaQueenBeeLootPlayer(
                slot,
                player.PositionX + VanillaPlayerWidth * 0.5f,
                player.PositionY + VanillaPlayerHeight * 0.5f);
        }

        var origin = new NpcLootWorldItemOrigin(
            (int)npc.PositionX + definition.Width * 0.5f,
            (int)npc.PositionY + definition.Height * 0.5f);
        var context = new VanillaQueenBeeLootContext(expertMode, masterMode);
        return VanillaQueenBeeLootEvaluator.TryExecute(
            in context,
            in origin,
            activeQueenBeeLootPlayers.AsSpan(0, activeCount),
            random,
            queenBeeLoot,
            out _);
    }

    private bool TryExecuteDeerclopsLoot(in NpcSnapshot npc)
    {
        if (!interactions.TryCopyInteractingSlots(npc.Handle, interactionSlots, out int interactionCount) ||
            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.Deerclops, out VanillaNpcDefinition definition))
        {
            return false;
        }

        int activeCount = 0;
        for (int index = 0; index < interactionCount; index++)
        {
            PlayerSlotId slot = interactionSlots[index];
            if (!players.TryGetPlayer(slot, out PlayerStateSnapshot player))
                continue;

            activeDeerclopsLootPlayers[activeCount++] = new VanillaDeerclopsLootPlayer(
                slot,
                player.PositionX + VanillaPlayerWidth * 0.5f,
                player.PositionY + VanillaPlayerHeight * 0.5f);
        }

        var origin = new NpcLootWorldItemOrigin(
            (int)npc.PositionX + definition.Width * 0.5f,
            (int)npc.PositionY + definition.Height * 0.5f);
        var context = new VanillaDeerclopsLootContext(expertMode, masterMode);
        return VanillaDeerclopsLootEvaluator.TryExecute(
            in context,
            in origin,
            activeDeerclopsLootPlayers.AsSpan(0, activeCount),
            random,
            deerclopsLoot,
            out _);
    }

    private bool TryExecuteWallOfFleshLoot(in NpcSnapshot npc)
    {
        if (!interactions.TryCopyInteractingSlots(npc.Handle, interactionSlots, out int interactionCount) ||
            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.WallOfFlesh, out VanillaNpcDefinition definition))
            return false;

        int activeCount = 0;
        for (int index = 0; index < interactionCount; index++)
        {
            PlayerSlotId slot = interactionSlots[index];
            if (!players.TryGetPlayer(slot, out PlayerStateSnapshot player))
                continue;
            activeWallOfFleshLootPlayers[activeCount++] = new VanillaWallOfFleshLootPlayer(
                slot,
                player.PositionX + VanillaPlayerWidth * 0.5f,
                player.PositionY + VanillaPlayerHeight * 0.5f);
        }

        var origin = new NpcLootWorldItemOrigin(
            (int)npc.PositionX + definition.Width * 0.5f,
            (int)npc.PositionY + definition.Height * 0.5f);
        var context = new VanillaWallOfFleshLootContext(expertMode, masterMode);
        return VanillaWallOfFleshLootEvaluator.TryExecute(
            in context,
            in origin,
            activeWallOfFleshLootPlayers.AsSpan(0, activeCount),
            random,
            wallOfFleshLoot,
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
    }}
