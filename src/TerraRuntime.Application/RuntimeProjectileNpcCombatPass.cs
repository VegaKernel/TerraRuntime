using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime;

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
    private readonly Func<long> tickProvider;
    private readonly Random random;
    private readonly NpcSnapshot[] npcBuffer;
    private int cachedNpcCount;
    private readonly PlayerSessionGeneration[] ownerGenerations = new PlayerSessionGeneration[PlayerSlotCount];
    private readonly long[] lastOwnerNpcHitTick;
    private readonly NpcGeneration[] lastOwnerNpcHitGeneration;
    private readonly ProjectileGeneration[] localNpcImmunityProjectileGenerations;
    private readonly NpcGeneration[] localNpcHitGeneration;
    private readonly NpcGeneration[] superStarSlashStaticNpcGeneration;
    private readonly long[] superStarSlashStaticNpcImmuneUntil;

    public RuntimeProjectileNpcCombatPass(
        RuntimeProjectileStore projectiles,
        RuntimeNpcStore npcs,
        RuntimeNpcNetworkCombatPipeline combat,
        PlayerAuthority players,
        Func<long> tickProvider,
        Random? random = null)
    {
        this.projectiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
        this.npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        this.combat = combat ?? throw new ArgumentNullException(nameof(combat));
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.tickProvider = tickProvider ?? throw new ArgumentNullException(nameof(tickProvider));
        this.random = random ?? Random.Shared;
        npcBuffer = new NpcSnapshot[npcs.Capacity];
        lastOwnerNpcHitTick = new long[checked(PlayerSlotCount * npcs.Capacity)];
        lastOwnerNpcHitGeneration = new NpcGeneration[lastOwnerNpcHitTick.Length];
        localNpcImmunityProjectileGenerations = new ProjectileGeneration[projectiles.Capacity];
        localNpcHitGeneration = new NpcGeneration[checked(projectiles.Capacity * npcs.Capacity)];
        superStarSlashStaticNpcGeneration = new NpcGeneration[npcs.Capacity];
        superStarSlashStaticNpcImmuneUntil = new long[npcs.Capacity];
        Array.Fill(lastOwnerNpcHitTick, long.MinValue);
        Array.Fill(superStarSlashStaticNpcImmuneUntil, long.MinValue);
    }

    public long CommittedHits { get; private set; }
    public long Kills { get; private set; }
    public long ConsumedProjectiles { get; private set; }
    public long SpawnedChildProjectiles { get; private set; }

    public void BeginTick() => cachedNpcCount = npcs.CopyActive(npcBuffer);

    public void Tick()
    {
        BeginTick();
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
            !IsEligible(in projectile, out VanillaProjectileDefinition projectileDefinition, out VanillaProjectileBehaviorProfile behaviorProfile))
        {
            return;
        }

        if (!projectiles.TryGetCombatTrustedOwner(projectile.Handle, out PlayerHandle trustedOwner) ||
            !TryResolveOwnerRow(trustedOwner, out int ownerRow) ||
            !players.TryCaptureCombatSnapshot(trustedOwner, out VanillaPlayerCombatSnapshot ownerCombat))
        {
            return;
        }
        bool sharedOwnerImmunity = VanillaProjectileNpcCombatFacts.UsesSharedOwnerNpcImmunity(projectile.Type);
        bool usesLocalImmunity = VanillaProjectileNpcCombatFacts.TryGetLocalNpcHitCooldown(projectile.Type, out int localNpcHitCooldown);
        bool usesStaticImmunity = VanillaProjectileNpcCombatFacts.TryGetStaticNpcHitCooldown(projectile.Type, out int staticNpcHitCooldown);
        int localNpcHitRow = usesLocalImmunity ? ResolveLocalNpcHitRow(projectile.Handle) : -1;

        for (int npcIndex = 0; npcIndex < cachedNpcCount; npcIndex++)
        {
            NpcSnapshot target = npcBuffer[npcIndex];
            // The candidate order is frozen at BeginTick, but combat state is not. A later physical projectile
            // slot (including a child spawned earlier in this same global tick) must observe a kill/despawn or
            // generation replacement committed by an earlier slot.
            if (!npcs.TryGet(target.Handle, out target) ||
                !IsEligibleTarget(in target, out VanillaNpcHitboxSize npcHitbox) ||
                !Intersects(in projectile, in projectileDefinition, in target, in npcHitbox) ||
                (sharedOwnerImmunity && IsOwnerNpcOnCooldown(ownerRow, target.Handle, tick)) ||
                (usesLocalImmunity && IsLocalNpcOnCooldown(localNpcHitRow, target.Handle, localNpcHitCooldown, tick)) ||
                (usesStaticImmunity && IsStaticNpcOnCooldown(target.Handle, staticNpcHitCooldown, tick)))
            {
                continue;
            }

            int hitDirection = projectile.VelocityX > 0.01f ? 1 : projectile.VelocityX < -0.01f ? -1 : 0;
            int sourceDamage = projectile.Damage;
            int armorPenetration = 0;
            bool critical = false;
            if (behaviorProfile.Family is VanillaProjectileBehaviorFamily.BasicArrow or
                VanillaProjectileBehaviorFamily.SuperStar or
                VanillaProjectileBehaviorFamily.SuperStarSlash)
            {
                // Projectile.Damage stores the authoritative pre-hit bow+ammo damage. Vanilla Damage() applies
                // DamageVar and ranged crit at collision time; use the current authoritative player snapshot.
                sourceDamage = Math.Max(1, (int)Math.Round(
                    projectile.Damage * (1f + random.Next(-15, 16) * 0.01f)));
                critical = random.Next(1, 101) <= Math.Clamp(ownerCombat.RangedCrit, 0, 100);
                armorPenetration = ownerCombat.GetArmorPenetration(melee: false);
            }

            // In Projectile.Damage, Nano Flask modifies dmg immediately before Flask of Party spawns child 289,
            // and both happen before NPC.StrikeNPC. Keep that ordering server-owned for admitted melee projectiles.
            if (VanillaProjectilePvpCombatFacts.ShouldApplyNanoFlaskDamageBoost(projectile.Type, ownerCombat.MeleeEnchant))
                sourceDamage = (int)(sourceDamage * VanillaProjectilePvpCombatFacts.NanoFlaskDamageMultiplier);

            if (VanillaProjectilePvpCombatFacts.ShouldSpawnConfettiMeleeChild(projectile.Type, ownerCombat.MeleeEnchant) &&
                RuntimeProjectileChildSpawn1458.TrySpawnConfettiMelee(
                    projectiles,
                    trustedOwner,
                    target.PositionX + npcHitbox.Width * 0.5f,
                    target.PositionY + npcHitbox.Height * 0.5f,
                    target.VelocityX,
                    target.VelocityY,
                    out _))
            {
                SpawnedChildProjectiles++;
            }

            RuntimeProjectileNpcDamageResult result = combat.TryStrikeProjectile(
                in projectile, target.Handle, hitDirection, sourceDamage, armorPenetration, critical);
            if (result == RuntimeProjectileNpcDamageResult.Rejected)
                continue;

            if (sharedOwnerImmunity)
                MarkOwnerNpcCooldown(ownerRow, target.Handle, tick);
            else if (usesLocalImmunity)
                MarkLocalNpcCooldown(localNpcHitRow, target.Handle, localNpcHitCooldown, tick);
            else if (usesStaticImmunity)
                MarkStaticNpcCooldown(target.Handle, staticNpcHitCooldown, tick);
            CommittedHits++;
            if (result == RuntimeProjectileNpcDamageResult.Killed)
                Kills++;

            if (!projectiles.TryConsumeNpcHitPenetration(projectile.Handle, out bool despawned, out ProjectileSnapshot current))
                break;
            if (despawned)
            {
                ConsumedProjectiles++;
                break;
            }
            projectile = current;

            // Projectile.Damage_PVE_Inner calls SummonSuperStarSlash only after the committed NPC hit has
            // completed immunity and penetration side effects. NewProjectile then chooses the first free physical
            // slot; the live slot scheduler decides whether that child gets a same-global-tick update.
            if (projectile.Type == VanillaProjectileIds.SuperStar &&
                RuntimeProjectileChildSpawn1458.TrySpawnSuperStarSlash(
                    projectiles,
                    trustedOwner,
                    target.PositionX + npcHitbox.Width * 0.5f,
                    target.PositionY + npcHitbox.Height * 0.5f,
                    initialProjectile.Damage,
                    random,
                    out _))
            {
                SpawnedChildProjectiles++;
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
            !VanillaProjectileDefinitionCatalog.TryGet(projectile.Type, out definition) ||
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
            VanillaProjectileBehaviorFamily.SuperStar;
    }

    private static bool IsEligibleTarget(in NpcSnapshot target, out VanillaNpcHitboxSize hitbox)
    {
        if (!target.IsActive || target.Simulation.Life <= 0 || target.Simulation.DontTakeDamage ||
            !VanillaNpcDefinitionCatalog.TryGet(target.TypeIdentity, target.NetIdentity, out VanillaNpcDefinition definition) ||
            definition.Role == NpcArchetypeRole.Town ||
            !definition.TryResolveHitbox(target.Simulation.Scale, out hitbox))
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

    private int ResolveLocalNpcHitRow(ProjectileHandle handle)
    {
        int slot = handle.Slot;
        if (localNpcImmunityProjectileGenerations[slot] != handle.Generation)
        {
            localNpcImmunityProjectileGenerations[slot] = handle.Generation;
            Array.Clear(localNpcHitGeneration, slot * npcs.Capacity, npcs.Capacity);
        }
        return slot * npcs.Capacity;
    }

    private bool IsLocalNpcOnCooldown(int rowStart, NpcHandle target, int cooldown, long tick)
    {
        if (rowStart < 0)
            return true;
        int index = rowStart + target.Slot;
        if (localNpcHitGeneration[index] != target.Generation)
            return false;
        // The current admitted local-immunity family is SuperStar with localNPCHitCooldown=-1, meaning
        // one hit per NPC generation for the entire projectile generation. Positive local cooldowns stay
        // fail-closed until their countdown state is added.
        return cooldown == -1 || cooldown > 0;
    }

    private void MarkLocalNpcCooldown(int rowStart, NpcHandle target, int cooldown, long tick)
    {
        if (rowStart < 0 || cooldown != -1)
            return;
        localNpcHitGeneration[rowStart + target.Slot] = target.Generation;
    }

    private bool IsStaticNpcOnCooldown(NpcHandle target, int cooldown, long tick)
    {
        if (cooldown <= 0 || target.Slot >= superStarSlashStaticNpcGeneration.Length)
            return true;
        int index = target.Slot;
        return superStarSlashStaticNpcGeneration[index] == target.Generation &&
               tick < superStarSlashStaticNpcImmuneUntil[index];
    }

    private void MarkStaticNpcCooldown(NpcHandle target, int cooldown, long tick)
    {
        if (cooldown <= 0 || target.Slot >= superStarSlashStaticNpcGeneration.Length)
            return;
        int index = target.Slot;
        superStarSlashStaticNpcGeneration[index] = target.Generation;
        superStarSlashStaticNpcImmuneUntil[index] = tick + cooldown;
    }

    private bool TryResolveOwnerRow(PlayerHandle trustedOwner, out int ownerRow)
    {
        ownerRow = -1;
        if (!trustedOwner.IsAssigned ||
            !players.TryCapture(trustedOwner, out PlayerStateSnapshot currentOwner) ||
            currentOwner.Player != trustedOwner)
        {
            return false;
        }

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

}
