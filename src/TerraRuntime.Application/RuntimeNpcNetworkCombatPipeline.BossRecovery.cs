using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    private readonly VanillaBossRecoveryDailyState1458 bossRecoveryDaily;

    private bool CanAcceptBossDeathCapacity(in NpcSnapshot npc)
    {
        if (!VanillaBossRecovery1458.IsAdmittedRoot(npc.TypeIdentity) &&
            !VanillaEaterOfWorldsLifecycle.IsSegment(npc.TypeIdentity) &&
            !IsDestroyerMember(npc.TypeIdentity) && npc.TypeIdentity != VanillaNpcIds.WallOfFleshEye)
            return true;
        // Conservative retained-family ceiling: ordinary rules fit MaxOrdinaryDrops; expert bags
        // lease one slot, master rules can additionally materialize one pet/mount per interacting player.
        // This is bounded pressure admission, not vanilla's age-based replacement/emergency stacking.
        int masterRecipients = 0;
        if (masterMode)
            for (int slot = 0; slot < VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots; slot++)
                if (TryGetActiveLootPlayer(new PlayerSlotId(checked((byte)slot)), out _)) masterRecipients++;
        int required = MaxOrdinaryDrops + VanillaBossRecovery1458.MaximumRecoveryDrops +
            (expertMode ? 1 : 0) + masterRecipients;
        Span<WorldItemDropReservation> slots = stackalloc WorldItemDropReservation[required];
        int count = 0;
        try
        {
            for (; count < slots.Length; count++)
                if (!worldItems.TryReserveDropSlot(out slots[count])) return false;
            return true;
        }
        finally
        {
            for (int index = 0; index < count; index++)
                if (!worldItems.TryReleaseDropReservation(in slots[index]))
                    throw new InvalidOperationException("Boss-death capacity probe lost its exact reservation.");
        }
    }

    private static NpcLootWorldItemOrigin ResolveNpcLootOrigin(in NpcSnapshot npc, in VanillaNpcDefinition definition)
    {
        if (!definition.TryResolveHitbox(npc.Simulation, out VanillaNpcHitboxSize body))
            throw new InvalidOperationException("Committed NPC loot has no valid physical body.");
        // CommonCode.DropItemFromNPC and recovery's legacy Item.NewItem rectangle overload use integer halves.
        return new((int)npc.PositionX + body.Width / 2, (int)npc.PositionY + body.Height / 2);
    }

    private void DropBossRecoveryItemsIfEligible(in NpcSnapshot npc, bool eaterBoss)
    {
        if (!eaterBoss && !VanillaBossRecovery1458.IsAdmittedRoot(npc.TypeIdentity)) return;
        if (npc.TypeIdentity == VanillaNpcIds.Retinazer || npc.TypeIdentity == VanillaNpcIds.Spazmatism)
        {
            NpcTypeId other = npc.TypeIdentity == VanillaNpcIds.Retinazer ? VanillaNpcIds.Spazmatism : VanillaNpcIds.Retinazer;
            int count = npcs.CopyActive(npcFamilyBuffer);
            for (int index = 0; index < count; index++)
                if (npcFamilyBuffer[index].TypeIdentity == other) return;
        }
        if (!VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out VanillaNpcDefinition definition))
            throw new InvalidOperationException("Committed boss recovery has no definition.");
        var origin = ResolveNpcLootOrigin(in npc, in definition);
        if (!VanillaBossRecovery1458.TryExecute(npc.TypeIdentity, in origin, bossRecoveryDaily, random, wallOfFleshLoot))
            throw new InvalidOperationException("Committed boss recovery could not be materialized.");
    }
}
