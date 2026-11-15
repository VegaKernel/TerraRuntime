using System.Runtime.ExceptionServices;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeProjectilePlayerCombatPass
{
    // Reuse the existing detached human/server HP, inventory, buff and immunity
    // participants. These contacts have an environmental source, never a fake NPC.
    private void TickWiringDartPlayers(in ProjectileSnapshot shot, long tick)
    {
        if (sourceRandom is null || shot.Spawner != byte.MaxValue ||
            shot.Handle.Slot >= RuntimeProjectileStore.VanillaPhysicalSlotCount ||
            !projectiles.IsCombatTrusted(shot.Handle) || projectiles.TryGetCombatTrustedOwner(shot.Handle, out _) ||
            projectiles.TryGetServerNpcSource(shot.Handle, out _) ||
            !projectiles.TryGet(shot.Handle, out var current) || current != shot ||
            !projectiles.TryGetLifecycle(shot.Handle, out var life) || life.NpcProjectile || life.Reflected || life.TimeLeft <= 0 ||
            shot.OriginalDamage != 0 || shot.KnockBack != 2f || shot.Damage != (shot.Type.Value == 98 ? 20 : shot.Type.Value == 184 ? 40 : 30) ||
            !VanillaDefinitionCatalog.TryGet(shot.Type, out var definition) ||
            !players.TryPrepareGroundCombatHits(in shot, default, tick, expertMode, masterMode, goodWorld, null, out var humans) ||
            humans is null) return;
        ServerPlayerAuthority.GroundCombatHitPreparation? servers = null;
        if (serverPlayers is not null &&
            (!serverPlayers.TryPrepareGroundCombatHits(in shot, default, tick, expertMode, masterMode, goodWorld, null, out servers) ||
             servers is null)) return;
        var before = sourceRandom.Clone(); var after = before.Clone();
        for (int slot = 0; slot < byte.MaxValue; slot++)
        {
            if (humans.HasPlayer(slot) && servers?.HasPlayer(slot) == true) return;
            if (humans.HasPlayer(slot))
            {
                if (!humans.TryStageTarget(slot, in shot, in definition, after, out _)) return;
            }
            else if (servers?.HasPlayer(slot) == true &&
                !servers.TryStageTarget(slot, in shot, in definition, after, out _)) return;
        }
        // Read the external clock first, then compare every retained concrete owner.
        if (tickProvider() != tick || !humans.IsCurrent || servers is not null && !servers.IsCurrent ||
            !sourceRandom.HasSameState(before) || !projectiles.TryGet(shot.Handle, out current) || current != shot ||
            !projectiles.TryGetLifecycle(shot.Handle, out var currentLife) || currentLife != life ||
            !projectiles.IsCombatTrusted(shot.Handle) || projectiles.TryGetCombatTrustedOwner(shot.Handle, out _) ||
            projectiles.TryGetServerNpcSource(shot.Handle, out _)) return;
        if (!humans.TryAdoptUnpublished() || servers is not null && !servers.TryAdoptUnpublished())
            throw new InvalidOperationException("Admitted wiring dart lost a callback-free player participant.");
        sourceRandom.CopyStateFrom(after);
        AdoptGroundCombatMetrics(humans, servers, false);
        ExceptionDispatchInfo? failure = null;
        try { humans.Publish(); } catch (Exception exception) { failure = ExceptionDispatchInfo.Capture(exception); }
        try { servers?.Publish(); } catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
        failure?.Throw();
    }
}
