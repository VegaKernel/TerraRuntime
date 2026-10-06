using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    private readonly VanillaBossRecoveryDailyState1458 bossRecoveryDaily;

    private bool CanAcceptBossDeathCapacity(in NpcSnapshot npc) => pendingDeathPlan is null;

    private NpcLootWorldItemOrigin ResolveNpcLootOrigin(in NpcSnapshot npc, in VanillaNpcDefinition definition,
        bool sourceCommonCode = true)
    {
        if (!definition.TryResolveHitbox(npc.Simulation, out VanillaNpcHitboxSize body))
            throw new InvalidOperationException("Committed NPC loot has no valid physical body.");
        // CommonCode.DropItemFromNPC and recovery's legacy Item.NewItem rectangle overload use integer halves.
        return new((int)npc.PositionX + body.Width / 2, (int)npc.PositionY + body.Height / 2,
            sourceCommonCode ? new NpcLootColorContext1458(npc.TypeIdentity, npc.NetIdentity, lootRemixWorld) : null);
    }

    private void DropBossRecoveryItemsIfEligible(in NpcSnapshot npc, bool eaterBoss)
    {
        if (!IsPreviewingDeath && pendingDeathPlan is { } plan)
        {
            if (!plan.TryPublishPhase(NpcDeathDropPhase1458.Recovery, lootDelivery.Adopt) ||
                !plan.TryPublishPhase(NpcDeathDropPhase1458.Money, lootDelivery.Adopt) ||
                !plan.TryPublishPhase(NpcDeathDropPhase1458.Healing, lootDelivery.Adopt))
                throw new InvalidOperationException("Accepted death events diverged from their owned RNG preview.");
            // Selected unpublished deaths adopted this state before their first outward callback.
            if (!plan.IsUnpublishedMode) bossRecoveryDaily.CopyFrom(plannedDaily!);
            plan.Dispose(); pendingDeathPlan = null; plannedDaily = null;
            plannedPrelude = null; plannedPreludeRevision = 0; plannedLootAllowed = false;
            return;
        }
        if (!eaterBoss && !VanillaBossRecovery1458.IsAdmittedRoot(npc.TypeIdentity)) return;
        if (npc.TypeIdentity == VanillaNpcIds.Retinazer || npc.TypeIdentity == VanillaNpcIds.Spazmatism)
        {
            NpcTypeId other = npc.TypeIdentity == VanillaNpcIds.Retinazer ? VanillaNpcIds.Spazmatism : VanillaNpcIds.Retinazer;
            int count = DeathNpcs.CopyActive(npcFamilyBuffer);
            for (int index = 0; index < count; index++)
                if (npcFamilyBuffer[index].TypeIdentity == other) return;
        }
        if (!VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out VanillaNpcDefinition definition))
            throw new InvalidOperationException("Committed boss recovery has no definition.");
        var origin = ResolveNpcLootOrigin(in npc, in definition);
        if (!VanillaBossRecovery1458.TryExecute(npc.TypeIdentity, in origin, DeathDaily, random, lootDelivery))
            throw new InvalidOperationException("Committed boss recovery could not be materialized.");
    }
}
