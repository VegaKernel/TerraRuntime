using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    private NpcHandle plannedGuideNameOwner;
    private string? plannedGuideName;

    private bool TryExecuteGuideNameLoot(in NpcSnapshot npc)
    {
        string? name = guideNameSource?.Invoke(npc.Handle);
        if (name is null) return false;
        if (IsPreviewingDeath) { plannedGuideNameOwner = npc.Handle; plannedGuideName = name; }
        if (!string.Equals(name, "Andrew", StringComparison.Ordinal)) return true;
        if (!VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out var definition)) return false;
        var origin = ResolveNpcLootOrigin(in npc, in definition);
        // ItemDropWithConditionRule(867,1,1,1,NamedNPC): its denominator-one luck roll is real,
        // followed by Next(1,2), before NewItem's two default velocity draws.
        if (random.RollLuck(1) != 0) return false;
        int stack = random.NextInt32(1, 2);
        var drop = new NpcLootDrop(VanillaItemIds.GreenCap, (short)stack);
        return lootDelivery.TryDeliverWorldItem(in origin, in drop, random);
    }

    private bool IsGuideNameCurrent() => !plannedGuideNameOwner.IsAssigned ||
        guideNameSource?.Invoke(plannedGuideNameOwner) == plannedGuideName;
}
