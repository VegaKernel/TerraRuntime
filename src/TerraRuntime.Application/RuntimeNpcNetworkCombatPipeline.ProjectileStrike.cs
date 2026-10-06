using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    private ProjectileStrikePlan1458? pendingProjectileStrike;
    private RuntimeNpcDeathPrelude1458.PreludePublication? preparedDeathPreludePublication;
    private int pendingProjectileResolvedDamage;
    private NpcSnapshot preparedProjectileCommittedNpc;

    private sealed record ProjectileStrikePlan1458(VanillaUnifiedRandom1458 Before,
        VanillaUnifiedRandom1458 After, Func<bool> IsCurrent, NpcHandle Target, PlayerHandle Player);

    internal RuntimeProjectileNpcDamageResult TryStrikePreparedProjectile(in ProjectileSnapshot projectile,
        in NpcSnapshot target, int hitDirection, int damageValue, int armorPenetration, bool critical,
        VanillaUnifiedRandom1458 before, VanillaUnifiedRandom1458 after, Func<bool> isCurrent,
        Action<NpcSnapshot> adoptAndPublish)
    {
        if (pendingProjectileStrike is not null || pendingDeathPlan is not null || pendingDebuffPrelude is not null ||
            !random.SourceRandom.HasSameState(before) || !isCurrent() ||
            !npcs.TryGet(target.Handle, out var retained) || retained != target ||
            !ProjectileNpcHitIntentBuilder.TryCreateNpcHit(in projectile, target.Handle, hitDirection,
                damageValue, armorPenetration, critical, players, out var intent) ||
            !intent.TryCreateDamageRequest(out var request)) return RuntimeProjectileNpcDamageResult.Rejected;
        pendingProjectileStrike = new(before, after, isCurrent, target.Handle, request.Source.Player);
        bool Current() => random.SourceRandom.HasSameState(before) && isCurrent() &&
            IsPreparedDeathCurrent();
        void Publish(NpcSnapshot committed)
        {
            AdoptPreparedStrikeOwners();
            adoptAndPublish(committed);
        }
        var prelude = new NpcDamagePrelude1458(target, null, Current, Publish);
        try
        {
            return TryStrikePlayerOwnedDamage(in target, in request, prelude);
        }
        finally
        {
            pendingProjectileStrike = null;
            preparedDeathPreludePublication = null;
            CancelPendingDeathPlan();
        }
    }

    private void AdoptPreparedStrikeOwners()
    {
        if (pendingDeathPlan is { } death)
        {
            if (!death.TryAdoptUnpublished() || plannedPrelude is null ||
                !deathPrelude.TryAdoptUnpublished(plannedPrelude, plannedPreludeRevision,
                    out preparedDeathPreludePublication))
                throw new InvalidOperationException("An admitted projectile death lost its retained owners.");
            if (plannedDaily is not null) bossRecoveryDaily.CopyFrom(plannedDaily);
            if (worldClock is not null && plannedUnpublishedClock is not null)
                worldClock.AdoptSlimeRainDeathProgress(plannedUnpublishedClock);
        }
        else if (pendingProjectileStrike is { } projectile)
        {
            random.SourceRandom.CopyStateFrom(projectile.After);
            ExecuteNpcNonlethalHitEffects(in preparedProjectileCommittedNpc, pendingProjectileResolvedDamage);
        }
    }

    private bool PublishPreparedDeathPrelude() => preparedDeathPreludePublication?.TryPublish() == true;

    private bool IsPreparedDeathCurrent() => pendingDeathPlan is null ||
        pendingDeathPlan.CanAdoptUnpublished() && deathPrelude.Revision == plannedPreludeRevision &&
        progression.CaptureSnapshot() == plannedDeathProgression && CaptureStrikeClock() == plannedStrikeClock &&
        CaptureStrikeDaily() == plannedStrikeDaily;
}
