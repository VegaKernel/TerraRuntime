using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeProjectileNpcCombatPass
{
    private bool TickWiringDart(in ProjectileSnapshot initial, int npcCount, long tick)
    {
        if (!usesSourceRandom || status is null ||
            !TerraRuntime.Gameplay.Projectiles.VanillaDefinitionCatalog.TryGet(initial.Type, out var definition)) return false;
        if (initial.Type.Value == 188)
        {
            if (!projectiles.TryGetLifecycle(initial.Handle, out var flameLife)) return false;
            if (flameLife.LocalAi.Ai0 < 5) return true;
        }
        var shot = initial;
        int ownerRow = byte.MaxValue * npcs.Capacity;
        for (int i = 0; i < npcCount; i++)
        {
            var target = npcBuffer[i];
            if (!IsEligibleTarget(target, out var body) || !Intersects(shot, definition, target, body) ||
                IsOwnerNpcOnCooldown(ownerRow, target.Handle, tick)) continue;
            if (target.TypeIdentity.Value is not (1 or 3 or 59) || target.Simulation.LifeRegenCounter is null ||
                target.Simulation.Immortal != false ||
                target.TypeIdentity.Value == 3 && (target.Simulation.ShimmerTransparency != 0 || target.Simulation.LiquidContact == NpcLiquidContactKind.Shimmer) ||
                target.TypeIdentity.Value == 1 && target.Ai.Ai1 == 1345f ||
                !TryPlanTrapStatus(in target, shot, out var buffPlan) ||
                !projectiles.TryGetLifecycle(shot.Handle, out var life) || life.NpcProjectile ||
                !projectiles.TryPrepareSimulation(in shot, out var actor) || actor is null) continue;
            // SimulationPreparation is a detached journal; it holds no allocation claim.
            {
                var current = shot;
                int direction = target.PositionX + body.Width * .5f < shot.PositionX + definition.Width * .5f ? -1 : 1;
                if (!actor.TryStageSourceDirection(direction)) continue;
                var before = sourceRandom.Clone(); var after = before.Clone();
                int damage = VanillaIncomingPlayerDamageFacts1458.ApplyDamageVariation(shot.Damage, after.Next(-15, 16));
                int immunityIndex = ownerRow + target.Handle.Slot;
                var immunityGeneration = lastOwnerNpcHitGeneration[immunityIndex]; long immunityTick = lastOwnerNpcHitTick[immunityIndex];
                bool Current() => tickProvider() == tick && actor.IsCurrent && buffPlan.IsCurrent &&
                    projectiles.TryGet(current.Handle, out var live) && live == current &&
                    projectiles.TryGetLifecycle(current.Handle, out var retainedLife) && retainedLife == life &&
                    lastOwnerNpcHitGeneration[immunityIndex] == immunityGeneration && lastOwnerNpcHitTick[immunityIndex] == immunityTick &&
                    sourceRandom.HasSameState(before);
                VanillaUnifiedRandom1458? acceptedRandom = null;
                void Adopt(NpcSnapshot accepted)
                {
                    if (!status.TryAdoptAddition(buffPlan, in accepted) || !actor.TryAdoptUnpublished(out current, out bool ended) || ended)
                        throw new InvalidOperationException("Admitted wiring dart lost its status/projectile participants.");
                    MarkOwnerNpcCooldown(ownerRow, target.Handle, tick); CommittedHits++;
                    if (accepted.Simulation.Life == 0) Kills++;
                    acceptedRandom = sourceRandom.Clone();
                    status.PublishAddition(buffPlan);
                }
                if (combat.TryStrikePreparedWiringDart(projectiles, in shot, in target, direction, damage, before, after, Current, Adopt) == RuntimeProjectileNpcDamageResult.Rejected)
                    continue;
                shot = current;
                if (acceptedRandom is null || !sourceRandom.HasSameState(acceptedRandom) || !actor.IsPublicationCurrent) return false;
            }
        }
        return true;
    }
    internal bool TickWiringFlameContact(in ProjectileSnapshot shot)
    {
        if (!ordinaryArrowSimulationActive || !MarkOrdinaryArrowHandled(in shot)) return false;
        long tick = tickProvider();
        int count = npcs.CopyActive(npcBuffer);
        return TickWiringDart(in shot, count, tick);
    }

    private bool TryPlanTrapStatus(in NpcSnapshot target, ProjectileSnapshot shot, out RuntimeNpcBuffAdditionPlan1458 plan)
    {
        if (shot.Type.Value is 185 or 186) return status!.CaptureAddition(in target, out plan);
        return status!.TryPlanAddition(in target,
            shot.Type.Value == 188 ? VanillaBuffIds.OnFire3 : shot.Type.Value == 980 ? VanillaBuffIds.Venom : VanillaBuffIds.Poisoned,
            shot.Type.Value == 188 ? 1200 : shot.Type.Value == 980 ? 300 : shot.Type.Value == 98 ? 600 : 900, out plan);
    }

}
