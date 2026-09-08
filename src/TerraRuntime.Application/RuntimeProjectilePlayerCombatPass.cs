using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

/// <summary>
/// Post-simulation trusted-projectile/PvP pass. It never consumes packet-117 damage. Only exact generations already
/// marked CombatTrusted can reach player HP, so speed/damage/AI are the server-simulated values from the projectile
/// store. The admitted source-backed slice also owns Projectile.playerImmune[target] semantics generation-safely.
/// </summary>
internal sealed partial class RuntimeProjectilePlayerCombatPass
{
    private const int PlayerSlotCount = byte.MaxValue + 1;
    private readonly RuntimeProjectileStore projectiles;
    private readonly RuntimeNpcStore npcs;
    private readonly PlayerAuthority players;
    private readonly ServerPlayerAuthority? serverPlayers;
    private readonly Func<long> tickProvider;
    private readonly Random random;
    private readonly ProjectileSnapshot[] projectileBuffer;
    private readonly long[] lastProjectilePlayerHitTick;
    private readonly ProjectileGeneration[] lastProjectileHitGeneration;
    private readonly PlayerSessionGeneration[] lastTargetGeneration;
    private readonly RuntimeCultistLightningArcTrailRegistry cultistLightningArcTrails;
    private readonly PlayerStateSnapshot[] serverPlayerBuffer = new PlayerStateSnapshot[byte.MaxValue + 1];
    private readonly PlayerStateSnapshot[] pvpTargetBuffer = new PlayerStateSnapshot[byte.MaxValue + 1];
    private readonly bool expertMode;
    private readonly bool masterMode;

    public RuntimeProjectilePlayerCombatPass(
        RuntimeProjectileStore projectiles,
        RuntimeNpcStore npcs,
        PlayerAuthority players,
        Func<long> tickProvider,
        Random? random = null,
        RuntimeCultistLightningArcTrailRegistry? cultistLightningArcTrails = null,
        ServerPlayerAuthority? serverPlayers = null,
        bool expertMode = false,
        bool masterMode = false)
    {
        this.projectiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
        this.npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.serverPlayers = serverPlayers;
        if (masterMode && !expertMode)
            throw new ArgumentException("Master mode requires expert-mode player damage semantics.", nameof(masterMode));
        this.expertMode = expertMode;
        this.masterMode = masterMode;
        this.tickProvider = tickProvider ?? throw new ArgumentNullException(nameof(tickProvider));
        this.random = random ?? Random.Shared;
        this.cultistLightningArcTrails = cultistLightningArcTrails ??
            new RuntimeCultistLightningArcTrailRegistry(projectiles.Capacity);
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
    public long PublishedPvpBuffs { get; private set; }
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
                !TryResolveTrustedPvpOwner(
                    trustedOwner,
                    projectile.Spawner,
                    out PlayerStateSnapshot owner,
                    out VanillaPlayerCombatSnapshot ownerCombat))
            {
                continue;
            }

            bool ended = false;
            int targetCount = players.CopyCombatTargets(pvpTargetBuffer);
            for (int targetIndex = 0; targetIndex < targetCount; targetIndex++)
            {
                PlayerStateSnapshot target = pvpTargetBuffer[targetIndex];
                if (target.Player.Slot.Value == projectile.Spawner || !target.Hostile || target.IsDead || !target.HasHealth || target.Life <= 0 ||
                    (owner.Team != 0 && owner.Team == target.Team) ||
                    IsPlayerOnProjectileCooldown(projectile.Handle, target.Player, tick) ||
                    !Intersects(in projectile, in definition, target.PositionX, target.PositionY))
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

                int direction = projectile.VelocityX > 0.01f ? 1 : projectile.VelocityX < -0.01f ? -1 : 0;
                bool killedBefore = target.IsDead;
                PlayerDamageCommitResult commitResult = players.TryCommitAuthoritativePvpDamageFromSnapshot(
                        tick,
                        in owner,
                        target.Player,
                        DamageSource.FromPlayerProjectile(trustedOwner, projectile.Handle),
                        hit.Damage,
                        hit.Critical,
                        direction,
                        out PlayerStateSnapshot committed);
                if (commitResult == PlayerDamageCommitResult.Rejected)
                    continue;

                MarkPlayerProjectileCooldown(projectile.Handle, target.Player, tick);
                // Vanilla Projectile.Damage_PVP calls StatusPvP before Player.Hurt. Creative god mode returns
                // from Hurt afterwards, so a source-backed PvP status proc still occurs on an avoided god-mode hit.
                TryApplyTypeSpecificPvpStatus(projectile.Type, target.Player);
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

    private void TryApplyTypeSpecificPvpStatus(ProjectileTypeId projectileType, PlayerHandle target)
    {
        if (!VanillaProjectilePvpStatusFacts1458.TryGetTypeSpecificRule(projectileType, out var rule) ||
            !rule.IsValid ||
            random.Next(rule.ChanceDenominator) != 0)
        {
            return;
        }

        if (players.TryPublishAuthoritativePvpBuff(target, rule.BuffType, rule.DurationTicks))
            PublishedPvpBuffs++;
    }

    private bool TryResolveTrustedPvpOwner(
        PlayerHandle owner,
        byte spawner,
        out PlayerStateSnapshot snapshot,
        out VanillaPlayerCombatSnapshot combat)
    {
        snapshot = default;
        combat = default;
        if (!owner.IsAssigned || owner.Slot.Value != spawner)
            return false;

        if (players.TryCapture(owner, out snapshot) && snapshot.Player == owner)
        {
            return snapshot.Hostile && !snapshot.IsDead &&
                players.TryCaptureCombatSnapshot(owner, out combat);
        }

        if (serverPlayers is not null && serverPlayers.TryGet(owner, out snapshot) && snapshot.Player == owner)
        {
            if (!snapshot.Hostile || snapshot.IsDead)
                return false;
            return serverPlayers.TryCaptureCombatSnapshot(owner, out combat);
        }

        snapshot = default;
        return false;
    }

    private void TickServerHostilePve(ReadOnlySpan<ProjectileSnapshot> activeProjectiles, long tick)
    {
        for (int i = 0; i < activeProjectiles.Length; i++)
        {
            ProjectileSnapshot projectile = activeProjectiles[i];
            if (!projectile.IsActive || projectile.Damage <= 0 ||
                !VanillaProjectileFacts.IsHostile(projectile.Type) ||
                !projectiles.TryGetServerNpcSource(projectile.Handle, out NpcHandle sourceNpc) ||
                !projectiles.TryGetLifecycle(projectile.Handle, out ProjectileLifecycleState lifecycle) ||
                !CanDealHostileProjectileDamage(projectile.Type, in lifecycle) ||
                !TerraRuntime.Gameplay.Projectiles.VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition) ||
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
                    !IntersectsHostile(in projectile, in definition, in lifecycle, sourceNpc, target.PositionX, target.PositionY))
                {
                    continue;
                }

                int damage = VanillaIncomingPlayerDamageFacts1458.ResolveHostileProjectileDamage(
                    projectile.Damage,
                    random.Next(-15, 16));
                if (damage <= 0)
                    continue;

                float projectileCenterX = GetHostileProjectileCenterX(in projectile, in definition, in lifecycle);
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

            if (serverPlayers is null)
                continue;
            int serverCount = serverPlayers.CopySnapshots(serverPlayerBuffer);
            for (int targetIndex = 0; targetIndex < serverCount; targetIndex++)
            {
                PlayerStateSnapshot target = serverPlayerBuffer[targetIndex];
                PlayerHandle targetHandle = target.Player;
                if (target.IsDead || !target.HasHealth || target.Life <= 0 ||
                    (target.GodMode && IsPlayerOnProjectileCooldown(projectile.Handle, targetHandle, tick)) ||
                    !IntersectsHostile(in projectile, in definition, in lifecycle, sourceNpc, target.PositionX, target.PositionY))
                {
                    continue;
                }

                int damage = VanillaIncomingPlayerDamageFacts1458.ResolveHostileProjectileDamage(
                    projectile.Damage,
                    random.Next(-15, 16));
                if (damage <= 0)
                    continue;

                float projectileCenterX = GetHostileProjectileCenterX(in projectile, in definition, in lifecycle);
                float targetCenterX = target.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
                int hitDirection = targetCenterX < projectileCenterX ? -1 : 1;
                bool killedBefore = target.IsDead;
                PlayerDamageCommitResult result = serverPlayers.TryCommitAuthoritativeNpcProjectileDamage(
                    tick,
                    sourceNpc,
                    projectile.Handle,
                    projectile.Type,
                    targetHandle,
                    damage,
                    hitDirection,
                    immunityChannel,
                    expertMode,
                    masterMode,
                    out PlayerStateSnapshot committed);
                if (result == PlayerDamageCommitResult.Rejected)
                    continue;
                if (result == PlayerDamageCommitResult.AvoidedByGodMode)
                {
                    HostileGodModeAvoidances++;
                    MarkPlayerProjectileCooldown(projectile.Handle, targetHandle, tick);
                    continue;
                }

                HostileCommittedHits++;
                if (!killedBefore && committed.IsDead)
                    HostileKills++;
            }
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
        return previous != long.MinValue && tick - previous < VanillaCombatFacts.PvpPlayerImmunityTicks;
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
            !TerraRuntime.Gameplay.Projectiles.VanillaDefinitionCatalog.TryGet(projectile.Type, out definition) ||
            !VanillaProjectileBehaviorProfileCatalog.TryGet(projectile.Type, out VanillaProjectileBehaviorProfile profile) ||
            !profile.BehaviorImplemented ||
            !VanillaProjectileNpcCombatFacts.TryGetInitialPenetration(projectile.Type, out _) ||
            !VanillaCombatFacts.TryGetDamageClass(projectile.Type, out _))
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
        float playerX,
        float playerY)
    {
        float left = projectile.PositionX + definition.CollisionOffsetX;
        float top = projectile.PositionY + definition.CollisionOffsetY;
        float right = left + definition.CollisionWidth;
        float bottom = top + definition.CollisionHeight;
        float playerRight = playerX + PlayerAuthority.VanillaBasePlayerWidth;
        float playerBottom = playerY + PlayerAuthority.VanillaBasePlayerHeight;
        return left < playerRight && right > playerX && top < playerBottom && bottom > playerY;
    }



}
