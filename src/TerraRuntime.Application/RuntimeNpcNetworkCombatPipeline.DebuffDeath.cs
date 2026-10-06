using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;

namespace TerraRuntime.Application;

internal delegate bool RuntimeNpcDebuffDeath1458(in NpcSnapshot before,
    in RuntimeNpcBuffPlan1458 plan, RuntimeNpcBuffStatus1458 status, VanillaUnifiedRandom1458 sourceRandom);

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    private NpcDamagePrelude1458? pendingDebuffPrelude;
    private RuntimeNpcBuffPlan1458? pendingDebuffPlan;
    private RuntimeNpcStinkyVisualOffer1458 pendingDebuffOffer;

    internal bool TryStrikeDebuffDeath(in NpcSnapshot before, in RuntimeNpcBuffPlan1458 plan,
        RuntimeNpcBuffStatus1458 status, VanillaUnifiedRandom1458 sourceRandom)
    {
        if (pendingDebuffPrelude is not null || pendingDeathPlan is not null ||
            before.TypeIdentity != VanillaNpcIds.BlueSlime && before.TypeIdentity != VanillaNpcIds.LavaSlime &&
            before.TypeIdentity != VanillaNpcIds.Zombie ||
            plan.Expected != before || plan.LifeAfter > 0 || plan.DotDamage <= 0 ||
            plan.CounterAfter is not { } counter || counter is < -119 or > 119 ||
            before.Simulation.Immortal != false || before.Simulation.DontTakeDamage ||
            before.Simulation.Life <= 0 || !ReferenceEquals(random.SourceRandom, sourceRandom) ||
            !status.IsCurrent(in plan)) return false;

        var expected = before;
        var retainedPlan = plan;
        var checkpoint = sourceRandom.Clone();
        bool Current() => ReferenceEquals(random.SourceRandom, sourceRandom) &&
            sourceRandom.HasSameState(checkpoint) && status.IsCurrent(in retainedPlan) && IsPreparedDeathCurrent();
        void Publish(NpcSnapshot committed)
        {
            AdoptPreparedStrikeOwners();
            if (!status.Commit(in retainedPlan, in committed))
                throw new InvalidOperationException("An admitted debuff death lost its retained status owner.");
            status.ObserveVisualOffer(committed.Handle, in pendingDebuffOffer);
            status.PublishExpired(in retainedPlan);
        }
        // NPC.GetHurtByDebuff first subtracts its unmitigated pulse, then restores life=1
        // before calling StrikeNPCNoInteraction(9999,0,0). Preserve that genuine strike
        // arithmetic and packet28 while keeping the pre-strike state detached until admission.
        var prelude = new NpcDamagePrelude1458(expected,
            expected.Simulation with { Life = 1, LifeRegenCounter = counter }, Current, Publish);
        pendingDebuffPrelude = prelude;
        pendingDebuffPlan = retainedPlan;
        try
        {
            return CommitNonPlayerDamage(expected, DamageSource.Environment, 9999, 0f, 0, prelude) ==
                RuntimeTownNpcMeleeDamageResult1458.Killed;
        }
        finally
        {
            pendingDebuffPrelude = null;
            pendingDebuffPlan = null;
            pendingDebuffOffer = default;
            CancelPendingDeathPlan();
        }
    }

    private bool IsCurrentDebuffPrelude() => pendingDebuffPrelude?.IsCurrent() ?? true;
}
