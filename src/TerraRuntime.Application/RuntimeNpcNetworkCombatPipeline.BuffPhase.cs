using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Application;

internal delegate bool RuntimeNpcBuffPhase1458(in NpcSnapshot before,
    in RuntimeNpcBuffPlan1458 plan, RuntimeNpcBuffStatus1458 status, VanillaUnifiedRandom1458 sourceRandom);

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    internal bool TryCommitZombieBuffPhase(in NpcSnapshot before, in RuntimeNpcBuffPlan1458 plan,
        RuntimeNpcBuffStatus1458 status, VanillaUnifiedRandom1458 sourceRandom)
    {
        if (before.TypeIdentity != VanillaNpcIds.Zombie || plan.Expected != before ||
            plan.LifeAfter <= 0 || !ReferenceEquals(random.SourceRandom, sourceRandom) || !status.IsCurrent(in plan))
            return false;
        var checkpoint = sourceRandom.Clone();
        var after = checkpoint.Clone();
        var offer = RuntimeNpcBuffStatus1458.PlanVisualOffers(in plan,
            new NpcRuntimeTownCombatRandom1458(new TerraRuntime.Core.Npcs.SystemVanillaNpcRandom(after)), false);
        if (!sourceRandom.HasSameState(checkpoint) || !status.IsCurrent(in plan)) return false;
        var committed = before;
        if (before.Simulation.Life != plan.LifeAfter || before.Simulation.LifeRegenCounter != plan.CounterAfter)
        {
            var update = new NpcStateUpdate(before.Type, before.NetId, before.PositionX, before.PositionY,
                before.VelocityX, before.VelocityY, before.Target, before.Ai,
                before.Simulation with { Life = plan.LifeAfter, LifeRegenCounter = plan.CounterAfter });
            if (!npcs.TryUpdateUnpublished(before.Handle, in update, out committed)) return false;
        }
        if (!status.Commit(in plan, in committed))
            throw new InvalidOperationException("Accepted Zombie buff phase lost its retained status owner.");
        status.ObserveVisualOffer(committed.Handle, in offer);
        sourceRandom.CopyStateFrom(after);
        status.PublishExpired(in plan);
        return true;
    }
}
