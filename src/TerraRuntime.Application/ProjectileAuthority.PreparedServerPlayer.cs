using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

internal sealed partial class ProjectileAuthority
{
    // The same accepted per-actor use owner is shared by trusted ranged and prepared melee producers.
    internal bool CanUseTrustedItem(in PlayerStateSnapshot expected, long tick, int useTime) =>
        tick >= 0 && useTime > 0 && tick <= long.MaxValue - useTime && expected.Player.IsAssigned &&
        !trustedClientUseCadence.IsOnCooldown(expected.Player, tick) &&
        playerSnapshots.TryGetPlayer(expected.Player.Slot, out var current) && current == expected;

    internal void MarkAcceptedTrustedItemUse(PlayerHandle player, long tick, int useTime)
    {
        if (!player.IsAssigned || tick < 0 || useTime < 1 || tick > long.MaxValue - useTime ||
            trustedClientUseCadence.IsOnCooldown(player, tick))
            throw new InvalidOperationException("Validated trusted item use changed during callback-free adoption.");
        trustedClientUseCadence.MarkUse(player, tick, useTime);
    }

    internal readonly record struct RangedTargetCapture(NpcSnapshot? Npc, PlayerStateSnapshot? Player);

    internal bool TryCaptureRangedTarget(NpcHandle npc, PlayerHandle player, out RangedTargetCapture capture)
    {
        capture = default;
        if (npc.IsAssigned)
        {
            if (!npcs.TryGet(npc, out var current) || !current.IsActive)
                return false;
            capture = new(current, null);
            return true;
        }
        if (!player.IsAssigned || !playerSnapshots.TryGetPlayer(player.Slot, out var target) ||
            target.Player != player || target.IsDead)
            return false;
        capture = new(null, target);
        return true;
    }

    internal bool IsCurrentRangedTarget(in RangedTargetCapture captured)
    {
        if (captured.Npc is { } npc)
            return npcs.TryGet(npc.Handle, out var current) && current == npc;
        return captured.Player is { } player && playerSnapshots.TryGetPlayer(player.Player.Slot, out var currentPlayer) &&
            currentPlayer == player;
    }

    // Reuse the projectile phase cursor; rejected preparation never advances its live owner.
    internal bool PrepareTrustedAmmoConservation(in VanillaProjectileWeaponCombatDefinition weapon,
        in VanillaProjectileAmmoCombatDefinition ammo, in VanillaPlayerCombatSnapshot combat,
        out VanillaUnifiedRandom1458 before, out VanillaUnifiedRandom1458 after)
    {
        before = projectileRandom.Clone();
        after = before.Clone();
        return PrepareAmmoConservation(in weapon, in ammo, in combat, after);
    }

    internal bool TryPrepareTrustedServerPlayerProjectile(in PlayerStateSnapshot expected,
        in ProjectileStateUpdate state, long tick, int useTime, out TrustedServerPlayerSpawnPreparation? plan,
        VanillaUnifiedRandom1458? randomBefore = null, VanillaUnifiedRandom1458? randomAfter = null)
    {
        plan = null;
        if ((randomBefore is null) != (randomAfter is null) ||
            randomBefore is not null && !projectileRandom.HasSameState(randomBefore) ||
            tick < 0 || useTime < 1 || trustedClientUseCadence.IsOnCooldown(expected.Player, tick) ||
            !expected.Player.IsAssigned || state.Spawner != expected.Player.Slot.Value ||
            !playerSnapshots.TryGetPlayer(expected.Player.Slot, out var current) || current != expected ||
            !projectiles.TryPrepareVanillaSpawnBatch([new(state)], out var prepared) || prepared is null)
            return false;
        RuntimeProjectileReplicationRegistry.SpawnBirthJournalPreparation? journal = null;
        if (replication is not null)
        {
            if (!RuntimeProjectilePacketProjection.TryCreateCanonicalKey(prepared.Births[0], out var key) ||
                !replication.TryPrepareSpawnBirthJournal(prepared.Births, [key], default, out journal))
            {
                prepared.Dispose();
                return false;
            }
        }
        plan = new(this, expected, tick, useTime, prepared, journal, randomBefore, randomAfter);
        return true;
    }

    internal sealed class TrustedServerPlayerSpawnPreparation(
        ProjectileAuthority owner, PlayerStateSnapshot expected, long tick, int useTime,
        RuntimeProjectileStore.SpawnBatchPreparation prepared,
        RuntimeProjectileReplicationRegistry.SpawnBirthJournalPreparation? journal,
        VanillaUnifiedRandom1458? randomBefore, VanillaUnifiedRandom1458? randomAfter) : IDisposable
    {
        private bool adopted;
        private bool published;
        private bool useAccepted;
        // This provider check belongs before the final pure player/BOT guards, never between writes.
        internal bool IsCurrent => !adopted &&
            (randomBefore is null || owner.projectileRandom.HasSameState(randomBefore)) && owner.playerSnapshots.TryGetPlayer(expected.Player.Slot, out var current) &&
            current == expected && !owner.trustedClientUseCadence.IsOnCooldown(expected.Player, tick) &&
            prepared.IsCurrentOwned && (journal is null || journal.IsCurrentOwned);

        internal bool TrySealPublicationRecipients() => !adopted &&
            (journal is null || journal.TrySealRecipients());

        internal bool TryAdoptUnpublished()
        {
            // Callers have completed provider captures and pure owner validation on the game thread.
            if (adopted || randomBefore is not null && !owner.projectileRandom.HasSameState(randomBefore) ||
                owner.trustedClientUseCadence.IsOnCooldown(expected.Player, tick) ||
                !prepared.IsCurrentOwned || (journal is not null && !journal.IsCurrentOwned) ||
                !prepared.TryCommitUnpublished())
                return false;
            var birth = prepared.FinalBirths[0];
            if (!owner.projectiles.TryMarkCombatTrusted(birth.Handle, expected.Player) ||
                (journal is not null && !journal.TryAdoptBaselines(owner.projectiles)))
                throw new InvalidOperationException("Validated trusted BOT spawn lost its callback-free ownership.");
            if (randomAfter is not null) owner.projectileRandom.CopyStateFrom(randomAfter);
            adopted = true;
            return true;
        }

        internal void AcceptItemUse()
        {
            if (!adopted || useAccepted)
                throw new InvalidOperationException("Prepared BOT item use must be accepted exactly once.");
            // Called after the player adoption, before any observer or external provider.
            owner.trustedClientUseCadence.MarkUse(expected.Player, tick, useTime);
            owner.AppliedSpawns++;
            useAccepted = true;
        }

        internal bool TryPublish()
        {
            if (!adopted || !useAccepted || published)
                return false;
            published = true;
            var birth = prepared.FinalBirths[0];
            if (!owner.playerSnapshots.TryGetPlayer(expected.Player.Slot, out var actor) || actor.Player != expected.Player ||
                !owner.projectiles.TryGet(birth.Handle, out var current) || current != birth ||
                !owner.projectiles.TryGetCombatTrustedOwner(birth.Handle, out var trusted) || trusted != expected.Player)
                return false;
            if (journal is not null)
            {
                if (!journal.TryPublish(owner.projectiles))
                    return false;
                using var observer = journal.EnterObserverPublication();
                return prepared.TryPublishBirthJournal();
            }
            return prepared.TryPublishBirthJournal();
        }

        public void Dispose() => prepared.Dispose();
    }
}
