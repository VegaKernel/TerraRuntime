using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeProjectileNpcCombatPass
{
    /// <summary>Called inline while the exact source world-bomb Kill generation remains active, before29.</summary>
    internal bool TryProcessWorldToolBombContacts(in ProjectileSnapshot source,
        out int acceptedHits, out ProjectileSnapshot acceptedCurrent, out VanillaUnifiedRandom1458 acceptedRandom)
    {
        acceptedHits = 0; acceptedCurrent = source; acceptedRandom = sourceRandom.Clone();
        if (!usesSourceRandom || status is null || source.Type.Value != 28 || source.Spawner != byte.MaxValue ||
            source.Damage != 100 || !projectiles.IsCombatTrusted(source.Handle) ||
            projectiles.TryGetCombatTrustedOwner(source.Handle, out _) || projectiles.TryGetServerNpcSource(source.Handle, out _) ||
            !projectiles.TryGet(source.Handle, out var retained) || retained != source ||
            !projectiles.TryGetLifecycle(source.Handle, out var lifecycle) || !lifecycle.NpcProjectile ||
            !lifecycle.WorldToolBombKillInProgress || lifecycle.TimeLeft != 0) return false;
        // NPC.immune[255] is the shared dedicated world source row, not a fake player generation.
        int ownerRow = byte.MaxValue * npcs.Capacity;
        long tick = tickProvider();
        int count = npcs.CopyActive(npcBuffer), hits = 0;
        var current = source;
        var currentRandom = acceptedRandom;
        float centerX = source.PositionX + 64, centerY = source.PositionY + 64;
        var area = new RuntimeProjectileExplosionEvent(source, default, default, centerX - 64, centerY - 64, 128, 128);
        try
        {
            for (int index = 0; index < count; index++)
            {
                var target = npcBuffer[index];
                if (target.Simulation.Friendly == true || target.Simulation.Immortal == true ||
                    target.Simulation.Friendly is null && VanillaNpcChaseability1458.FriendlyAtSpawn(target.TypeIdentity.Value)) continue;
                if (!IsEligibleTarget(in target, out var body) || !Intersects(in area, in target, in body) ||
                    IsOwnerNpcOnCooldown(ownerRow, target.Handle, tick)) continue;
                if (target.TypeIdentity.Value is not (1 or 3 or 59) || target.Simulation.LifeRegenCounter is null ||
                    target.Simulation.Immortal != false ||
                    target.TypeIdentity.Value == 3 && (target.Simulation.ShimmerTransparency != 0 ||
                        target.Simulation.LiquidContact == NpcLiquidContactKind.Shimmer) ||
                    target.TypeIdentity.Value == 1 && target.Ai.Ai1 == 1345f ||
                    !npcs.TryCaptureDeathMutationSerial(out ulong npcSerial) ||
                    !status.CaptureAddition(in target, out var buffPlan) ||
                    !projectiles.TryPrepareSimulation(in current, out var directionPlan) || directionPlan is null) return false;
                int direction = target.PositionX + body.Width / 2 < centerX ? -1 : 1;
                if (!directionPlan.TryStageSourceDirection(direction)) return false;
                var before = sourceRandom.Clone(); var after = before.Clone();
                if (!VanillaCombatFacts.TryResolveWorldToolBombPveHit(current.Type, current.Damage,
                    after.Next(-15, 16), out var hit)) return false;
                int immunityIndex = ownerRow + target.Handle.Slot;
                var immunityGeneration = lastOwnerNpcHitGeneration[immunityIndex];
                long immunityTick = lastOwnerNpcHitTick[immunityIndex];
                var handle = current.Handle;
                bool Current() => tickProvider() == tick &&
                    directionPlan.IsCurrent && projectiles.TryGet(handle, out var live) && live == current &&
                    projectiles.TryGetLifecycle(handle, out var life) && life == lifecycle &&
                    projectiles.IsCombatTrusted(handle) && !projectiles.TryGetCombatTrustedOwner(handle, out _) &&
                    !projectiles.TryGetServerNpcSource(handle, out _) &&
                    npcs.TryCaptureDeathMutationSerial(out ulong serial) && serial == npcSerial &&
                    buffPlan.IsCurrent && lastOwnerNpcHitGeneration[immunityIndex] == immunityGeneration &&
                    lastOwnerNpcHitTick[immunityIndex] == immunityTick && sourceRandom.HasSameState(before);
                void Adopt(NpcSnapshot accepted)
                {
                    if (!status.TryAdoptAddition(buffPlan, in accepted) ||
                        !directionPlan.TryAdoptUnpublished(out current, out bool ended) || ended)
                        throw new InvalidOperationException("A prepared world bomb hit lost its source status owner.");
                    MarkOwnerNpcCooldown(ownerRow, target.Handle, tick);
                    // The shared death prelude may have consumed source loot RNG.
                    // Capture its accepted endpoint before the first status observer.
                    currentRandom.CopyStateFrom(sourceRandom);
                    hits++; CommittedHits++;
                    if (accepted.Simulation.Life == 0) Kills++;
                    status.PublishAddition(buffPlan);
                }
                if (combat.TryStrikePreparedWorldToolBomb(projectiles, in current, in target, direction, hit.Damage,
                    before, after, Current, Adopt) == RuntimeProjectileNpcDamageResult.Rejected) return false;
                if (!directionPlan.IsPublicationCurrent || !projectiles.TryGet(handle, out retained) || retained != current ||
                    !projectiles.TryGetLifecycle(handle, out var afterLife) || afterLife != lifecycle ||
                    !sourceRandom.HasSameState(currentRandom)) return false;
            }
            return true;
        }
        finally { acceptedHits = hits; acceptedCurrent = current; acceptedRandom = currentRandom; }
    }
}
