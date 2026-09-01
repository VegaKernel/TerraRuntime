from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    p = Path(path)
    text = p.read_text(encoding='utf-8')
    count = text.count(old)
    if count != 1:
        raise SystemExit(f'{path}: expected exactly one anchor, got {count}')
    p.write_text(text.replace(old, new, 1), encoding='utf-8')


def write_new(path: str, content: str) -> None:
    p = Path(path)
    if p.exists():
        raise SystemExit(f'{path}: file already exists')
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(content, encoding='utf-8')


write_new('src/TerraRuntime.Contracts/Gameplay/VanillaEaterOfWorldsItemIds.cs', r'''namespace TerraRuntime.Contracts.Gameplay;

/// <summary>TerrariaServer 1.4.5.8 item identities consumed by the Eater of Worlds death/loot slice.</summary>
public static class VanillaEaterOfWorldsItemIds
{
    public static readonly ItemTypeId DemoniteOre = new(56);
    public static readonly ItemTypeId ShadowScale = new(86);
    public static readonly ItemTypeId EatersBone = new(994);
    public static readonly ItemTypeId EaterOfWorldsTrophy = new(1361);
    public static readonly ItemTypeId EaterMask = new(2111);
    public static readonly ItemTypeId EaterOfWorldsBossBag = new(3320);
    public static readonly ItemTypeId EaterOfWorldsPetItem = new(4799);
    public static readonly ItemTypeId EaterOfWorldsMasterTrophy = new(4925);
}
''')

write_new('src/TerraRuntime.Core/Npcs/VanillaEaterOfWorldsLoot.cs', r'''using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

public readonly record struct VanillaEaterOfWorldsLootContext(
    bool IsExpertMode,
    bool IsMasterMode,
    bool IsBoss)
{
    public bool IsValid => !IsMasterMode || IsExpertMode;
}

public readonly record struct VanillaEaterOfWorldsLootPlayer(
    PlayerSlotId Slot,
    float CenterX,
    float CenterY)
{
    public bool IsValid =>
        Slot.Value < RuntimeNpcPlayerInteractionLedger.VanillaInteractablePlayerSlots &&
        float.IsFinite(CenterX) &&
        float.IsFinite(CenterY);

    public NpcLootWorldItemOrigin Origin => new(CenterX, CenterY);
}

/// <summary>
/// Delivery boundary for Eater of Worlds segment drops and last-segment boss wrappers. CanDeliver methods are
/// side-effect free; successful evaluation materializes each item inline so Item.NewItem RNG remains interleaved with
/// subsequent loot-rule RNG exactly like the TerrariaServer rule chain.
/// </summary>
public interface IEaterOfWorldsLootDeliverySink
{
    bool CanDeliverInstanced(ItemTypeId itemType);

    bool CanDeliverWorldItem(ItemTypeId itemType);

    bool TryDeliverInstanced(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        ReadOnlySpan<VanillaEaterOfWorldsLootPlayer> recipients,
        int slotLeaseTicks,
        INpcLootRollSource random);

    bool TryDeliverWorldItem(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        INpcLootRollSource random);
}

public readonly record struct EaterOfWorldsLootExecutionResult(
    int SegmentWorldItemCount,
    int BossWorldItemCount,
    int InstancedItemCount,
    int InstancedRecipientCount,
    int MasterPetDropCount)
{
    public int TotalLogicalItemCount => checked(SegmentWorldItemCount + BossWorldItemCount + InstancedItemCount);

    public bool IsValid =>
        SegmentWorldItemCount >= 0 &&
        BossWorldItemCount >= 0 &&
        InstancedItemCount >= 0 &&
        InstancedRecipientCount >= 0 &&
        MasterPetDropCount >= 0 &&
        MasterPetDropCount <= BossWorldItemCount;
}

/// <summary>
/// Source-order implementation of RegisterBoss_EOW from TerrariaServer 1.4.5.8. Every segment owns the two small
/// difficulty-scaled material rules. DropEoWLoot promotes only the final active 13/14/15 segment to boss, which then
/// unlocks the Expert bag, Master relic/pet path or normal-only finishing drops, followed by the separately registered
/// boss trophy rule.
/// </summary>
public static class VanillaEaterOfWorldsLootEvaluator
{
    public const int InstancedItemSlotLeaseTicks = 54_000;
    public const int MasterPetChanceDenominator = 4;

    public static bool TryExecute(
        in VanillaEaterOfWorldsLootContext context,
        in NpcLootWorldItemOrigin npcOrigin,
        ReadOnlySpan<VanillaEaterOfWorldsLootPlayer> activeInteractingPlayers,
        INpcLootRollSource rolls,
        IEaterOfWorldsLootDeliverySink sink,
        out EaterOfWorldsLootExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(rolls);
        ArgumentNullException.ThrowIfNull(sink);
        result = default;

        if (!context.IsValid || !npcOrigin.IsValid || !ArePlayersSourceOrdered(activeInteractingPlayers) ||
            !sink.CanDeliverWorldItem(VanillaEaterOfWorldsItemIds.ShadowScale) ||
            !sink.CanDeliverWorldItem(VanillaEaterOfWorldsItemIds.DemoniteOre))
        {
            return false;
        }

        if (context.IsBoss)
        {
            if (context.IsExpertMode && !sink.CanDeliverInstanced(VanillaEaterOfWorldsItemIds.EaterOfWorldsBossBag))
                return false;
            if (context.IsMasterMode &&
                (!sink.CanDeliverWorldItem(VanillaEaterOfWorldsItemIds.EaterOfWorldsMasterTrophy) ||
                 !sink.CanDeliverWorldItem(VanillaEaterOfWorldsItemIds.EaterOfWorldsPetItem)))
            {
                return false;
            }
            if (!context.IsExpertMode &&
                (!sink.CanDeliverWorldItem(VanillaEaterOfWorldsItemIds.EatersBone) ||
                 !sink.CanDeliverWorldItem(VanillaEaterOfWorldsItemIds.EaterMask)))
            {
                return false;
            }
            if (!sink.CanDeliverWorldItem(VanillaEaterOfWorldsItemIds.EaterOfWorldsTrophy))
                return false;
        }

        int segmentItems = 0;
        int bossItems = 0;
        int petDrops = 0;

        int scaleChance = context.IsMasterMode ? 10 : context.IsExpertMode ? 5 : 2;
        if (!TryRollWorldItem(
                VanillaEaterOfWorldsItemIds.ShadowScale,
                scaleChance,
                1,
                2,
                in npcOrigin,
                rolls,
                sink,
                ref segmentItems))
        {
            return false;
        }

        int oreChance = context.IsMasterMode ? 3 : 2;
        int oreMin = context.IsMasterMode || context.IsExpertMode ? 1 : 2;
        int oreMax = context.IsMasterMode ? 2 : context.IsExpertMode ? 3 : 5;
        if (!TryRollWorldItem(
                VanillaEaterOfWorldsItemIds.DemoniteOre,
                oreChance,
                oreMin,
                oreMax,
                in npcOrigin,
                rolls,
                sink,
                ref segmentItems))
        {
            return false;
        }

        if (!context.IsBoss)
        {
            result = new EaterOfWorldsLootExecutionResult(segmentItems, 0, 0, 0, 0);
            return result.IsValid;
        }

        int instancedItems = 0;
        int instancedRecipients = 0;
        if (context.IsExpertMode)
        {
            // BossBagByCondition -> DropLocalPerClientAndResetsNPCMoneyTo0: raw chance RNG then shared stack RNG.
            rolls.NextInt32(0, 1);
            short bagStack = checked((short)rolls.NextInt32(1, 2));
            var bag = new NpcLootDrop(VanillaEaterOfWorldsItemIds.EaterOfWorldsBossBag, bagStack);
            if (!sink.TryDeliverInstanced(
                    in npcOrigin,
                    in bag,
                    activeInteractingPlayers,
                    InstancedItemSlotLeaseTicks,
                    rolls))
            {
                throw new InvalidOperationException(
                    "Eater of Worlds loot sink advertised instanced bag support but failed after preflight.");
            }
            instancedItems = 1;
            instancedRecipients = activeInteractingPlayers.Length;
        }

        if (context.IsMasterMode)
        {
            if (!TryRollWorldItem(
                    VanillaEaterOfWorldsItemIds.EaterOfWorldsMasterTrophy,
                    1,
                    1,
                    1,
                    in npcOrigin,
                    rolls,
                    sink,
                    ref bossItems))
            {
                return false;
            }

            // MasterModeDropOnAllPlayers chooses one stack before its ascending active-player loop and uses raw RNG.
            short petStack = checked((short)rolls.NextInt32(1, 2));
            for (int index = 0; index < activeInteractingPlayers.Length; index++)
            {
                if (rolls.NextInt32(0, MasterPetChanceDenominator) != 0)
                    continue;
                VanillaEaterOfWorldsLootPlayer player = activeInteractingPlayers[index];
                NpcLootWorldItemOrigin playerOrigin = player.Origin;
                var pet = new NpcLootDrop(VanillaEaterOfWorldsItemIds.EaterOfWorldsPetItem, petStack);
                if (!sink.TryDeliverWorldItem(in playerOrigin, in pet, rolls))
                {
                    throw new InvalidOperationException(
                        "Eater of Worlds loot sink advertised Master pet support but failed after preflight.");
                }
                bossItems++;
                petDrops++;
            }
        }
        else if (!context.IsExpertMode)
        {
            if (!TryRollWorldItem(
                    VanillaEaterOfWorldsItemIds.DemoniteOre,
                    1,
                    20,
                    60,
                    in npcOrigin,
                    rolls,
                    sink,
                    ref bossItems) ||
                !TryRollWorldItem(
                    VanillaEaterOfWorldsItemIds.EatersBone,
                    20,
                    1,
                    1,
                    in npcOrigin,
                    rolls,
                    sink,
                    ref bossItems) ||
                !TryRollWorldItem(
                    VanillaEaterOfWorldsItemIds.EaterMask,
                    7,
                    1,
                    1,
                    in npcOrigin,
                    rolls,
                    sink,
                    ref bossItems))
            {
                return false;
            }
        }

        // Trophy rules for 13/14/15 are registered later and retain their source position after RegisterBoss_EOW.
        if (!TryRollWorldItem(
                VanillaEaterOfWorldsItemIds.EaterOfWorldsTrophy,
                10,
                1,
                1,
                in npcOrigin,
                rolls,
                sink,
                ref bossItems))
        {
            return false;
        }

        result = new EaterOfWorldsLootExecutionResult(
            segmentItems,
            bossItems,
            instancedItems,
            instancedRecipients,
            petDrops);
        return result.IsValid;
    }

    private static bool TryRollWorldItem(
        ItemTypeId itemType,
        int chanceDenominator,
        int minimumStack,
        int maximumStack,
        in NpcLootWorldItemOrigin origin,
        INpcLootRollSource rolls,
        IEaterOfWorldsLootDeliverySink sink,
        ref int delivered)
    {
        if (rolls.RollLuck(chanceDenominator) != 0)
            return true;

        short stack = checked((short)rolls.NextInt32(minimumStack, checked(maximumStack + 1)));
        var drop = new NpcLootDrop(itemType, stack);
        if (!sink.TryDeliverWorldItem(in origin, in drop, rolls))
        {
            throw new InvalidOperationException(
                $"Eater of Worlds loot sink advertised world-item support for {itemType.Value} but failed after preflight.");
        }
        delivered++;
        return true;
    }

    private static bool ArePlayersSourceOrdered(ReadOnlySpan<VanillaEaterOfWorldsLootPlayer> players)
    {
        int previousSlot = -1;
        for (int index = 0; index < players.Length; index++)
        {
            VanillaEaterOfWorldsLootPlayer player = players[index];
            if (!player.IsValid || player.Slot.Value <= previousSlot)
                return false;
            previousSlot = player.Slot.Value;
        }
        return true;
    }
}

/// <summary>
/// Source-shaped shared lifecycle helpers for NPC types 13/14/15. NPC.PlayerInteraction propagates a hit to every
/// active Eater segment, and DropEoWLoot promotes the dying segment to boss only when no other active Eater segment
/// remains. Both scans use the store's stable slot order and preserve generation-safe ledger keys.
/// </summary>
public static class VanillaEaterOfWorldsLifecycle
{
    public static bool IsSegment(NpcTypeId type) =>
        type == VanillaNpcIds.EaterOfWorldsHead ||
        type == VanillaNpcIds.EaterOfWorldsBody ||
        type == VanillaNpcIds.EaterOfWorldsTail;

    public static int MarkPlayerInteractionAcrossActiveSegments(
        RuntimeNpcStore store,
        RuntimeNpcPlayerInteractionLedger interactions,
        PlayerHandle player,
        Span<NpcSnapshot> activeBuffer)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(interactions);
        int count = store.CopyActive(activeBuffer);
        int marked = 0;
        for (int index = 0; index < count; index++)
        {
            NpcSnapshot candidate = activeBuffer[index];
            if (IsSegment(candidate.TypeIdentity) && interactions.TryMark(candidate.Handle, player))
                marked++;
        }
        return marked;
    }

    public static bool IsLastActiveSegment(
        RuntimeNpcStore store,
        in NpcSnapshot dying,
        Span<NpcSnapshot> activeBuffer)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (!IsSegment(dying.TypeIdentity))
            return false;

        int count = store.CopyActive(activeBuffer);
        for (int index = 0; index < count; index++)
        {
            NpcSnapshot candidate = activeBuffer[index];
            if (candidate.Handle != dying.Handle && IsSegment(candidate.TypeIdentity))
                return false;
        }
        return true;
    }
}
''')

write_new('src/TerraRuntime/RuntimeEaterOfWorldsLootDeliverySink.cs', r'''using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime;

/// <summary>
/// Production Eater of Worlds loot delivery. Ordinary drops use the shared authoritative world-item store. Expert
/// Boss Bags use the same packet-90 addressed delivery and 54000-tick unpublished slot lease contract as King Slime,
/// while Master per-player items remain ordinary world items placed at each qualifying player's center.
/// </summary>
internal sealed class RuntimeEaterOfWorldsLootDeliverySink : IEaterOfWorldsLootDeliverySink
{
    private readonly RuntimeWorldItemStore worldItems;
    private readonly RuntimeWorldItemInstancedLeaseStore? leases;
    private readonly RuntimeWorldItemReplicationRegistry? replication;
    private readonly INpcLootWorldItemMaterializer materializer;

    public RuntimeEaterOfWorldsLootDeliverySink(
        RuntimeWorldItemStore worldItems,
        RuntimeWorldItemInstancedLeaseStore? leases,
        RuntimeWorldItemReplicationRegistry? replication,
        INpcLootWorldItemMaterializer? materializer = null)
    {
        this.worldItems = worldItems ?? throw new ArgumentNullException(nameof(worldItems));
        this.leases = leases;
        this.replication = replication;
        this.materializer = materializer ?? VanillaNpcLootWorldItemMaterializer.Instance;
    }

    public bool CanDeliverInstanced(ItemTypeId itemType) =>
        leases is not null && replication is not null && materializer.CanMaterialize(itemType);

    public bool CanDeliverWorldItem(ItemTypeId itemType) => materializer.CanMaterialize(itemType);

    public bool TryDeliverInstanced(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        ReadOnlySpan<VanillaEaterOfWorldsLootPlayer> recipients,
        int slotLeaseTicks,
        INpcLootRollSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (leases is null || replication is null ||
            !materializer.TryMaterialize(in origin, in drop, random, out WorldItemDropStateUpdate materialized) ||
            !leases.TryLease(in materialized, slotLeaseTicks, out WorldItemDropReservation reservation))
        {
            return false;
        }

        TerrariaWorldItemDropState wireState = RuntimeWorldItemReplicationRegistry.MapDrop(reservation.Slot, in materialized);
        if (TerrariaWorldItemFrameEncoder.TryEncodeInstancedDrop(in wireState, out ReadOnlyMemory<byte> frame) !=
            TerrariaWorldItemFrameEncodeResult.Encoded)
        {
            leases.TryCancel(in reservation);
            return false;
        }

        for (int index = 0; index < recipients.Length; index++)
        {
            if (replication.TrySendInstanced(recipients[index].Slot, frame))
                continue;
            return false;
        }
        return true;
    }

    public bool TryDeliverWorldItem(
        in NpcLootWorldItemOrigin origin,
        in NpcLootDrop drop,
        INpcLootRollSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return materializer.TryMaterialize(in origin, in drop, random, out WorldItemDropStateUpdate materialized) &&
               worldItems.TryAllocateDrop(in materialized, out _);
    }
}
''')

replace_once(
    'src/TerraRuntime.Core/Items/VanillaItemDefinitionCatalog.cs',
    '''    public static bool TryGet(ItemTypeId type, out VanillaItemDefinition definition)\n    {\n''',
    '''    private static readonly VanillaItemDefinition EaterDemoniteOreDefinition =\n        EaterWorldDrop(VanillaEaterOfWorldsItemIds.DemoniteOre, 12, 12);\n    private static readonly VanillaItemDefinition EaterShadowScaleDefinition =\n        EaterWorldDrop(VanillaEaterOfWorldsItemIds.ShadowScale, 14, 18);\n    private static readonly VanillaItemDefinition EatersBoneDefinition =\n        EaterWorldDrop(VanillaEaterOfWorldsItemIds.EatersBone, 16, 30);\n    private static readonly VanillaItemDefinition EaterTrophyDefinition =\n        EaterWorldDrop(VanillaEaterOfWorldsItemIds.EaterOfWorldsTrophy, 30, 30);\n    private static readonly VanillaItemDefinition EaterMaskDefinition =\n        EaterWorldDrop(VanillaEaterOfWorldsItemIds.EaterMask, 28, 20);\n    private static readonly VanillaItemDefinition EaterBossBagDefinition =\n        EaterWorldDrop(VanillaEaterOfWorldsItemIds.EaterOfWorldsBossBag, 24, 24);\n    private static readonly VanillaItemDefinition EaterPetItemDefinition =\n        EaterWorldDrop(VanillaEaterOfWorldsItemIds.EaterOfWorldsPetItem, 16, 30);\n    private static readonly VanillaItemDefinition EaterMasterTrophyDefinition =\n        EaterWorldDrop(VanillaEaterOfWorldsItemIds.EaterOfWorldsMasterTrophy, 14, 14);\n\n    private static VanillaItemDefinition EaterWorldDrop(ItemTypeId type, int width, int height) =>\n        new(\n            Type: type,\n            RuntimeDefaults: new VanillaItemRuntimeDefaults(width, height, CommonMaximumStack),\n            UseTiming: null,\n            Placement: null,\n            PickTool: null,\n            WorldDrop: new VanillaItemWorldDropDefinition(\n                width,\n                height,\n                NoGravity: false,\n                PrefixFamily: VanillaItemPrefixFamily.None));\n\n    public static bool TryGet(ItemTypeId type, out VanillaItemDefinition definition)\n    {\n''')

replace_once(
    'src/TerraRuntime.Core/Items/VanillaItemDefinitionCatalog.cs',
    '''        if (type == VanillaKingSlimeItemIds.KingSlimeMasterTrophy)\n        {\n            definition = KingSlimeMasterTrophyDefinition;\n            return true;\n        }\n\n        definition = default;\n''',
    '''        if (type == VanillaKingSlimeItemIds.KingSlimeMasterTrophy)\n        {\n            definition = KingSlimeMasterTrophyDefinition;\n            return true;\n        }\n\n        if (type == VanillaEaterOfWorldsItemIds.DemoniteOre)\n        {\n            definition = EaterDemoniteOreDefinition;\n            return true;\n        }\n        if (type == VanillaEaterOfWorldsItemIds.ShadowScale)\n        {\n            definition = EaterShadowScaleDefinition;\n            return true;\n        }\n        if (type == VanillaEaterOfWorldsItemIds.EatersBone)\n        {\n            definition = EatersBoneDefinition;\n            return true;\n        }\n        if (type == VanillaEaterOfWorldsItemIds.EaterOfWorldsTrophy)\n        {\n            definition = EaterTrophyDefinition;\n            return true;\n        }\n        if (type == VanillaEaterOfWorldsItemIds.EaterMask)\n        {\n            definition = EaterMaskDefinition;\n            return true;\n        }\n        if (type == VanillaEaterOfWorldsItemIds.EaterOfWorldsBossBag)\n        {\n            definition = EaterBossBagDefinition;\n            return true;\n        }\n        if (type == VanillaEaterOfWorldsItemIds.EaterOfWorldsPetItem)\n        {\n            definition = EaterPetItemDefinition;\n            return true;\n        }\n        if (type == VanillaEaterOfWorldsItemIds.EaterOfWorldsMasterTrophy)\n        {\n            definition = EaterMasterTrophyDefinition;\n            return true;\n        }\n\n        definition = default;\n''')

replace_once(
    'src/TerraRuntime/RuntimeNpcNetworkCombatPipeline.cs',
    '''/// TerrariaServer ordering PlayerInteraction -> StrikeNPC -> imported loot -> King Slime death effects -> despawn ->\n/// packet 28 -> packet 23, and never lets a socket thread touch runtime entity state.\n''',
    '''/// TerrariaServer ordering PlayerInteraction -> StrikeNPC -> imported loot -> boss death effects -> despawn -> packet 28\n/// -> packet 23, including shared Eater-of-Worlds interaction credit and last-segment boss promotion. Socket threads never\n/// mutate runtime entity state directly.\n''')
replace_once(
    'src/TerraRuntime/RuntimeNpcNetworkCombatPipeline.cs',
    '''    private readonly RuntimeKingSlimeDifficultyLootDeliverySink? difficultyLoot;\n''',
    '''    private readonly RuntimeKingSlimeDifficultyLootDeliverySink? difficultyLoot;\n    private readonly RuntimeEaterOfWorldsLootDeliverySink eaterLoot;\n''')
replace_once(
    'src/TerraRuntime/RuntimeNpcNetworkCombatPipeline.cs',
    '''    private readonly VanillaKingSlimeLootPlayer[] activeLootPlayers =\n        new VanillaKingSlimeLootPlayer[RuntimeNpcPlayerInteractionLedger.VanillaInteractablePlayerSlots];\n''',
    '''    private readonly VanillaKingSlimeLootPlayer[] activeLootPlayers =\n        new VanillaKingSlimeLootPlayer[RuntimeNpcPlayerInteractionLedger.VanillaInteractablePlayerSlots];\n    private readonly VanillaEaterOfWorldsLootPlayer[] activeEaterLootPlayers =\n        new VanillaEaterOfWorldsLootPlayer[RuntimeNpcPlayerInteractionLedger.VanillaInteractablePlayerSlots];\n    private readonly NpcSnapshot[] npcFamilyBuffer;\n''')
replace_once(
    'src/TerraRuntime/RuntimeNpcNetworkCombatPipeline.cs',
    '''        interactions = new RuntimeNpcPlayerInteractionLedger(npcs);\n        damage = new RuntimeNpcDamageExecutor(npcs, expertMode, interactions);\n        if (worldItemReplication is not null)\n''',
    '''        interactions = new RuntimeNpcPlayerInteractionLedger(npcs);\n        damage = new RuntimeNpcDamageExecutor(npcs, expertMode, interactions);\n        npcFamilyBuffer = new NpcSnapshot[npcs.Capacity];\n        eaterLoot = new RuntimeEaterOfWorldsLootDeliverySink(\n            worldItems,\n            instancedLeases,\n            worldItemReplication);\n        if (worldItemReplication is not null)\n''')
replace_once(
    'src/TerraRuntime/RuntimeNpcNetworkCombatPipeline.cs',
    '''        interactions.TryMark(current.Handle, connection.Player);\n\n        var request = new NpcDamageRequest(\n''',
    '''        if (VanillaEaterOfWorldsLifecycle.IsSegment(current.TypeIdentity))\n        {\n            VanillaEaterOfWorldsLifecycle.MarkPlayerInteractionAcrossActiveSegments(\n                npcs,\n                interactions,\n                connection.Player,\n                npcFamilyBuffer);\n        }\n        else\n        {\n            interactions.TryMark(current.Handle, connection.Player);\n        }\n\n        var request = new NpcDamageRequest(\n''')
replace_once(
    'src/TerraRuntime/RuntimeNpcNetworkCombatPipeline.cs',
    '''            if (!TryExecuteImportedLoot(in dead))\n                throw new InvalidOperationException("Imported NPC loot could not be finalized after a lethal packet-28 commit.");\n\n            if (dead.TypeIdentity == VanillaNpcIds.KingSlime)\n                ApplyKingSlimeDeathEffects(in dead);\n''',
    '''            bool eaterBoss =\n                VanillaEaterOfWorldsLifecycle.IsSegment(dead.TypeIdentity) &&\n                VanillaEaterOfWorldsLifecycle.IsLastActiveSegment(npcs, in dead, npcFamilyBuffer);\n\n            if (!TryExecuteImportedLoot(in dead, eaterBoss))\n                throw new InvalidOperationException("Imported NPC loot could not be finalized after a lethal packet-28 commit.");\n\n            if (dead.TypeIdentity == VanillaNpcIds.KingSlime)\n                ApplyKingSlimeDeathEffects(in dead);\n            else if (eaterBoss)\n                ApplyEaterOfWorldsDeathEffects();\n''')
replace_once(
    'src/TerraRuntime/RuntimeNpcNetworkCombatPipeline.cs',
    '''    private bool TryExecuteImportedLoot(in NpcSnapshot npc)\n    {\n        if (npc.TypeIdentity == VanillaNpcIds.KingSlime && expertMode)\n''',
    '''    private bool TryExecuteImportedLoot(in NpcSnapshot npc, bool eaterBoss)\n    {\n        if (VanillaEaterOfWorldsLifecycle.IsSegment(npc.TypeIdentity))\n            return TryExecuteEaterOfWorldsLoot(in npc, eaterBoss);\n\n        if (npc.TypeIdentity == VanillaNpcIds.KingSlime && expertMode)\n''')
replace_once(
    'src/TerraRuntime/RuntimeNpcNetworkCombatPipeline.cs',
    '''    private bool TryExecuteKingSlimeDifficultyLoot(in NpcSnapshot npc)\n    {\n''',
    '''    private bool TryExecuteEaterOfWorldsLoot(in NpcSnapshot npc, bool isBoss)\n    {\n        if (!interactions.TryCopyInteractingSlots(npc.Handle, interactionSlots, out int interactionCount) ||\n            !VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out VanillaNpcDefinition definition))\n        {\n            return false;\n        }\n\n        int activeCount = 0;\n        for (int index = 0; index < interactionCount; index++)\n        {\n            PlayerSlotId slot = interactionSlots[index];\n            if (!players.TryGetPlayer(slot, out PlayerStateSnapshot player))\n                continue;\n            activeEaterLootPlayers[activeCount++] = new VanillaEaterOfWorldsLootPlayer(\n                slot,\n                player.PositionX + VanillaPlayerWidth * 0.5f,\n                player.PositionY + VanillaPlayerHeight * 0.5f);\n        }\n\n        var origin = new NpcLootWorldItemOrigin(\n            (int)npc.PositionX + definition.Width * 0.5f,\n            (int)npc.PositionY + definition.Height * 0.5f);\n        var context = new VanillaEaterOfWorldsLootContext(expertMode, masterMode, isBoss);\n        return VanillaEaterOfWorldsLootEvaluator.TryExecute(\n            in context,\n            in origin,\n            activeEaterLootPlayers.AsSpan(0, activeCount),\n            random,\n            eaterLoot,\n            out _);\n    }\n\n    private bool TryExecuteKingSlimeDifficultyLoot(in NpcSnapshot npc)\n    {\n''')
replace_once(
    'src/TerraRuntime/RuntimeNpcNetworkCombatPipeline.cs',
    '''    private void ApplyKingSlimeDeathEffects(in NpcSnapshot kingSlime)\n    {\n''',
    '''    private void ApplyEaterOfWorldsDeathEffects()\n    {\n        if (worldTiles is null)\n            return;\n        RuntimeWorldProgressionRegistry.GetOrCreate(worldTiles)\n            .MarkCompleted(VanillaWorldProgressionId.EvilBoss);\n    }\n\n    private void ApplyKingSlimeDeathEffects(in NpcSnapshot kingSlime)\n    {\n''')

replace_once(
    'src/TerraRuntime.World/WorldFileProgressionHeaderPatcher.cs',
    '''    private const ulong SupportedMutationMask = 1UL << (int)VanillaWorldProgressionId.KingSlime;\n''',
    '''    private const ulong SupportedMutationMask =\n        (1UL << (int)VanillaWorldProgressionId.KingSlime) |\n        (1UL << (int)VanillaWorldProgressionId.EvilBoss);\n''')
replace_once(
    'src/TerraRuntime.World/WorldFileProgressionHeaderPatcher.cs',
    '''        // crimson; downedBoss1/2/3; Queen Bee; mech 1/2/3/any; Plantera; Golem.\n        if (!reader.TrySkipBools(11))\n            return WorldFileProgressionHeaderPatchResult.InvalidHeader;\n\n        int downedSlimeKingOffset = reader.Offset;\n''',
    '''        // crimson; downedBoss1; downedBoss2; then boss3, Queen Bee, mech 1/2/3/any, Plantera and Golem.\n        if (!reader.TryReadBool(out _) || !reader.TryReadBool(out _))\n            return WorldFileProgressionHeaderPatchResult.InvalidHeader;\n        int downedBoss2Offset = reader.Offset;\n        if (!reader.TryReadBool(out bool persistedDownedBoss2) || !reader.TrySkipBools(8))\n            return WorldFileProgressionHeaderPatchResult.InvalidHeader;\n\n        int downedSlimeKingOffset = reader.Offset;\n''')
replace_once(
    'src/TerraRuntime.World/WorldFileProgressionHeaderPatcher.cs',
    '''        patchedHeader = sourceHeader.ToArray();\n        if (mutations.IsCompleted(VanillaWorldProgressionId.KingSlime) && !persistedDownedSlimeKing)\n            patchedHeader[downedSlimeKingOffset] = 1;\n''',
    '''        patchedHeader = sourceHeader.ToArray();\n        if (mutations.IsCompleted(VanillaWorldProgressionId.EvilBoss) && !persistedDownedBoss2)\n            patchedHeader[downedBoss2Offset] = 1;\n        if (mutations.IsCompleted(VanillaWorldProgressionId.KingSlime) && !persistedDownedSlimeKing)\n            patchedHeader[downedSlimeKingOffset] = 1;\n''')

write_new('tests/TerraRuntime.Tests/VanillaEaterOfWorldsLootTests.cs', r'''using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaEaterOfWorldsLootTests
{
    [Fact]
    public void Classic_non_boss_segment_only_runs_small_material_rules_in_source_order()
    {
        var rolls = new SequenceRolls([0, 0], [2, 4]);
        var sink = new RecordingSink();
        var context = new VanillaEaterOfWorldsLootContext(false, false, false);
        var origin = new NpcLootWorldItemOrigin(100f, 200f);

        Assert.True(VanillaEaterOfWorldsLootEvaluator.TryExecute(
            in context, in origin, [], rolls, sink, out EaterOfWorldsLootExecutionResult result));

        Assert.Equal(2, result.SegmentWorldItemCount);
        Assert.Equal(0, result.BossWorldItemCount);
        Assert.Equal(
            ["world:86:2:100:200", "world:56:4:100:200"],
            sink.Events);
        rolls.AssertExhausted();
    }

    [Fact]
    public void Classic_last_segment_appends_normal_boss_rules_then_trophy()
    {
        var rolls = new SequenceRolls(
            luck: [0, 0, 0, 0, 0, 0],
            raw: [1, 3, 40, 1, 1, 1]);
        var sink = new RecordingSink();
        var context = new VanillaEaterOfWorldsLootContext(false, false, true);
        var origin = new NpcLootWorldItemOrigin(10f, 20f);

        Assert.True(VanillaEaterOfWorldsLootEvaluator.TryExecute(
            in context, in origin, [], rolls, sink, out EaterOfWorldsLootExecutionResult result));

        Assert.Equal(2, result.SegmentWorldItemCount);
        Assert.Equal(4, result.BossWorldItemCount);
        Assert.Equal(
            [
                "world:86:1:10:20",
                "world:56:3:10:20",
                "world:56:40:10:20",
                "world:994:1:10:20",
                "world:2111:1:10:20",
                "world:1361:1:10:20"
            ],
            sink.Events);
        rolls.AssertExhausted();
    }

    [Fact]
    public void Master_last_segment_runs_small_rules_bag_relic_per_player_pet_then_trophy()
    {
        VanillaEaterOfWorldsLootPlayer[] players =
        [
            new(new PlayerSlotId(2), 20f, 30f),
            new(new PlayerSlotId(7), 70f, 80f)
        ];
        var rolls = new SequenceRolls(
            luck: [0, 0, 0, 0],
            raw: [2, 1, 0, 1, 1, 1, 0, 3, 1]);
        var sink = new RecordingSink();
        var context = new VanillaEaterOfWorldsLootContext(true, true, true);
        var origin = new NpcLootWorldItemOrigin(100f, 200f);

        Assert.True(VanillaEaterOfWorldsLootEvaluator.TryExecute(
            in context, in origin, players, rolls, sink, out EaterOfWorldsLootExecutionResult result));

        Assert.Equal(2, result.SegmentWorldItemCount);
        Assert.Equal(3, result.BossWorldItemCount);
        Assert.Equal(1, result.InstancedItemCount);
        Assert.Equal(2, result.InstancedRecipientCount);
        Assert.Equal(1, result.MasterPetDropCount);
        Assert.Equal(
            [
                "world:86:2:100:200",
                "world:56:1:100:200",
                "instanced:3320:1:2:54000",
                "world:4925:1:100:200",
                "world:4799:1:20:30",
                "world:1361:1:100:200"
            ],
            sink.Events);
        rolls.AssertExhausted();
    }

    private sealed class SequenceRolls(int[] luck, int[] raw) : INpcLootRollSource
    {
        private int luckIndex;
        private int rawIndex;

        public int RollLuck(int chanceDenominator)
        {
            if (luckIndex >= luck.Length)
                throw new Xunit.Sdk.XunitException("Luck RNG consumed more values than expected.");
            int value = luck[luckIndex++];
            Assert.InRange(value, 0, chanceDenominator - 1);
            return value;
        }

        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            if (rawIndex >= raw.Length)
                throw new Xunit.Sdk.XunitException("Raw RNG consumed more values than expected.");
            int value = raw[rawIndex++];
            Assert.InRange(value, inclusiveMin, exclusiveMax - 1);
            return value;
        }

        public void AssertExhausted()
        {
            Assert.Equal(luck.Length, luckIndex);
            Assert.Equal(raw.Length, rawIndex);
        }
    }

    private sealed class RecordingSink : IEaterOfWorldsLootDeliverySink
    {
        public List<string> Events { get; } = [];

        public bool CanDeliverInstanced(ItemTypeId itemType) => true;
        public bool CanDeliverWorldItem(ItemTypeId itemType) => true;

        public bool TryDeliverInstanced(
            in NpcLootWorldItemOrigin origin,
            in NpcLootDrop drop,
            ReadOnlySpan<VanillaEaterOfWorldsLootPlayer> recipients,
            int slotLeaseTicks,
            INpcLootRollSource random)
        {
            Events.Add($"instanced:{drop.ItemType.Value}:{drop.Stack}:{recipients.Length}:{slotLeaseTicks}");
            return true;
        }

        public bool TryDeliverWorldItem(
            in NpcLootWorldItemOrigin origin,
            in NpcLootDrop drop,
            INpcLootRollSource random)
        {
            Events.Add($"world:{drop.ItemType.Value}:{drop.Stack}:{origin.CenterX:0}:{origin.CenterY:0}");
            return true;
        }
    }
}
''')

write_new('tests/TerraRuntime.Tests/VanillaEaterOfWorldsLifecycleTests.cs', r'''using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaEaterOfWorldsLifecycleTests
{
    [Fact]
    public void Player_interaction_is_propagated_to_every_active_eater_segment()
    {
        var store = new RuntimeNpcStore(capacity: 8);
        var ledger = new RuntimeNpcPlayerInteractionLedger(store);
        NpcSnapshot head = Spawn(store, 0, VanillaNpcIds.EaterOfWorldsHead);
        NpcSnapshot body = Spawn(store, 1, VanillaNpcIds.EaterOfWorldsBody);
        NpcSnapshot tail = Spawn(store, 2, VanillaNpcIds.EaterOfWorldsTail);
        _ = Spawn(store, 3, VanillaNpcIds.BlueSlime);
        var player = new PlayerHandle(new PlayerSlotId(5), new PlayerSessionGeneration(1));
        var buffer = new NpcSnapshot[store.Capacity];

        Assert.Equal(3, VanillaEaterOfWorldsLifecycle.MarkPlayerInteractionAcrossActiveSegments(
            store, ledger, player, buffer));

        Assert.True(ledger.HasInteraction(head.Handle, player.Slot));
        Assert.True(ledger.HasInteraction(body.Handle, player.Slot));
        Assert.True(ledger.HasInteraction(tail.Handle, player.Slot));
    }

    [Fact]
    public void Only_final_active_segment_is_promoted_to_boss_for_death_loot()
    {
        var store = new RuntimeNpcStore(capacity: 8);
        NpcSnapshot head = Spawn(store, 0, VanillaNpcIds.EaterOfWorldsHead);
        NpcSnapshot body = Spawn(store, 1, VanillaNpcIds.EaterOfWorldsBody);
        NpcSnapshot tail = Spawn(store, 2, VanillaNpcIds.EaterOfWorldsTail);
        var buffer = new NpcSnapshot[store.Capacity];

        Assert.False(VanillaEaterOfWorldsLifecycle.IsLastActiveSegment(store, in body, buffer));
        Assert.True(store.TryDespawn(head.Handle));
        Assert.True(store.TryDespawn(tail.Handle));
        Assert.True(VanillaEaterOfWorldsLifecycle.IsLastActiveSegment(store, in body, buffer));
    }

    private static NpcSnapshot Spawn(RuntimeNpcStore store, ushort slot, NpcTypeId type)
    {
        var update = new NpcStateUpdate(
            Type: type.Value,
            NetId: checked((short)type.Value),
            PositionX: 32f + slot * 20f,
            PositionY: 64f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial with
            {
                Life = 150,
                LifeMax = 150
            });
        Assert.True(store.TrySpawn(slot, in update, out NpcSnapshot spawned));
        return spawned;
    }
}
''')

write_new('tests/TerraRuntime.Tests/EaterOfWorldsDeathProgressionTests.cs', r'''using System.Reflection;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class EaterOfWorldsDeathProgressionTests
{
    [Fact]
    public void Progression_header_patcher_sets_downed_boss2_and_keeps_world_loadable()
    {
        byte[] sourceFile = LoaderFixture<byte[]>("CreateCompleteCurrentWorld");
        WorldFileLoadLimits limits = LoaderFixture<WorldFileLoadLimits>("CreateLimits");
        Assert.True(WorldFileLoader.TryLoad(sourceFile, limits, out WorldFileData? sourceWorld).IsLoaded);
        WorldFileData source = Assert.IsType<WorldFileData>(sourceWorld);
        Assert.False(source.RuntimeMetadata.DownedBoss2);
        Assert.True(WorldFilePreservedSections.TryCapture(
            sourceFile,
            source.Envelope,
            out WorldFilePreservedSections? preserved));
        Assert.NotNull(preserved);

        var mutations = new RuntimeWorldProgressionMutations();
        Assert.True(mutations.MarkCompleted(VanillaWorldProgressionId.EvilBoss));
        RuntimeWorldProgressionMutationSnapshot snapshot = mutations.CaptureSnapshot();
        byte[] originalHeader = preserved!.Header.ToArray();

        Assert.Equal(
            WorldFileProgressionHeaderPatchResult.Patched,
            WorldFileProgressionHeaderPatcher.TryPatch(
                originalHeader,
                source.Header,
                in snapshot,
                out byte[] patchedHeader));
        Assert.Equal(1, originalHeader.Zip(patchedHeader).Count(pair => pair.First != pair.Second));

        byte[] patchedFile = sourceFile.ToArray();
        int headerStart = source.Envelope.SectionOffsets[0];
        patchedHeader.CopyTo(patchedFile.AsSpan(headerStart, patchedHeader.Length));

        WorldFileLoadDiagnostic diagnostic = WorldFileLoader.TryLoad(patchedFile, limits, out WorldFileData? loadedWorld);
        Assert.True(diagnostic.IsLoaded);
        WorldFileData loaded = Assert.IsType<WorldFileData>(loadedWorld);
        Assert.True(loaded.RuntimeMetadata.DownedBoss2);
        Assert.Equal(source.RuntimeMetadata.DownedBoss1, loaded.RuntimeMetadata.DownedBoss1);
        Assert.Equal(source.RuntimeMetadata.DownedBoss3, loaded.RuntimeMetadata.DownedBoss3);
        Assert.Equal(source.RuntimeMetadata.DownedSlimeKing, loaded.RuntimeMetadata.DownedSlimeKing);
        Assert.Equal(source.Chests, loaded.Chests);
        Assert.Equal(source.Signs, loaded.Signs);
    }

    private static T LoaderFixture<T>(string methodName)
    {
        MethodInfo? method = typeof(WorldFileLoaderTests).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<T>(method!.Invoke(null, null));
    }
}
''')

write_new('tests/TerraRuntime.Tests/VanillaEaterOfWorldsItemDefinitionTests.cs', r'''using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaEaterOfWorldsItemDefinitionTests
{
    public static TheoryData<ItemTypeId, int, int> Drops => new()
    {
        { VanillaEaterOfWorldsItemIds.DemoniteOre, 12, 12 },
        { VanillaEaterOfWorldsItemIds.ShadowScale, 14, 18 },
        { VanillaEaterOfWorldsItemIds.EatersBone, 16, 30 },
        { VanillaEaterOfWorldsItemIds.EaterOfWorldsTrophy, 30, 30 },
        { VanillaEaterOfWorldsItemIds.EaterMask, 28, 20 },
        { VanillaEaterOfWorldsItemIds.EaterOfWorldsBossBag, 24, 24 },
        { VanillaEaterOfWorldsItemIds.EaterOfWorldsPetItem, 16, 30 },
        { VanillaEaterOfWorldsItemIds.EaterOfWorldsMasterTrophy, 14, 14 }
    };

    [Theory]
    [MemberData(nameof(Drops))]
    public void Eater_drop_defaults_are_source_backed(ItemTypeId type, int width, int height)
    {
        Assert.True(VanillaItemDefinitionCatalog.TryGetWorldDrop(type, out VanillaItemWorldDropDefinition drop));
        Assert.Equal(width, drop.Width);
        Assert.Equal(height, drop.Height);
        Assert.False(drop.NoGravity);
        Assert.Equal(VanillaItemPrefixFamily.None, drop.PrefixFamily);
    }
}
''')

replace_once(
    'docs/en/npc-worm-ai-parity.md',
    '''This guide records the source-backed chain-lifecycle slice implemented for TerrariaServer 1.4.5.8 worm AI. It is deliberately narrower than full NPC parity: movement families, chain construction and the link lifecycle described here are admitted, while complete death/loot/progression and every AI_006 side effect remain separate work.\n''',
    '''This guide records the source-backed chain-lifecycle slice implemented for TerrariaServer 1.4.5.8 worm AI. It is deliberately narrower than full NPC parity: movement families, chain construction, link repair and the Eater of Worlds server-side death/loot/progression vertical described here are admitted, while remaining AI_006 side effects stay separate work.\n''')
replace_once(
    'docs/en/npc-worm-ai-parity.md',
    '''## Still incomplete\n\nThis evidence does not make `FullVanillaAiParity` true. Complete Eater of Worlds synchronized lifecycle, damage/death consequences, loot/progression, every `realLife` interaction, remaining AI_006 special branches and broad differential gameplay scenarios remain open in the NPC parity roadmap.\n''',
    '''## Eater of Worlds death and shared combat state\n\nPacket-28 player interaction now follows `NPC.PlayerInteraction` for types 13/14/15: a hit credits every currently active Eater segment, so later splits and segment deaths do not lose the player list used by per-player boss loot. On lethal damage the runtime performs the same `DropEoWLoot` family scan: every segment evaluates the two small Shadow Scale/Demonite rules, but only the final active segment is promoted to boss for the Expert bag, Master relic/per-player pet, normal-only finishing drops and trophy. The final segment also marks `VanillaWorldProgressionId.EvilBoss`, and `WorldFileProgressionHeaderPatcher` now persists that mutation to the 1.4.5.8 `downedBoss2` header byte.\n\n## Still incomplete\n\nThis evidence does not make `FullVanillaAiParity` true. Eater meteor scheduling, the Skyblock low-tile `shadowOrbSmashed` death side effect, healing-heart/presentation effects, unowned `realLife` nuances and broad differential gameplay scenarios remain open in the NPC parity roadmap.\n''')
replace_once(
    'docs/ru/npc-worm-ai-parity.md',
    '''Этот документ фиксирует source-backed часть chain lifecycle для worm AI из TerrariaServer 1.4.5.8. Это намеренно уже полной NPC parity: movement families, построение цепочки и описанная здесь link lifecycle допускаются, а полный death/loot/progression и все побочные ветви AI_006 остаются отдельной работой.\n''',
    '''Этот документ фиксирует source-backed часть chain lifecycle для worm AI из TerrariaServer 1.4.5.8. Это намеренно уже полной NPC parity: movement families, построение цепочки, link repair и описанный здесь server-side death/loot/progression vertical Eater of Worlds допускаются, а оставшиеся побочные ветви AI_006 остаются отдельной работой.\n''')
replace_once(
    'docs/ru/npc-worm-ai-parity.md',
    '''## Что ещё не закончено\n\nЭто evidence не делает `FullVanillaAiParity` истинным. Полный synchronized lifecycle Eater of Worlds, последствия damage/death, loot/progression, все взаимодействия `realLife`, оставшиеся специальные ветви AI_006 и широкие differential gameplay scenarios остаются открытыми в NPC parity roadmap.\n''',
    '''## Death и shared combat state Eater of Worlds\n\nPacket-28 player interaction теперь повторяет `NPC.PlayerInteraction` для типов 13/14/15: попадание выдаёт credit всем текущим active Eater segments, поэтому последующие split и смерти сегментов не теряют player list для per-player boss loot. При lethal damage runtime выполняет такой же family scan `DropEoWLoot`: каждый сегмент вычисляет две малые rules Shadow Scale/Demonite, но только последний active segment временно получает boss-семантику для Expert bag, Master relic/per-player pet, normal-only finishing drops и trophy. Последний сегмент также отмечает `VanillaWorldProgressionId.EvilBoss`, а `WorldFileProgressionHeaderPatcher` теперь сохраняет эту mutation в header byte `downedBoss2` формата 1.4.5.8.\n\n## Что ещё не закончено\n\nЭто evidence не делает `FullVanillaAiParity` истинным. Для Eater остаются meteor scheduling, Skyblock low-tile death side effect `shadowOrbSmashed`, healing-heart/presentation effects, неохваченные нюансы `realLife` и широкие differential gameplay scenarios из NPC parity roadmap.\n''')
replace_once(
    'docs/roadmap/npc-ai-parity.md',
    '''- [ ] Eater of Worlds death/loot/progression and complete synchronized lifecycle;\n''',
    '''- [x] Eater of Worlds packet-28 shared playerInteraction, per-segment material loot, last-segment boss promotion, Expert/Master/normal boss loot and persistent `downedBoss2` progression;\n- [ ] Eater of Worlds remaining death-event parity: meteor scheduling, Skyblock low-tile `shadowOrbSmashed`, healing-heart/presentation effects and remaining `realLife` nuances;\n''')

print('Eater of Worlds death/loot/progression block applied.')
