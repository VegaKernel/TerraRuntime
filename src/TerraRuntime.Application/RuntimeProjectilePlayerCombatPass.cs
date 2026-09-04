using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime;

/// <summary>
/// Post-simulation trusted-projectile/PvP pass. It never consumes packet-117 damage. Only exact generations already
/// marked CombatTrusted can reach player HP, so speed/damage/AI are the server-simulated values from the projectile
/// store. The admitted source-backed slice also owns Projectile.playerImmune[target] semantics generation-safely.
/// </summary>
internal sealed class RuntimeProjectilePlayerCombatPass
{
    private const int PlayerSlotCount = byte.MaxValue + 1;
    private readonly RuntimeProjectileStore projectiles;
    private readonly PlayerAuthority players;
    private readonly Func<long> tickProvider;
    private readonly Random random;
    private readonly ProjectileSnapshot[] projectileBuffer;
    private readonly long[] lastProjectilePlayerHitTick;
    private readonly ProjectileGeneration[] lastProjectileHitGeneration;
    private readonly PlayerSessionGeneration[] lastTargetGeneration;

    public RuntimeProjectilePlayerCombatPass(
        RuntimeProjectileStore projectiles,
        PlayerAuthority players,
        Func<long> tickProvider,
        Random? random = null)
    {
        this.projectiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.tickProvider = tickProvider ?? throw new ArgumentNullException(nameof(tickProvider));
        this.random = random ?? Random.Shared;
        projectileBuffer = new ProjectileSnapshot[projectiles.Capacity];
        int immunityCells = checked(projectiles.Capacity * PlayerSlotCount);
        lastProjectilePlayerHitTick = new long[immunityCells];
        lastProjectileHitGeneration = new ProjectileGeneration[immunityCells];
        lastTargetGeneration = new PlayerSessionGeneration[immunityCells];
        Array.Fill(lastProjectilePlayerHitTick, long.MinValue);
    }

    public long CommittedHits { get; private set; }
    public long Kills { get; private set; }
    public long ConsumedProjectiles { get; private set; }
    public long HostileCommittedHits { get; private set; }
    public long HostileGodModeAvoidances { get; private set; }
    public long HostileKills { get; private set; }

    public void Tick(ReadOnlySpan<RuntimeProjectileExplosionEvent> explosions)
    {
        long tick = tickProvider();
        int projectileCount = projectiles.CopyActive(projectileBuffer);
        for (int i = 0; i < projectileCount; i++)
        {
            ProjectileSnapshot projectile = projectileBuffer[i];
            if (!projectiles.IsCombatTrusted(projectile.Handle) ||
                !projectiles.TryGetCombatTrustedOwner(projectile.Handle, out PlayerHandle trustedOwner) ||
                !IsEligible(in projectile, out VanillaProjectileDefinition definition) ||
                !players.TryGet(trustedOwner, out RuntimePlayerMember? owner) ||
                !players.TryCaptureCombatSnapshot(trustedOwner, out VanillaPlayerCombatSnapshot ownerCombat) ||
                owner.Connection.Player != trustedOwner ||
                owner.Slot.Value != projectile.Spawner ||
                !owner.Hostile || owner.IsDead)
            {
                continue;
            }

            bool ended = false;
            foreach (RuntimePlayerMember target in players.Members)
            {
                if (target.Slot.Value == projectile.Spawner || !target.Hostile || target.IsDead || !target.HasHealth || target.Life <= 0 ||
                    (owner.Team != 0 && owner.Team == target.Team) ||
                    IsPlayerOnProjectileCooldown(projectile.Handle, target.Connection.Player, tick) ||
                    !Intersects(in projectile, in definition, target))
                {
                    continue;
                }

                int meleeCritRoll = VanillaProjectileCombatFacts.UsesMeleePvpCrit(projectile.Type)
                    ? random.Next(1, 101)
                    : 100;
                int damageVariation = random.Next(-15, 16);
                if (!VanillaProjectileCombatFacts.TryResolvePvpHit(
                        projectile.Type,
                        projectile.Damage,
                        in ownerCombat,
                        meleeCritRoll,
                        damageVariation,
                        out VanillaProjectileResolvedHit hit))
                {
                    continue;
                }

                int direction = projectile.VelocityX > 0.01f ? 1 : projectile.VelocityX < -0.01f ? -1 : 0;
                bool killedBefore = target.IsDead;
                PlayerDamageCommitResult commitResult = players.TryCommitAuthoritativePvpDamage(
                        tick,
                        owner.Connection.Player,
                        target.Connection.Player,
                        DamageSource.FromPlayerProjectile(owner.Connection.Player, projectile.Handle),
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

                if (!projectiles.TryConsumeCombatHitPenetration(projectile.Handle, out bool despawned, out ProjectileSnapshot current))
                    break;
                if (despawned)
                {
                    ConsumedProjectiles++;
                    ended = true;
                    break;
                }
                projectile = current;
            }

            if (ended)
                continue;
        }

        TickServerHostilePve(projectileBuffer.AsSpan(0, projectileCount), tick);
        TickExplosions(explosions, tick);
    }

    private void TickServerHostilePve(ReadOnlySpan<ProjectileSnapshot> activeProjectiles, long tick)
    {
        for (int i = 0; i < activeProjectiles.Length; i++)
        {
            ProjectileSnapshot projectile = activeProjectiles[i];
            if (!projectile.IsActive || projectile.Damage <= 0 ||
                !VanillaProjectileFacts.IsHostile(projectile.Type) ||
                !projectiles.TryGetServerNpcSource(projectile.Handle, out NpcHandle sourceNpc) ||
                !VanillaProjectileDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition) ||
                !VanillaProjectileBehaviorProfileCatalog.TryGet(projectile.Type, out VanillaProjectileBehaviorProfile profile) ||
                !profile.BehaviorImplemented)
            {
                continue;
            }

            VanillaPlayerImmunityChannel1458 immunityChannel =
                VanillaIncomingPlayerDamageFacts1458.GetHostileProjectileImmunityChannel(projectile.Type);
            foreach (RuntimePlayerMember target in players.Members)
            {
                PlayerHandle targetHandle = target.Connection.Player;
                if (target.IsDead || !target.HasHealth || target.Life <= 0 ||
                    (target.GodMode && IsPlayerOnProjectileCooldown(projectile.Handle, targetHandle, tick)) ||
                    !Intersects(in projectile, in definition, target))
                {
                    continue;
                }

                int damage = VanillaIncomingPlayerDamageFacts1458.ResolveHostileProjectileDamage(
                    projectile.Damage,
                    random.Next(-15, 16));
                if (damage <= 0)
                    continue;

                float projectileCenterX = projectile.PositionX + definition.Width * 0.5f;
                float targetCenterX = target.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
                int hitDirection = targetCenterX < projectileCenterX ? -1 : 1;
                bool killedBefore = target.IsDead;
                PlayerDamageCommitResult result = players.TryCommitAuthoritativeNpcProjectileDamage(
                    tick,
                    sourceNpc,
                    projectile.Handle,
                    targetHandle,
                    damage,
                    hitDirection,
                    immunityChannel,
                    out PlayerStateSnapshot committed);
                if (result == PlayerDamageCommitResult.Rejected)
                    continue;

                if (result == PlayerDamageCommitResult.AvoidedByGodMode)
                {
                    HostileGodModeAvoidances++;
                    // Creative god mode returns before vanilla Hurt mutates immunity. This is presentation-only
                    // throttling so a projectile overlapping for many ticks does not flood packet 119.
                    MarkPlayerProjectileCooldown(projectile.Handle, targetHandle, tick);
                    continue;
                }

                HostileCommittedHits++;
                if (!killedBefore && committed.IsDead)
                    HostileKills++;

                // Projectile.Damage_EVP does not generically decrement penetrate on player contact. Only a small
                // explicit type set does so; none is admitted here until those per-type side effects are modeled.
            }
        }
    }


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
                !VanillaProjectileCombatFacts.TryGetDamageClass(projectile.Type, out _) ||
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

                int meleeCritRoll = VanillaProjectileCombatFacts.UsesMeleePvpCrit(projectile.Type)
                    ? random.Next(1, 101)
                    : 100;
                int damageVariation = random.Next(-15, 16);
                if (!VanillaProjectileCombatFacts.TryResolvePvpHit(
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

    private bool IsPlayerOnProjectileCooldown(ProjectileHandle projectile, PlayerHandle target, long tick)
    {
        if (!projectile.IsAssigned || !target.IsAssigned)
            return true;
        int index = checked(projectile.Slot * PlayerSlotCount + target.Slot.Value);
        if (lastProjectileHitGeneration[index] != projectile.Generation ||
            lastTargetGeneration[index] != target.Generation)
        {
            return false;
        }
        long previous = lastProjectilePlayerHitTick[index];
        return previous != long.MinValue && tick - previous < VanillaProjectileCombatFacts.PvpPlayerImmunityTicks;
    }

    private void MarkPlayerProjectileCooldown(ProjectileHandle projectile, PlayerHandle target, long tick)
    {
        int index = checked(projectile.Slot * PlayerSlotCount + target.Slot.Value);
        lastProjectileHitGeneration[index] = projectile.Generation;
        lastTargetGeneration[index] = target.Generation;
        lastProjectilePlayerHitTick[index] = tick;
    }

    private static bool IsEligible(in ProjectileSnapshot projectile, out VanillaProjectileDefinition definition)
    {
        if (!projectile.IsActive || projectile.Damage <= 0 || !VanillaProjectileOwnership.IsPlayerOwned(projectile.Spawner) ||
            VanillaProjectileFacts.IsHostile(projectile.Type) ||
            !VanillaProjectileDefinitionCatalog.TryGet(projectile.Type, out definition) ||
            !VanillaProjectileBehaviorProfileCatalog.TryGet(projectile.Type, out VanillaProjectileBehaviorProfile profile) ||
            !profile.BehaviorImplemented ||
            !VanillaProjectileNpcCombatFacts.TryGetInitialPenetration(projectile.Type, out _) ||
            !VanillaProjectileCombatFacts.TryGetDamageClass(projectile.Type, out _))
        {
            definition = default;
            return false;
        }
        return profile.Family is VanillaProjectileBehaviorFamily.BasicArrow or
            VanillaProjectileBehaviorFamily.Thrown or
            VanillaProjectileBehaviorFamily.Boomerang or
            VanillaProjectileBehaviorFamily.Bomb or
            VanillaProjectileBehaviorFamily.ControlledMagicMissile;
    }

    private static bool Intersects(
        in ProjectileSnapshot projectile,
        in VanillaProjectileDefinition definition,
        RuntimePlayerMember player)
    {
        float left = projectile.PositionX + definition.CollisionOffsetX;
        float top = projectile.PositionY + definition.CollisionOffsetY;
        float right = left + definition.CollisionWidth;
        float bottom = top + definition.CollisionHeight;
        float playerRight = player.PositionX + PlayerAuthority.VanillaBasePlayerWidth;
        float playerBottom = player.PositionY + PlayerAuthority.VanillaBasePlayerHeight;
        return left < playerRight && right > player.PositionX && top < playerBottom && bottom > player.PositionY;
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
