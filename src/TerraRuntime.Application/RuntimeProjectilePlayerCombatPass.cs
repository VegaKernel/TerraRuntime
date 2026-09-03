using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime;

/// <summary>
/// Post-simulation trusted-projectile/PvP pass. It never consumes packet-117 damage. Only exact generations already
/// marked CombatTrusted can reach player HP, so speed/damage/AI are the server-simulated values from the projectile
/// store. Unsupported projectile families and armored/unmodeled targets fail closed out of this strict slice.
/// </summary>
internal sealed class RuntimeProjectilePlayerCombatPass
{
    private readonly RuntimeProjectileStore projectiles;
    private readonly PlayerAuthority players;
    private readonly Func<long> tickProvider;
    private readonly Random random;
    private const int PlayerSlotCount = byte.MaxValue + 1;
    private readonly ProjectileGeneration[] projectileHitGenerations;
    private readonly long[] lastProjectilePlayerHitTick;
    private readonly PlayerSessionGeneration[] lastProjectilePlayerHitGeneration;

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
        projectileHitGenerations = new ProjectileGeneration[projectiles.Capacity];
        lastProjectilePlayerHitTick = new long[checked(projectiles.Capacity * PlayerSlotCount)];
        lastProjectilePlayerHitGeneration = new PlayerSessionGeneration[lastProjectilePlayerHitTick.Length];
        Array.Fill(lastProjectilePlayerHitTick, long.MinValue);
    }

    public long CommittedHits { get; private set; }
    public long Kills { get; private set; }
    public long ConsumedProjectiles { get; private set; }
    public long DodgedHits { get; private set; }
    public long ImmuneSkips { get; private set; }
    public long AppliedStatusEffects { get; private set; }
    public long SpawnedChildProjectiles { get; private set; }

    public void Tick()
    {
        int physicalSlots = Math.Min(projectiles.Capacity, RuntimeProjectileStore.VanillaPhysicalSlotCount);
        for (ushort slot = 0; slot < physicalSlots; slot++)
            TickProjectile(slot);
    }

    public void TickProjectile(ushort projectileSlot)
    {
        if (!projectiles.TryGetActive(projectileSlot, out ProjectileSnapshot projectile))
            return;

        ProcessProjectile(in projectile, tickProvider());
    }

    private void ProcessProjectile(in ProjectileSnapshot initialProjectile, long tick)
    {
        ProjectileSnapshot projectile = initialProjectile;
        if (!projectiles.IsCombatTrusted(projectile.Handle) ||
            !projectiles.TryGetLifecycle(projectile.Handle, out ProjectileLifecycleState lifecycle) ||
            lifecycle.Reflected ||
            !projectiles.TryGetCombatTrustedOwner(projectile.Handle, out PlayerHandle trustedOwner) ||
            !IsEligible(in projectile, out VanillaProjectileDefinition definition) ||
            !TryResolveProjectileHitRow(projectile.Handle, out int projectileHitRow) ||
            !players.TryGet(trustedOwner, out RuntimePlayerMember? owner) ||
            owner.Connection.Player != trustedOwner ||
            owner.Slot.Value != projectile.Spawner ||
            !owner.Hostile || owner.IsDead ||
            !players.TryCaptureCombatSnapshot(trustedOwner, out VanillaPlayerCombatSnapshot attackerCombat))
        {
            return;
        }

        foreach (RuntimePlayerMember target in players.Members)
        {
            if (target.Slot.Value == projectile.Spawner || !target.Hostile || target.IsDead || !target.HasHealth || target.Life <= 0 ||
                (owner.Team != 0 && owner.Team == target.Team) ||
                IsProjectilePlayerOnCooldown(projectileHitRow, target.Connection.Player, tick) ||
                !Intersects(in projectile, in definition, target))
            {
                continue;
            }

            // Damage_PVP rejects general player immunity before StatusPvP/TryDoingOnHitEffects and before
            // penetration/playerImmune side effects. Preflight the target combat snapshot for the same reason:
            // a fail-closed target must not receive a status mutation from a hit that cannot be committed.
            if (players.IsAuthoritativePvpImmune(target.Connection.Player, tick))
            {
                ImmuneSkips++;
                continue;
            }
            if (!players.TryCaptureCombatSnapshot(target.Connection.Player, out _))
                continue;

            int direction = projectile.VelocityX > 0.01f ? 1 : projectile.VelocityX < -0.01f ? -1 : 0;
            // Projectile.Damage_PVP calls Main.DamageVar before StatusPvP. Keep the roll server-owned instead
            // of treating packet-117 damage as the already-varied result. Luck-skewed variance remains a later
            // parity slice; the existing strict projectile path owns the vanilla +/-15% envelope here.
            int variedDamage = Math.Max(1, (int)Math.Round(
                projectile.Damage * (1f + random.Next(-15, 16) * 0.01f)));

            // StatusPvP runs before Player.Hurt. For the admitted SetDefaults classes the source order is
            // meleeEnchant -> Frost set -> magmaStone -> type-specific status. TryDoingOnHitEffects then runs
            // before Hurt, so Hallowed Protection may arm even when Shimmer/ordinary dodge later returns zero.
            if (VanillaProjectilePvpCombatFacts.CanCarryMeleeEnchantStatus(projectile.Type) &&
                VanillaProjectilePvpCombatFacts.TryRollMeleeEnchantStatus(
                    attackerCombat.MeleeEnchant,
                    random,
                    out VanillaProjectilePvpStatusEffect enchantStatus) &&
                enchantStatus.IsPresent)
            {
                if (!players.TryGrantAuthoritativePvpStatus(
                        target.Connection.Player,
                        enchantStatus.Buff,
                        enchantStatus.DurationTicks))
                {
                    continue;
                }
                AppliedStatusEffects++;
            }

            if (VanillaProjectilePvpCombatFacts.CanCarryFrostBurnStatus(projectile.Type) &&
                VanillaProjectilePvpCombatFacts.TryRollFrostBurnStatus(
                    attackerCombat.FrostBurn,
                    random,
                    out VanillaProjectilePvpStatusEffect frostStatus) &&
                frostStatus.IsPresent)
            {
                if (!players.TryGrantAuthoritativePvpStatus(
                        target.Connection.Player,
                        frostStatus.Buff,
                        frostStatus.DurationTicks))
                {
                    continue;
                }
                AppliedStatusEffects++;
            }

            if (VanillaProjectilePvpCombatFacts.TryRollMagmaStoneStatus(
                    projectile.Type,
                    attackerCombat.MagmaStone,
                    random,
                    out VanillaProjectilePvpStatusEffect magmaStatus) &&
                magmaStatus.IsPresent)
            {
                if (!players.TryGrantAuthoritativePvpStatus(
                        target.Connection.Player,
                        magmaStatus.Buff,
                        magmaStatus.DurationTicks))
                {
                    continue;
                }
                AppliedStatusEffects++;
            }

            if (VanillaProjectilePvpCombatFacts.TryRollAdmittedStatus(
                    projectile.Type,
                    random,
                    out VanillaProjectilePvpStatusEffect status) &&
                status.IsPresent)
            {
                if (!players.TryGrantAuthoritativePvpStatus(
                        target.Connection.Player,
                        status.Buff,
                        status.DurationTicks))
                {
                    continue;
                }
                AppliedStatusEffects++;
            }

            // Projectile.TryDoingOnHitEffects explicitly excludes type 729, so a Super Star Slash can carry
            // ranged/Frost StatusPvP but must not arm the attacker's Hallowed Protection.
            if (VanillaProjectilePvpCombatFacts.RunsAttackerOnHitEffects(projectile.Type) &&
                !players.TryGrantHallowedProtectionOnHit(owner.Connection.Player, in attackerCombat, tick))
            {
                continue;
            }

            AuthoritativePvpDamageCommitResult result = players.CommitAuthoritativePvpDamage(
                tick,
                owner.Connection.Player,
                target.Connection.Player,
                DamageSource.FromPlayerProjectile(owner.Connection.Player, projectile.Handle),
                variedDamage,
                critical: false,
                direction,
                VanillaProjectilePvpCombatFacts.IsDamageDodgeable(projectile.Type, projectile.Damage),
                allowShimmerDodge: !VanillaProjectilePvpCombatFacts.CanHitPastShimmer(projectile.Type),
                out PlayerStateSnapshot committed);
            if (result == AuthoritativePvpDamageCommitResult.Rejected)
                continue;
            if (result == AuthoritativePvpDamageCommitResult.Immune)
            {
                ImmuneSkips++;
                continue;
            }

            // Projectile.Damage_PVP spawns Flask of Party child 289 after Player.Hurt returns, regardless of
            // whether Hurt dealt damage or resolved to a dodge, and before playerImmune/penetration side effects.
            if (VanillaProjectilePvpCombatFacts.ShouldSpawnConfettiMeleeChild(projectile.Type, attackerCombat.MeleeEnchant) &&
                RuntimeProjectileChildSpawn1458.TrySpawnConfettiMelee(
                    projectiles,
                    owner.Connection.Player,
                    target.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f,
                    target.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f,
                    target.VelocityX,
                    target.VelocityY,
                    out _))
            {
                SpawnedChildProjectiles++;
            }

            // Damage_PVP writes projectile.playerImmune[target] = 40 and consumes penetration even when Hurt
            // resolves to a dodge. Global player immunity, by contrast, is checked before collision and does not.
            MarkProjectilePlayerCooldown(projectileHitRow, target.Connection.Player, tick);
            if (result == AuthoritativePvpDamageCommitResult.Dodged)
                DodgedHits++;
            else
            {
                CommittedHits++;
                if (result == AuthoritativePvpDamageCommitResult.Killed || committed.IsDead)
                    Kills++;
            }

            if (!projectiles.TryConsumeCombatHitPenetration(projectile.Handle, out bool despawned, out ProjectileSnapshot current))
                break;
            if (despawned)
            {
                ConsumedProjectiles++;
                break;
            }
            projectile = current;
        }
    }

    private bool TryResolveProjectileHitRow(ProjectileHandle handle, out int rowStart)
    {
        rowStart = -1;
        if (!handle.IsAssigned || handle.Slot >= projectiles.Capacity)
            return false;

        int slot = handle.Slot;
        if (projectileHitGenerations[slot] != handle.Generation)
        {
            projectileHitGenerations[slot] = handle.Generation;
            int resetStart = slot * PlayerSlotCount;
            Array.Fill(lastProjectilePlayerHitTick, long.MinValue, resetStart, PlayerSlotCount);
            Array.Clear(lastProjectilePlayerHitGeneration, resetStart, PlayerSlotCount);
        }

        rowStart = slot * PlayerSlotCount;
        return true;
    }

    private bool IsProjectilePlayerOnCooldown(int rowStart, PlayerHandle target, long tick)
    {
        int index = rowStart + target.Slot.Value;
        if (lastProjectilePlayerHitGeneration[index] != target.Generation)
            return false;
        long previous = lastProjectilePlayerHitTick[index];
        return previous != long.MinValue &&
            tick - previous < VanillaProjectilePvpCombatFacts.PerProjectilePlayerImmunityTicks;
    }

    private void MarkProjectilePlayerCooldown(int rowStart, PlayerHandle target, long tick)
    {
        int index = rowStart + target.Slot.Value;
        lastProjectilePlayerHitGeneration[index] = target.Generation;
        lastProjectilePlayerHitTick[index] = tick;
    }

    private static bool IsEligible(in ProjectileSnapshot projectile, out VanillaProjectileDefinition definition)
    {
        if (!projectile.IsActive || projectile.Damage <= 0 || !VanillaProjectileOwnership.IsPlayerOwned(projectile.Spawner) ||
            VanillaProjectileFacts.IsHostile(projectile.Type) ||
            !VanillaProjectileDefinitionCatalog.TryGet(projectile.Type, out definition) ||
            !VanillaProjectileBehaviorProfileCatalog.TryGet(projectile.Type, out VanillaProjectileBehaviorProfile profile) ||
            !profile.BehaviorImplemented ||
            !VanillaProjectileNpcCombatFacts.TryGetInitialPenetration(projectile.Type, out _))
        {
            definition = default;
            return false;
        }
        return profile.Family is VanillaProjectileBehaviorFamily.BasicArrow or
            VanillaProjectileBehaviorFamily.Thrown or
            VanillaProjectileBehaviorFamily.Boomerang or
            VanillaProjectileBehaviorFamily.SuperStar;
    }

    private static bool Intersects(
        in ProjectileSnapshot projectile,
        in VanillaProjectileDefinition definition,
        RuntimePlayerMember player)
    {
        if (!VanillaProjectilePvpCombatFacts.CanUseDefinitionAabb(projectile.Type, projectile.Ai.Ai0))
            return false;

        float left = projectile.PositionX + definition.CollisionOffsetX;
        float top = projectile.PositionY + definition.CollisionOffsetY;
        float right = left + definition.CollisionWidth;
        float bottom = top + definition.CollisionHeight;
        float playerRight = player.PositionX + PlayerAuthority.VanillaBasePlayerWidth;
        float playerBottom = player.PositionY + PlayerAuthority.VanillaBasePlayerHeight;
        return left < playerRight && right > player.PositionX && top < playerBottom && bottom > player.PositionY;
    }
}
