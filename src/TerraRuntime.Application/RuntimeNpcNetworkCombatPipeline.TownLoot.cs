using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    private NpcHandle plannedTownNameOwner;
    private string? plannedTownName;

    private bool TryExecuteTownSpecificLoot(in NpcSnapshot npc)
    {
        if (!VanillaTownNpcLootRules1458.TryGet(npc.TypeIdentity, out var rules)) return false;
        bool? named = false;
        if (VanillaTownNpcLootRules1458.RequiresName(npc.TypeIdentity))
        {
            string? name = townNameSource?.Invoke(npc.Handle);
            named = VanillaTownNpcLootRules1458.CaptureNamedEligibility(npc.TypeIdentity, name, townLootLanguage);
            if (IsPreviewingDeath) { plannedTownNameOwner = npc.Handle; plannedTownName = name; }
        }
        bool hardmode = plannedGlobalLootWorld?.HardMode == true ||
            DeathProgression.IsCompleted(VanillaWorldProgressionId.Hardmode);
        var context = new VanillaTownNpcLootContext1458(named, hardmode);
        if (!VanillaTownNpcLootRules1458.TryValidateContext(rules, in context) ||
            !VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out var definition)) return false;
        var origin = ResolveNpcLootOrigin(in npc, in definition);
        foreach (ref readonly var rule in rules)
        {
            if (!VanillaTownNpcLootRules1458.TryEvaluateRule(in rule, in context, random,
                    out bool dropped, out var drop) ||
                (dropped && !lootDelivery.TryWorld(in origin, in drop, random))) return false;
        }
        return true;
    }

    private bool IsTownNameCurrent() => !plannedTownNameOwner.IsAssigned ||
        townNameSource?.Invoke(plannedTownNameOwner) == plannedTownName;
}
