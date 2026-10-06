using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Worlds;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    private readonly RuntimeWorldInvasion1458? invasion;
    private readonly Action<RuntimeInvasionCapture1458>? invasionProgressPublisher;

    private RuntimeNpcInvasionDeathCredit1458? PrepareInvasionDeathCredit(in NpcSnapshot dead)
    {
        if (invasion is null) return null;
        return RuntimeNpcInvasionDeathCredit1458.TryPrepare(invasion, npcs, in dead, out var plan) ? plan : null;
    }

    private bool CanOwnInvasionDeath(in NpcSnapshot npc)
    {
        if (invasion is null || !invasion.TryCapture(out var capture)) return true;
        if (!VanillaInvasionLifecycle1458.TryCreditDeath(capture.State, npc.Type, out var transition)) return false;
        if (!transition.Effects.Progress) return true;
        // Captain ghost creation and saucer world/boss effects require their own complete death producers.
        return npc.Type is not (216 or 395) && invasion.CanAdopt(in capture, in transition);
    }

    private bool TryCompleteInvasionDeath(in NpcSnapshot dead, RuntimeNpcInvasionDeathCredit1458? plan)
    {
        if (plan is null) return false;
        if (plan.TryAdoptAfterLoot(out var accepted, out var terminal))
        {
            // Source checkDead has already set active=false when progress78 is emitted.
            // The retained terminal packet follows it, and skips a reused generation.
            invasionProgressPublisher!(accepted);
            terminal!.TryPublish();
        }
        else if (npcs.TryGet(dead.Handle, out var current) && current == dead)
        {
            // Reentrant loot may change the event. Finish the exact accepted death without
            // overwriting the newer event; a revised, revived or replacement actor survives.
            npcs.TryDespawn(dead.Handle);
        }
        return true;
    }
}
