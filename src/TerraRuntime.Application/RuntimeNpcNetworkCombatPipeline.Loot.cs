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
    private void ExecuteNpcNonlethalHitEffects(in NpcSnapshot npc, int resolvedDamage = 0)
    {
        if (npc.TypeIdentity == VanillaNpcIds.LavaSlime && npc.Simulation.LifeMax > 0)
        {
            // HitEffect uses the post-defense damage and a double threshold, including a fractional final particle.
            double particles = (double)resolvedDamage / npc.Simulation.LifeMax * 80.0;
            for (int particle = 0; particle < particles; particle++) random.NextInt32(0, 8);
        }
        // Dedicated NPC.HitEffect still makes the Eskimo Zombie's gore choice, even though
        // Gore.NewGore and Dust.NewDust perform no server-side particle allocation.
        if (npc.TypeIdentity.Value == 186) random.NextInt32(0, 5);
    }

    private void ExecuteNpcDeathHitEffects(in NpcSnapshot npc)
    {
        if (npc.TypeIdentity == VanillaNpcIds.LavaSlime)
        {
            // Dedicated Dust.NewDust returns early, but its caller still selects noGravity forty times.
            for (int particle = 0; particle < 40; particle++) random.NextInt32(0, 8);
        }
        VanillaTownNpcDeathHitEffect1458.ConsumeLethalChoices(npc.TypeIdentity, random);
        // Both Eskimo identities choose a death gore variant on the dedicated server.
        if (npc.TypeIdentity.Value is 186 or 432) random.NextInt32(0, 2);
        if (npc.TypeIdentity == VanillaNpcIds.Bee || npc.TypeIdentity == VanillaNpcIds.SmallBee)
        {
            // NPC.HitEffect (1.4.5.8) still makes six noGravity choices after dedicated Dust.NewDust returns.
            for (int particle = 0; particle < 6; particle++) random.NextInt32(0, 2);
        }
        if (npc.TypeIdentity == VanillaNpcIds.Slimer && EnsurePreviewSpawnStream() &&
            !DeathNpcs.TryExecuteSlimerDeathSpawn(in npc, out _, out _))
            deathPreviewFailed = true;
        if (npc.TypeIdentity == VanillaNpcIds.MotherSlime && EnsurePreviewSpawnStream())
            VanillaMotherSlimeDeathSplit1458.SpawnChildren(DeathNpcs, in npc, random);
    }

    private bool TryGetActiveLootPlayer(PlayerSlotId slot, out PlayerStateSnapshot player)
    {
        if (!players.TryGetPlayer(slot, out player))
            return false;
        // Runtime-controlled players participate in combat but have no vanilla client-local item consumer.
        // Until an explicit actor-owned instanced-loot adapter exists, their personalized difficulty rewards
        // fail closed. Never send their copy to an observer, grant it directly to inventory, or abort human loot.
        // Classic ordinary drops and normal bot pickup are unaffected.
        return !expertMode || (worldItemReplication?.HasClientLocalItemReceiver(player.Player) ?? false);
    }

    private bool TryExecuteImportedLoot(in NpcSnapshot npc, bool eaterBoss)
    {
        if (!IsPreviewingDeath && pendingDeathPlan is { } plan)
        {
            if (plan.IsUnpublishedMode)
            {
                // The selected canonical HitEffect choices were consumed by the detached death preview.
                // Items/RNG and the ledger are already adopted before the first status54.
                if (!plan.IsAdoptedUnpublished ||
                    !plan.TryPublishPhase(NpcDeathDropPhase1458.Prelude, lootDelivery.Adopt) ||
                    !PublishPreparedDeathPrelude()) return false;
                return plan.TryPublishPhase(NpcDeathDropPhase1458.Imported, lootDelivery.Adopt);
            }
            if (!plan.CanBeginDeathHitEffects() || deathPrelude.Revision != plannedPreludeRevision) return false;
            ExecuteNpcDeathHitEffects(in npc);
            if (!plan.TryPublishPhase(NpcDeathDropPhase1458.Prelude, lootDelivery.Adopt) ||
                !deathPrelude.TryPublish(plannedPrelude!, plannedPreludeRevision)) return false;
            return plan.TryPublishPhase(NpcDeathDropPhase1458.Imported, lootDelivery.Adopt);
        }
        if (!TryExecuteMechSpawnersLoot(in npc)) return false;
        if (!TryExecuteSlimeBodyLoot(in npc)) return false;
        if (!TryExecuteGlobalLoot(in npc)) return false;
        if (VanillaTownNpcLootRules1458.TryGet(npc.TypeIdentity, out _)) return TryExecuteTownSpecificLoot(in npc);
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
        if (npc.TypeIdentity == VanillaNpcIds.QueenSlime)
            return TryExecuteQueenSlimeLoot(in npc);
        if (npc.TypeIdentity == VanillaNpcIds.Plantera)
            return TryExecutePlanteraLoot(in npc);
        if (npc.TypeIdentity == VanillaNpcIds.MoonLordCore)
            return TryExecuteMoonLordLoot(in npc);
        if (npc.TypeIdentity == VanillaNpcIds.Golem)
            return TryExecuteGolemLoot(in npc);
        if (npc.TypeIdentity == VanillaNpcIds.EyeOfCthulhu)
            return TryExecuteEyeOfCthulhuLoot(in npc);
        if (VanillaMechanicalBossLootEvaluator.IsRoot(npc.TypeIdentity))
            return TryExecuteMechanicalBossLoot(in npc);

        if (npc.TypeIdentity == VanillaNpcIds.KingSlime && expertMode)
            return TryExecuteKingSlimeDifficultyLoot(in npc);

        bool kingSlimeNormal = npc.TypeIdentity == VanillaNpcIds.KingSlime;
        VanillaNpcLootTable genericTable = default;
        if (!kingSlimeNormal && !VanillaNpcLootRuleCatalog.TryGetNpcSpecificTable(npc.TypeIdentity, out genericTable))
            return true;

        int maximumDropCount = kingSlimeNormal
            ? VanillaKingSlimeNormalLootCatalog.MaximumDropCount
            : genericTable.MaximumDropCount;
        int maximumSupportedDrops = npc.Type is >= 212 and <= 215
            ? VanillaInvasionNpcLootCatalog1458.PirateMaximumDropCount
            : MaxOrdinaryDrops;
        if (maximumDropCount > maximumSupportedDrops ||
            !VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out VanillaNpcDefinition definition))
        {
            return false;
        }

        var origin = ResolveNpcLootOrigin(in npc, in definition);
        int stagedCount = 0;
        if (!TryCaptureSpecificLootContext(in npc, out var context)) return false;

        if (kingSlimeNormal)
        {
            ReadOnlySpan<VanillaKingSlimeNormalLootRule> rules = VanillaKingSlimeNormalLootCatalog.Rules;
            for (int index = 0; index < rules.Length; index++)
            {
                if (!VanillaKingSlimeNormalLootEvaluator.TryEvaluateRule(
                        in rules[index], random, out bool dropped, out NpcLootDrop drop))
                {
                    return false;
                }
                if (dropped && !StageDrop(in origin, in drop, ref stagedCount))
                    return false;
            }
        }
        else
        {
            if (!VanillaNpcLootEvaluator.TryValidateNpcSpecificContext(in genericTable, in context)) return false;
            ReadOnlySpan<VanillaNpcLootRule> rules = genericTable.Rules;
            for (int index = 0; index < rules.Length; index++)
            {
                if (!VanillaNpcLootEvaluator.TryEvaluateRule(
                        in rules[index], in context, random, out bool dropped, out NpcLootDrop drop))
                {
                    return false;
                }
                if (dropped && !StageDrop(in origin, in drop, ref stagedCount))
                    return false;
            }
        }

        return true;
    }

    private bool TryExecuteSlimeBodyLoot(in NpcSnapshot npc)
    {
        if (!VanillaSlimeBodyLoot1458.TryEvaluate(npc.TypeIdentity, npc.Ai.Ai1, random,
                out bool dropped, out NpcLootDrop drop)) return false;
        if (!dropped) return true;
        if (!VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out var definition)) return false;
        var origin = ResolveNpcLootOrigin(in npc, in definition);
        return lootDelivery.TryWorld(in origin, in drop, random);
    }

    private bool StageDrop(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop, ref int stagedCount)
    {
        if (!lootDelivery.TryWorld(in origin, in drop, random)) return false;
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
            if (!TryGetActiveLootPlayer(slot, out PlayerStateSnapshot player))
                continue;
            activeEaterLootPlayers[activeCount++] = new VanillaEaterOfWorldsLootPlayer(
                slot,
                player.PositionX + VanillaPlayerWidth * 0.5f,
                player.PositionY + VanillaPlayerHeight * 0.5f);
        }

        var origin = ResolveNpcLootOrigin(in npc, in definition);
        var context = new VanillaEaterOfWorldsLootContext(expertMode, masterMode, isBoss);
        return VanillaEaterOfWorldsLootEvaluator.TryExecute(
            in context,
            in origin,
            activeEaterLootPlayers.AsSpan(0, activeCount),
            random,
            lootDelivery,
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
                if (!TryGetActiveLootPlayer(slot, out PlayerStateSnapshot player))
                    continue;
                activeBrainLootPlayers[activeCount++] = new VanillaBrainOfCthulhuLootPlayer(
                    slot,
                    player.PositionX + VanillaPlayerWidth * 0.5f,
                    player.PositionY + VanillaPlayerHeight * 0.5f);
            }
        }

        var origin = ResolveNpcLootOrigin(in npc, in definition);
        var context = new VanillaBrainOfCthulhuLootContext(expertMode, masterMode, npc.TypeIdentity);
        return VanillaBrainOfCthulhuLootEvaluator.TryExecute(
            in context,
            in origin,
            activeBrainLootPlayers.AsSpan(0, activeCount),
            random,
            lootDelivery,
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
            if (!TryGetActiveLootPlayer(slot, out PlayerStateSnapshot player))
                continue;
            activeSkeletronLootPlayers[activeCount++] = new VanillaSkeletronLootPlayer(
                slot,
                player.PositionX + VanillaPlayerWidth * 0.5f,
                player.PositionY + VanillaPlayerHeight * 0.5f);
        }

        var origin = ResolveNpcLootOrigin(in npc, in definition);
        var context = new VanillaSkeletronLootContext(
            expertMode,
            masterMode,
            RedHatAdjustmentsEnabled: VanillaSkeletronCombat.HasRedHatAdjustments(npc.TypeIdentity, npc.Ai, npc.Simulation.LocalAi));
        return VanillaSkeletronLootEvaluator.TryExecute(
            in context,
            in origin,
            activeSkeletronLootPlayers.AsSpan(0, activeCount),
            random,
            lootDelivery,
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
            if (!TryGetActiveLootPlayer(slot, out PlayerStateSnapshot player))
                continue;
            activeQueenBeeLootPlayers[activeCount++] = new VanillaQueenBeeLootPlayer(
                slot,
                player.PositionX + VanillaPlayerWidth * 0.5f,
                player.PositionY + VanillaPlayerHeight * 0.5f);
        }

        var origin = ResolveNpcLootOrigin(in npc, in definition);
        var context = new VanillaQueenBeeLootContext(expertMode, masterMode);
        return VanillaQueenBeeLootEvaluator.TryExecute(
            in context,
            in origin,
            activeQueenBeeLootPlayers.AsSpan(0, activeCount),
            random,
            lootDelivery,
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
            if (!TryGetActiveLootPlayer(slot, out PlayerStateSnapshot player))
                continue;

            activeDeerclopsLootPlayers[activeCount++] = new VanillaDeerclopsLootPlayer(
                slot,
                player.PositionX + VanillaPlayerWidth * 0.5f,
                player.PositionY + VanillaPlayerHeight * 0.5f);
        }

        var origin = ResolveNpcLootOrigin(in npc, in definition);
        var context = new VanillaDeerclopsLootContext(expertMode, masterMode);
        return VanillaDeerclopsLootEvaluator.TryExecute(
            in context,
            in origin,
            activeDeerclopsLootPlayers.AsSpan(0, activeCount),
            random,
            lootDelivery,
            out _);
    }

    private bool TryExecutePlanteraLoot(in NpcSnapshot npc)
    {
        bool downed = planteraDownedBaseline == true || progression.IsCompleted(VanillaWorldProgressionId.Plantera);
        // Missing loaded-world facts cannot be interpreted as a first kill. Do not invent rewards or
        // turn an otherwise valid death into a fatal error in runtimes without a canonical world baseline.
        if (!expertMode && !downed && !planteraDownedBaseline.HasValue)
            return true;
        if (!interactions.TryCopyInteractingSlots(npc.Handle, interactionSlots, out int interactionCount) ||
            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.Plantera, out VanillaNpcDefinition definition))
            return false;

        int activeCount = 0;
        for (int index = 0; index < interactionCount; index++)
        {
            PlayerSlotId slot = interactionSlots[index];
            if (!TryGetActiveLootPlayer(slot, out PlayerStateSnapshot player))
                continue;
            activePlanteraLootPlayers[activeCount++] = new VanillaPlanteraLootPlayer(slot,
                player.PositionX + VanillaPlayerWidth * 0.5f,
                player.PositionY + VanillaPlayerHeight * 0.5f);
        }
        var origin = ResolveNpcLootOrigin(in npc, in definition);
        var context = new VanillaPlanteraLootContext(expertMode, masterMode, downed);
        return VanillaPlanteraLootEvaluator.TryExecute(in context, in origin,
            activePlanteraLootPlayers.AsSpan(0, activeCount), random, lootDelivery, out _);
    }

    private bool TryExecuteGolemLoot(in NpcSnapshot npc)
    {
        if (!interactions.TryCopyInteractingSlots(npc.Handle, interactionSlots, out int interactionCount) ||
            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.Golem, out VanillaNpcDefinition definition))
        {
            return false;
        }

        int activeCount = 0;
        for (int index = 0; index < interactionCount; index++)
        {
            PlayerSlotId slot = interactionSlots[index];
            if (!TryGetActiveLootPlayer(slot, out PlayerStateSnapshot player))
                continue;

            activeGolemLootPlayers[activeCount++] = new VanillaGolemLootPlayer(
                slot,
                player.PositionX + VanillaPlayerWidth * 0.5f,
                player.PositionY + VanillaPlayerHeight * 0.5f);
        }

        var origin = ResolveNpcLootOrigin(in npc, in definition);
        var context = new VanillaGolemLootContext(expertMode, masterMode);
        return VanillaGolemLootEvaluator.TryExecute(
            in context,
            in origin,
            activeGolemLootPlayers.AsSpan(0, activeCount),
            random,
            lootDelivery,
            out _);
    }

    private bool TryExecuteMoonLordLoot(in NpcSnapshot npc)
    {
        if (!interactions.TryCopyInteractingSlots(npc.Handle, interactionSlots, out int interactionCount) ||
            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.MoonLordCore, out VanillaNpcDefinition definition))
        {
            return false;
        }

        int activeCount = 0;
        for (int index = 0; index < interactionCount; index++)
        {
            PlayerSlotId slot = interactionSlots[index];
            if (!TryGetActiveLootPlayer(slot, out PlayerStateSnapshot player))
                continue;

            activeMoonLordLootPlayers[activeCount++] = new VanillaMoonLordLootPlayer(
                slot,
                player.PositionX + VanillaPlayerWidth * 0.5f,
                player.PositionY + VanillaPlayerHeight * 0.5f);
        }

        var origin = ResolveNpcLootOrigin(in npc, in definition);
        var context = new VanillaMoonLordLootContext(expertMode, masterMode);
        return VanillaMoonLordLootEvaluator.TryExecute(
            in context,
            in origin,
            activeMoonLordLootPlayers.AsSpan(0, activeCount),
            random,
            lootDelivery,
            out _);
    }

    private bool TryExecuteQueenSlimeLoot(in NpcSnapshot npc)
    {
        if (!interactions.TryCopyInteractingSlots(npc.Handle, interactionSlots, out int interactionCount) ||
            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.QueenSlime, out VanillaNpcDefinition definition))
        {
            return false;
        }

        int activeCount = 0;
        for (int index = 0; index < interactionCount; index++)
        {
            PlayerSlotId slot = interactionSlots[index];
            if (!TryGetActiveLootPlayer(slot, out PlayerStateSnapshot player))
                continue;

            activeQueenSlimeLootPlayers[activeCount++] = new VanillaQueenSlimeLootPlayer(
                slot,
                player.PositionX + VanillaPlayerWidth * 0.5f,
                player.PositionY + VanillaPlayerHeight * 0.5f);
        }

        var origin = ResolveNpcLootOrigin(in npc, in definition);
        var context = new VanillaQueenSlimeLootContext(expertMode, masterMode);
        return VanillaQueenSlimeLootEvaluator.TryExecute(
            in context,
            in origin,
            activeQueenSlimeLootPlayers.AsSpan(0, activeCount),
            random,
            lootDelivery,
            out _);
    }

    private bool TryExecuteEyeOfCthulhuLoot(in NpcSnapshot npc)
    {
        if (!interactions.TryCopyInteractingSlots(npc.Handle, interactionSlots, out int interactionCount) ||
            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.EyeOfCthulhu, out VanillaNpcDefinition definition))
        {
            return false;
        }

        int activeCount = 0;
        for (int index = 0; index < interactionCount; index++)
        {
            PlayerSlotId slot = interactionSlots[index];
            if (!TryGetActiveLootPlayer(slot, out PlayerStateSnapshot player))
                continue;

            activeEyeOfCthulhuLootPlayers[activeCount++] = new VanillaEyeOfCthulhuLootPlayer(
                slot,
                player.PositionX + VanillaPlayerWidth * 0.5f,
                player.PositionY + VanillaPlayerHeight * 0.5f);
        }

        var origin = ResolveNpcLootOrigin(in npc, in definition);
        var context = new VanillaEyeOfCthulhuLootContext(expertMode, masterMode, crimsonWorld);
        return VanillaEyeOfCthulhuLootEvaluator.TryExecute(
            in context,
            in origin,
            activeEyeOfCthulhuLootPlayers.AsSpan(0, activeCount),
            random,
            lootDelivery,
            out _);
    }

    private bool TryExecuteMechanicalBossLoot(in NpcSnapshot npc)
    {
        if (!interactions.TryCopyInteractingSlots(npc.Handle, interactionSlots, out int interactionCount) ||
            !VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, out VanillaNpcDefinition definition))
        {
            return false;
        }

        int activeCount = 0;
        for (int index = 0; index < interactionCount; index++)
        {
            PlayerSlotId slot = interactionSlots[index];
            if (!TryGetActiveLootPlayer(slot, out PlayerStateSnapshot player))
                continue;

            activeMechanicalBossLootPlayers[activeCount++] = new VanillaMechanicalBossLootPlayer(
                slot,
                player.PositionX + VanillaPlayerWidth * 0.5f,
                player.PositionY + VanillaPlayerHeight * 0.5f);
        }

        var origin = ResolveNpcLootOrigin(in npc, in definition);
        bool otherTwinActive = false;
        bool mechdusaKill = IsMechdusaKill(in npc);
        if (VanillaMechanicalBossLootEvaluator.IsTwin(npc.TypeIdentity))
        {
            NpcTypeId otherType = npc.TypeIdentity == VanillaNpcIds.Retinazer ?
                VanillaNpcIds.Spazmatism : VanillaNpcIds.Retinazer;
            int count = DeathNpcs.CopyActive(npcFamilyBuffer);
            for (int index = 0; index < count; index++)
                otherTwinActive |= npcFamilyBuffer[index].TypeIdentity == otherType;
        }
        var context = new VanillaMechanicalBossLootContext(
            npc.TypeIdentity, expertMode, masterMode, otherTwinActive, mechdusaKill);
        return VanillaMechanicalBossLootEvaluator.TryExecute(
            in context,
            in origin,
            activeMechanicalBossLootPlayers.AsSpan(0, activeCount),
            random,
            lootDelivery,
            out _);
    }

    private bool IsMechdusaKill(in NpcSnapshot dying)
    {
        if (!zenithWorld)
            return false;

        int count = DeathNpcs.CopyActive(npcFamilyBuffer);
        for (int index = 0; index < count; index++)
        {
            NpcTypeId type = npcFamilyBuffer[index].TypeIdentity;
            if (VanillaMechanicalBossLootEvaluator.IsRoot(type) && type != dying.TypeIdentity)
                return false;
        }
        return true;
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
            if (!TryGetActiveLootPlayer(slot, out PlayerStateSnapshot player))
                continue;
            activeWallOfFleshLootPlayers[activeCount++] = new VanillaWallOfFleshLootPlayer(
                slot,
                player.PositionX + VanillaPlayerWidth * 0.5f,
                player.PositionY + VanillaPlayerHeight * 0.5f);
        }

        var origin = ResolveNpcLootOrigin(in npc, in definition);
        var context = new VanillaWallOfFleshLootContext(expertMode, masterMode);
        return VanillaWallOfFleshLootEvaluator.TryExecute(
            in context,
            in origin,
            activeWallOfFleshLootPlayers.AsSpan(0, activeCount),
            random,
            lootDelivery,
            out _);
    }

    private bool TryExecuteKingSlimeDifficultyLoot(in NpcSnapshot npc)
    {
        if (worldItemReplication is null ||
            !interactions.TryCopyInteractingSlots(npc.Handle, interactionSlots, out int interactionCount) ||
            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.KingSlime, out VanillaNpcDefinition definition))
        {
            return false;
        }

        int activeCount = 0;
        for (int index = 0; index < interactionCount; index++)
        {
            PlayerSlotId slot = interactionSlots[index];
            if (!TryGetActiveLootPlayer(slot, out PlayerStateSnapshot player))
                continue;
            activeLootPlayers[activeCount++] = new VanillaKingSlimeLootPlayer(
                slot,
                player.PositionX + VanillaPlayerWidth * 0.5f,
                player.PositionY + VanillaPlayerHeight * 0.5f);
        }

        var origin = ResolveNpcLootOrigin(in npc, in definition);
        var context = new VanillaKingSlimeDifficultyLootContext(expertMode, masterMode);
        return VanillaKingSlimeDifficultyLootEvaluator.TryExecute(
            in context,
            in origin,
            activeLootPlayers.AsSpan(0, activeCount),
            random,
            lootDelivery,
            out _);
    }

    private sealed class SystemNpcCombatRandom : INpcLootRollSource, IKingSlimeDeathRandom, IVanillaNpcRandom, TerraRuntime.Gameplay.Npcs.Loot.INpcMoneyRandom1458
    {
        public VanillaUnifiedRandom1458 SourceRandom { get; private set; }
        public float Luck { get; set; }
        public SystemNpcCombatRandom(VanillaUnifiedRandom1458? sourceRandom = null) =>
            SourceRandom = sourceRandom ?? new VanillaUnifiedRandom1458(Random.Shared.Next());
        public void UseSource(VanillaUnifiedRandom1458 source) => SourceRandom = source;

        public int RollLuck(int chanceDenominator)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(chanceDenominator, 1);
            if (Luck > 0f && NextFloat() < Luck)
                return SourceRandom.Next(SourceRandom.Next(chanceDenominator / 2, chanceDenominator));
            if (Luck < 0f && NextFloat() < -Luck)
                return SourceRandom.Next(SourceRandom.Next(chanceDenominator, checked(chanceDenominator * 2)));
            return SourceRandom.Next(chanceDenominator);
        }
        public int NextInt32(int inclusiveMin, int exclusiveMax) => SourceRandom.Next(inclusiveMin, exclusiveMax);
        public float NextFloat() => (float)SourceRandom.NextDouble();
        public float NextFloatDirection() => NextFloat() * 2f - 1f;
    }
}
