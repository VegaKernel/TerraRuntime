using TerraRuntime.Gameplay.Items;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeProjectilePlayerCombatPass
{
    private void TickExplosions(ReadOnlySpan<RuntimeProjectileExplosionEvent> explosions, long tick)
    {
        for (int explosionIndex = 0; explosionIndex < explosions.Length; explosionIndex++)
        {
            RuntimeProjectileExplosionEvent explosion = explosions[explosionIndex];
            ProjectileSnapshot projectile = explosion.Projectile;
            if (explosion.SourceNpc.IsAssigned)
            {
                TickHostileNpcExplosion(in explosion, tick);
                continue;
            }

            PlayerHandle trustedOwner = explosion.TrustedOwner;
            if (!projectile.IsActive || projectile.Damage <= 0 ||
                !VanillaProjectileOwnership.IsPlayerOwned(projectile.Spawner) ||
                VanillaProjectileFacts.IsHostile(projectile.Type) ||
                !VanillaProjectileExplosionFacts.TryGetOnKillExplosion(projectile.Type, out _) ||
                !VanillaCombatFacts.TryGetDamageClass(projectile.Type, out _) ||
                !players.TryGet(trustedOwner, out RuntimePlayerMember? owner) ||
                !players.TryCaptureCombatSnapshot(trustedOwner, out VanillaPlayerCombatSnapshot ownerCombat) ||
                owner.Connection.Player != trustedOwner || owner.Slot.Value != projectile.Spawner ||
                !owner.Hostile || owner.IsDead)
            {
                continue;
            }

            foreach (RuntimePlayerMember target in players.Members)
            {
                if (target.Slot.Value == projectile.Spawner || !target.Hostile || target.IsDead ||
                    !target.HasHealth || target.Life <= 0 ||
                    (owner.Team != 0 && owner.Team == target.Team) ||
                    IsPlayerOnProjectileCooldown(projectile.Handle, target.Connection.Player, tick) ||
                    !Intersects(in explosion, target))
                {
                    continue;
                }

                int meleeCritRoll = VanillaCombatFacts.UsesMeleePvpCrit(projectile.Type)
                    ? random.Next(1, 101)
                    : 100;
                int damageVariation = random.Next(-15, 16);
                if (!VanillaCombatFacts.TryResolvePvpHit(
                        projectile.Type,
                        projectile.Damage,
                        in ownerCombat,
                        meleeCritRoll,
                        damageVariation,
                        out VanillaProjectileResolvedHit hit))
                {
                    continue;
                }

                int direction = ResolveExplosionDirection(in explosion, target);
                bool killedBefore = target.IsDead;
                PlayerDamageCommitResult commitResult = players.TryCommitAuthoritativePvpDamage(
                        tick,
                        trustedOwner,
                        target.Connection.Player,
                        DamageSource.FromPlayerProjectile(trustedOwner, projectile.Handle),
                        hit.Damage,
                        hit.Critical,
                        direction,
                        out PlayerStateSnapshot committed);
                if (commitResult == PlayerDamageCommitResult.Rejected)
                    continue;

                MarkPlayerProjectileCooldown(projectile.Handle, target.Connection.Player, tick);
                if (commitResult == PlayerDamageCommitResult.Committed)
                {
                    CommittedHits++;
                    if (!killedBefore && committed.IsDead)
                        Kills++;
                }
            }
        }
    }

    private void TickHostileNpcExplosion(in RuntimeProjectileExplosionEvent explosion, long tick)
    {
        ProjectileSnapshot projectile = explosion.Projectile;
        if (!projectile.IsActive || projectile.Damage <= 0 ||
            !VanillaProjectileFacts.IsHostile(projectile.Type) ||
            !VanillaProjectileExplosionFacts.TryGetOnKillExplosion(projectile.Type, out _))
        {
            return;
        }

        VanillaPlayerImmunityChannel1458 immunityChannel =
            VanillaIncomingPlayerDamageFacts1458.GetHostileProjectileImmunityChannel(projectile.Type);
        foreach (RuntimePlayerMember target in players.Members)
        {
            if (target.IsDead || !target.HasHealth || target.Life <= 0 || !Intersects(in explosion, target))
                continue;

            int damage = VanillaIncomingPlayerDamageFacts1458.ResolveHostileProjectileDamage(
                projectile.Damage,
                random.Next(-15, 16));
            if (damage <= 0)
                continue;

            int hitDirection = ResolveExplosionDirection(in explosion, target);
            bool killedBefore = target.IsDead;
            PlayerDamageCommitResult result = players.TryCommitAuthoritativeNpcProjectileDamage(
                tick,
                explosion.SourceNpc,
                projectile.Handle,
                target.Connection.Player,
                damage,
                hitDirection,
                immunityChannel,
                out PlayerStateSnapshot committed);
            if (result == PlayerDamageCommitResult.Rejected)
                continue;

            if (result == PlayerDamageCommitResult.AvoidedByGodMode)
            {
                HostileGodModeAvoidances++;
                continue;
            }

            HostileCommittedHits++;
            if (!killedBefore && committed.IsDead)
                HostileKills++;
        }
    }

    private static bool Intersects(in RuntimeProjectileExplosionEvent explosion, RuntimePlayerMember player)
    {
        float right = explosion.Left + explosion.Width;
        float bottom = explosion.Top + explosion.Height;
        float playerRight = player.PositionX + PlayerAuthority.VanillaBasePlayerWidth;
        float playerBottom = player.PositionY + PlayerAuthority.VanillaBasePlayerHeight;
        return explosion.Left < playerRight && right > player.PositionX &&
               explosion.Top < playerBottom && bottom > player.PositionY;
    }

    private static int ResolveExplosionDirection(
        in RuntimeProjectileExplosionEvent explosion,
        RuntimePlayerMember target)
    {
        if (explosion.Projectile.VelocityX > 0.01f)
            return 1;
        if (explosion.Projectile.VelocityX < -0.01f)
            return -1;
        float targetCenter = target.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
        return targetCenter > explosion.CenterX ? 1 : targetCenter < explosion.CenterX ? -1 : 0;
    }
}
