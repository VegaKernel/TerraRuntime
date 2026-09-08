using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Application;

/// <summary>
/// Deterministic post-simulation projectile/NPC collision pass for the source-backed friendly projectile slice.
/// Ordering is physical projectile slot then physical NPC slot. Damage commits before penetration side effects, and
/// only a committed hit consumes penetration. Unsupported projectile types never reach world mutation.
/// </summary>
internal sealed class RuntimeProjectileNpcCombatPass
{
    private readonly RuntimeProjectileStore projectiles;
    private readonly RuntimeNpcStore npcs;
    private const int PlayerSlotCount = byte.MaxValue + 1;
    private readonly RuntimeNpcNetworkCombatPipeline combat;
    private readonly PlayerAuthority players;
    private readonly ServerPlayerAuthority? serverPlayers;
    private readonly Func<long> tickProvider;
    private readonly Random random;
    private readonly ProjectileSnapshot[] projectileBuffer;
    private readonly NpcSnapshot[] npcBuffer;
    private readonly PlayerSessionGeneration[] ownerGenerations = new PlayerSessionGeneration[PlayerSlotCount];
    private readonly long[] lastOwnerNpcHitTick;
    private readonly NpcGeneration[] lastOwnerNpcHitGeneration;
    private readonly RuntimeProjectileNpcLocalImmunityRegistry localNpcImmunity;

    public RuntimeProjectileNpcCombatPass(
        RuntimeProjectileStore projectiles,
        RuntimeNpcStore npcs,
        RuntimeNpcNetworkCombatPipeline combat,
        PlayerAuthority players,
        Func<long> tickProvider,
        Random? random = null,
        ServerPlayerAuthority? serverPlayers = null,
        RuntimeProjectileNpcLocalImmunityRegistry? localNpcImmunity = null)
    {
        this.projectiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
        this.npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        this.combat = combat ?? throw new ArgumentNullException(nameof(combat));
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.serverPlayers = serverPlayers;
        this.tickProvider = tickProvider ?? throw new ArgumentNullException(nameof(tickProvider));
        this.random = random ?? Random.Shared;
        projectileBuffer = new ProjectileSnapshot[projectiles.Capacity];
        npcBuffer = new NpcSnapshot[npcs.Capacity];
        lastOwnerNpcHitTick = new long[checked(PlayerSlotCount * npcs.Capacity)];
        lastOwnerNpcHitGeneration = new NpcGeneration[lastOwnerNpcHitTick.Length];
        this.localNpcImmunity = localNpcImmunity ??
            new RuntimeProjectileNpcLocalImmunityRegistry(projectiles.Capacity, npcs.Capacity);
        Array.Fill(lastOwnerNpcHitTick, long.MinValue);
    }

    public long CommittedHits { get; private set; }
    public long Kills { get; private set; }
    public long ConsumedProjectiles { get; private set; }

    public void Tick()
    {
        long tick = tickProvider();
        int projectileCount = projectiles.CopyActive(projectileBuffer);
        int npcCount = npcs.CopyActive(npcBuffer);

        for (int projectileIndex = 0; projectileIndex < projectileCount; projectileIndex++)
        {
            ProjectileSnapshot projectile = projectileBuffer[projectileIndex];
            if (!projectiles.IsCombatTrusted(projectile.Handle) ||
                !IsEligible(in projectile, out VanillaProjectileDefinition projectileDefinition, out _))
            {
                continue;
            }

            if (!projectiles.TryGetCombatTrustedOwner(projectile.Handle, out PlayerHandle trustedOwner) ||
                !TryResolveOwnerRow(trustedOwner, out int ownerRow) ||
                !TryCaptureOwnerCombatSnapshot(trustedOwner, out VanillaPlayerCombatSnapshot ownerCombat))
            {
                continue;
            }
            bool sharedOwnerImmunity = VanillaProjectileNpcCombatFacts.UsesSharedOwnerNpcImmunity(projectile.Type);
            bool localImmunity = VanillaProjectileNpcCombatFacts.TryGetLocalNpcImmunityCooldown(projectile.Type, out int localImmunityCooldown);

            bool projectileEnded = false;
            for (int npcIndex = 0; npcIndex < npcCount; npcIndex++)
            {
                NpcSnapshot target = npcBuffer[npcIndex];
                if (!IsEligibleTarget(in target, out VanillaNpcHitboxSize npcHitbox) ||
                    !Intersects(in projectile, in projectileDefinition, in target, in npcHitbox) ||
                    (sharedOwnerImmunity && IsOwnerNpcOnCooldown(ownerRow, target.Handle, tick)) ||
                    (localImmunity && localNpcImmunity.IsImmune(projectile.Handle, target.Handle, tick, localImmunityCooldown)))
                {
                    continue;
                }

                int hitDirection = projectile.VelocityX > 0.01f ? 1 : projectile.VelocityX < -0.01f ? -1 : 0;
                int critRoll = random.Next(1, 101);
                int damageVariation = random.Next(-15, 16);
                if (!VanillaCombatFacts.TryResolvePveHit(
                        projectile.Type,
                        projectile.Damage,
                        in ownerCombat,
                        critRoll,
                        damageVariation,
                        out VanillaProjectileResolvedHit hit))
                {
                    continue;
                }
                RuntimeProjectileNpcDamageResult result = combat.TryStrikeProjectile(
                    in projectile, target.Handle, hitDirection, hit.Damage, hit.ArmorPenetration, hit.Critical);
                if (result == RuntimeProjectileNpcDamageResult.Rejected)
                    continue;

                if (sharedOwnerImmunity)
                    MarkOwnerNpcCooldown(ownerRow, target.Handle, tick);
                if (localImmunity)
                    localNpcImmunity.MarkHit(projectile.Handle, target.Handle, tick);
                if (!TryApplyControlledMagicPostHitTargetReset(ref projectile))
                    throw new InvalidOperationException("Committed controlled-magic NPC hit could not apply its source-backed target reset.");
                CommittedHits++;
                if (result == RuntimeProjectileNpcDamageResult.Killed)
                    Kills++;

                if (!projectiles.TryConsumeNpcHitPenetration(projectile.Handle, out bool despawned, out ProjectileSnapshot current))
                    break;
                if (despawned)
                {
                    ConsumedProjectiles++;
                    projectileEnded = true;
                    break;
                }
                projectile = current;
            }

            if (projectileEnded)
                continue;
        }
    }

    public void TickExplosions(ReadOnlySpan<RuntimeProjectileExplosionEvent> explosions)
    {
        if (explosions.IsEmpty)
            return;

        long tick = tickProvider();
        int npcCount = npcs.CopyActive(npcBuffer);
        for (int explosionIndex = 0; explosionIndex < explosions.Length; explosionIndex++)
        {
            RuntimeProjectileExplosionEvent explosion = explosions[explosionIndex];
            ProjectileSnapshot projectile = explosion.Projectile;
            if (!projectile.IsActive || projectile.Damage <= 0 ||
                !VanillaProjectileOwnership.IsPlayerOwned(projectile.Spawner) ||
                VanillaProjectileFacts.IsHostile(projectile.Type) ||
                !VanillaProjectileExplosionFacts.TryGetOnKillExplosion(projectile.Type, out _) ||
                !VanillaProjectileNpcCombatFacts.TryGetInitialPenetration(projectile.Type, out _) ||
                !TryResolveOwnerRow(explosion.TrustedOwner, out int ownerRow) ||
                !TryCaptureOwnerCombatSnapshot(explosion.TrustedOwner, out VanillaPlayerCombatSnapshot ownerCombat))
            {
                continue;
            }

            bool sharedOwnerImmunity = VanillaProjectileNpcCombatFacts.UsesSharedOwnerNpcImmunity(projectile.Type);
            bool localImmunity = VanillaProjectileNpcCombatFacts.TryGetLocalNpcImmunityCooldown(projectile.Type, out int localImmunityCooldown);
            for (int npcIndex = 0; npcIndex < npcCount; npcIndex++)
            {
                NpcSnapshot target = npcBuffer[npcIndex];
                if (!IsEligibleTarget(in target, out VanillaNpcHitboxSize npcHitbox) ||
                    !Intersects(in explosion, in target, in npcHitbox) ||
                    (sharedOwnerImmunity && IsOwnerNpcOnCooldown(ownerRow, target.Handle, tick)) ||
                    (localImmunity && localNpcImmunity.IsImmune(projectile.Handle, target.Handle, tick, localImmunityCooldown)))
                {
                    continue;
                }

                int hitDirection = ResolveExplosionDirection(in explosion, in target, in npcHitbox);
                int critRoll = random.Next(1, 101);
                int damageVariation = random.Next(-15, 16);
                if (!VanillaCombatFacts.TryResolvePveHit(
                        projectile.Type,
                        projectile.Damage,
                        in ownerCombat,
                        critRoll,
                        damageVariation,
                        out VanillaProjectileResolvedHit hit))
                {
                    continue;
                }

                RuntimeProjectileNpcDamageResult result = combat.TryStrikeProjectile(
                    in projectile, target.Handle, hitDirection, hit.Damage, hit.ArmorPenetration, hit.Critical);
                if (result == RuntimeProjectileNpcDamageResult.Rejected)
                    continue;

                if (sharedOwnerImmunity)
                    MarkOwnerNpcCooldown(ownerRow, target.Handle, tick);
                if (localImmunity)
                    localNpcImmunity.MarkHit(projectile.Handle, target.Handle, tick);
                CommittedHits++;
                if (result == RuntimeProjectileNpcDamageResult.Killed)
                    Kills++;
            }
        }
    }

    private static bool IsEligible(
        in ProjectileSnapshot projectile,
        out VanillaProjectileDefinition definition,
        out VanillaProjectileBehaviorProfile profile)
    {
        if (!projectile.IsActive || projectile.Damage <= 0 || !VanillaProjectileOwnership.IsPlayerOwned(projectile.Spawner) ||
            VanillaProjectileFacts.IsHostile(projectile.Type) ||
            !TerraRuntime.Gameplay.Projectiles.VanillaDefinitionCatalog.TryGet(projectile.Type, out definition) ||
            !VanillaProjectileBehaviorProfileCatalog.TryGet(projectile.Type, out profile) ||
            !profile.BehaviorImplemented ||
            !VanillaProjectileNpcCombatFacts.TryGetInitialPenetration(projectile.Type, out _))
        {
            definition = default;
            profile = default;
            return false;
        }

        return profile.Family is VanillaProjectileBehaviorFamily.BasicArrow or
            VanillaProjectileBehaviorFamily.Thrown or
            VanillaProjectileBehaviorFamily.Boomerang or
            VanillaProjectileBehaviorFamily.Bomb or
            VanillaProjectileBehaviorFamily.ControlledMagicMissile;
    }

    private static bool IsEligibleTarget(in NpcSnapshot target, out VanillaNpcHitboxSize hitbox)
    {
        if (!target.IsActive || target.Simulation.Life <= 0 || target.Simulation.DontTakeDamage ||
            !VanillaNpcDefinitionCatalog.TryGet(target.TypeIdentity, target.NetIdentity, out VanillaNpcDefinition definition) ||
            definition.Role == NpcArchetypeRole.Town ||
            !definition.TryResolveHitbox(target.Simulation, out hitbox))
        {
            hitbox = default;
            return false;
        }
        return true;
    }

    private static bool Intersects(
        in ProjectileSnapshot projectile,
        in VanillaProjectileDefinition projectileDefinition,
        in NpcSnapshot npc,
        in VanillaNpcHitboxSize npcHitbox)
    {
        float projectileLeft = projectile.PositionX + projectileDefinition.CollisionOffsetX;
        float projectileTop = projectile.PositionY + projectileDefinition.CollisionOffsetY;
        float projectileRight = projectileLeft + projectileDefinition.CollisionWidth;
        float projectileBottom = projectileTop + projectileDefinition.CollisionHeight;
        float npcRight = npc.PositionX + npcHitbox.Width;
        float npcBottom = npc.PositionY + npcHitbox.Height;
        return projectileLeft < npcRight && projectileRight > npc.PositionX &&
               projectileTop < npcBottom && projectileBottom > npc.PositionY;
    }

    private static bool Intersects(
        in RuntimeProjectileExplosionEvent explosion,
        in NpcSnapshot npc,
        in VanillaNpcHitboxSize npcHitbox)
    {
        float right = explosion.Left + explosion.Width;
        float bottom = explosion.Top + explosion.Height;
        float npcRight = npc.PositionX + npcHitbox.Width;
        float npcBottom = npc.PositionY + npcHitbox.Height;
        return explosion.Left < npcRight && right > npc.PositionX &&
               explosion.Top < npcBottom && bottom > npc.PositionY;
    }

    private static int ResolveExplosionDirection(
        in RuntimeProjectileExplosionEvent explosion,
        in NpcSnapshot target,
        in VanillaNpcHitboxSize hitbox)
    {
        if (explosion.Projectile.VelocityX > 0.01f)
            return 1;
        if (explosion.Projectile.VelocityX < -0.01f)
            return -1;
        float targetCenter = target.PositionX + hitbox.Width * 0.5f;
        return targetCenter > explosion.CenterX ? 1 : targetCenter < explosion.CenterX ? -1 : 0;
    }

    private bool TryCaptureOwnerSnapshot(PlayerHandle owner, out PlayerStateSnapshot snapshot)
    {
        if (players.TryCapture(owner, out snapshot) && snapshot.Player == owner)
            return true;
        if (serverPlayers is not null && serverPlayers.TryGet(owner, out snapshot) && snapshot.Player == owner)
            return true;
        snapshot = default;
        return false;
    }

    private bool TryCaptureOwnerCombatSnapshot(PlayerHandle owner, out VanillaPlayerCombatSnapshot snapshot)
    {
        if (players.TryCaptureCombatSnapshot(owner, out snapshot))
            return true;
        if (serverPlayers is not null && serverPlayers.TryGet(owner, out PlayerStateSnapshot player) &&
            player.Player == owner && !player.IsDead)
        {
            // Runtime-owned bot presets currently contain only ordinary clothing/metal armor with no outgoing
            // damage bonuses. Baseline therefore matches the source-backed offensive modifiers used by its bow.
            snapshot = VanillaPlayerCombatSnapshot.Baseline;
            return true;
        }
        snapshot = default;
        return false;
    }

    private bool TryResolveOwnerRow(PlayerHandle trustedOwner, out int ownerRow)
    {
        ownerRow = -1;
        if (!trustedOwner.IsAssigned || !TryCaptureOwnerSnapshot(trustedOwner, out PlayerStateSnapshot currentOwner))
            return false;

        byte spawner = trustedOwner.Slot.Value;
        if (ownerGenerations[spawner] != trustedOwner.Generation)
        {
            ownerGenerations[spawner] = trustedOwner.Generation;
            int rowStart = spawner * npcs.Capacity;
            Array.Fill(lastOwnerNpcHitTick, long.MinValue, rowStart, npcs.Capacity);
            Array.Clear(lastOwnerNpcHitGeneration, rowStart, npcs.Capacity);
        }

        ownerRow = spawner * npcs.Capacity;
        return true;
    }

    private bool IsOwnerNpcOnCooldown(int ownerRow, NpcHandle target, long tick)
    {
        int index = ownerRow + target.Slot;
        if (lastOwnerNpcHitGeneration[index] != target.Generation)
            return false;
        long previous = lastOwnerNpcHitTick[index];
        return previous != long.MinValue &&
            tick - previous < VanillaProjectileNpcCombatFacts.BaselineOwnerNpcHitCooldownTicks;
    }

    private void MarkOwnerNpcCooldown(int ownerRow, NpcHandle target, long tick)
    {
        int index = ownerRow + target.Slot;
        lastOwnerNpcHitGeneration[index] = target.Generation;
        lastOwnerNpcHitTick[index] = tick;
    }


    private bool TryApplyControlledMagicPostHitTargetReset(ref ProjectileSnapshot projectile)
    {
        if (!VanillaProjectileNpcCombatFacts.ShouldResetReleasedControlledMagicTargetAfterNpcHit(
                projectile.Type, projectile.Ai.Ai0) ||
            projectile.Ai.Ai1 == -1f)
        {
            return true;
        }

        var update = new ProjectileStateUpdate(
            projectile.Type,
            projectile.Spawner,
            projectile.PositionX,
            projectile.PositionY,
            projectile.VelocityX,
            projectile.VelocityY,
            new ProjectileAiState(projectile.Ai.Ai0, -1f, projectile.Ai.Ai2),
            projectile.BannerIdToRespondTo,
            projectile.Damage,
            projectile.KnockBack,
            projectile.OriginalDamage);
        if (!projectiles.TryUpdate(projectile.Handle, in update, out ProjectileSnapshot committed))
            return false;

        projectile = committed;
        return true;
    }

}
