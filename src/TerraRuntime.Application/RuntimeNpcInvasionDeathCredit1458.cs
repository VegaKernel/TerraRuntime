using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Worlds;

namespace TerraRuntime.Application;

// Capture before loot callbacks; adopt only after the complete source loot/healing phase.
internal sealed class RuntimeNpcInvasionDeathCredit1458
{
    private readonly RuntimeWorldInvasion1458 invasion;
    private readonly RuntimeInvasionCapture1458 before;
    private readonly InvasionTransition1458 transition;
    private readonly RuntimeNpcStore.TerminalRemoval removal;
    private bool adopted;

    private RuntimeNpcInvasionDeathCredit1458(RuntimeWorldInvasion1458 invasion,
        RuntimeInvasionCapture1458 before, InvasionTransition1458 transition,
        RuntimeNpcStore.TerminalRemoval removal)
    {
        this.invasion = invasion;
        this.before = before;
        this.transition = transition;
        this.removal = removal;
    }

    internal static bool TryPrepare(RuntimeWorldInvasion1458 invasion, RuntimeNpcStore npcs,
        in NpcSnapshot expectedDead, out RuntimeNpcInvasionDeathCredit1458? plan)
    {
        plan = null;
        // These two encounter deaths have additional source producers not represented by this adapter.
        if (expectedDead.Type is 216 or 395 || !invasion.TryCapture(out var before) ||
            !VanillaInvasionLifecycle1458.TryCreditDeath(before.State, expectedDead.Type, out var transition) ||
            !transition.Effects.Progress || !npcs.TryPrepareTerminalRemoval(in expectedDead, out var removal))
            return false;
        plan = new(invasion, before, transition, removal!);
        return true;
    }

    internal bool IsCurrent => !adopted && invasion.CanAdopt(in before, in transition) && removal.IsCurrent;

    internal bool TryAdoptAfterLoot(out RuntimeInvasionCapture1458 accepted,
        out RuntimeNpcStore.TerminalRemoval? publication)
    {
        accepted = default;
        publication = null;
        if (!IsCurrent) return false;
        // Both owners are synchronous sealed value owners: no callbacks between these direct mutations.
        if (!removal.TryAdoptUnpublished()) return false;
        if (!invasion.TryAdopt(in before, in transition, out accepted))
            throw new InvalidOperationException("A validated invasion owner changed without a callback.");
        adopted = true;
        publication = removal;
        return true;
    }
}
