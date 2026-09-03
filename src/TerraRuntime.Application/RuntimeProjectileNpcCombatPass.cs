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
    private readonly IRuntimePlayerSlotSnapshotLookup players;
    private readonly Func<long> tickProvider;
    private readonly ProjectileSnapshot[] projectileBuffer;
    private readonly NpcSnapshot[] npcBuffer;
    private readonly PlayerSessionGeneration[] ownerGenerations = new PlayerSessionGeneration[PlayerSlotCount];
    private readonly long[] lastOwnerNpcHitTick;

    public RuntimeProjectileNpcCombatPass(
        RuntimeProjectileStore projectiles,
        RuntimeNpcStore npcs,
        RuntimeNpcNetworkCombatPipeline combat,
        IRuntimePlayerSlotSnapshotLookup players,
        Func<long> tickProvider)
    {
        this.projectiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
        this.npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        this.combat = combat ?? throw new ArgumentNullException(nameof(combat));
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.tickProvider = tickProvider ?? throw new ArgumentNullException(nameof(tickProvider));
        projectileBuffer = new ProjectileSnapshot[projectiles.Capacity];
        npcBuffer = new NpcSnapshot[npcs.Capacity];
        lastOwnerNpcHitTick = new long[checked(PlayerSlotCount * npcs.Capacity)];
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
                !IsEligible(in projectile, out VanillaProjectileDefinition projectileDefinition))
            {
                continue;
            }

            if (!projectiles.TryGetCombatTrustedOwner(projectile.Handle, out PlayerHandle trustedOwner) ||
                !TryResolveOwnerRow(trustedOwner, out int ownerRow))
            {
                continue;
            }
            bool sharedOwnerImmunity = VanillaProjectileNpcCombatFacts.UsesSharedOwnerNpcImmunity(projectile.Type);

            bool projectileEnded = false;
            for (int npcIndex = 0; npcIndex < npcCount; npcIndex++)
            {
                NpcSnapshot target = npcBuffer[npcIndex];
                if (!IsEligibleTarget(in target, out VanillaNpcHitboxSize npcHitbox) ||
                    !Intersects(in projectile, in projectileDefinition, in target, in npcHitbox) ||
                    (sharedOwnerImmunity && IsOwnerNpcOnCooldown(ownerRow, target.Handle.Slot, tick)))
                {
                    continue;
                }

                int hitDirection = projectile.VelocityX > 0.01f ? 1 : projectile.VelocityX < -0.01f ? -1 : 0;
                RuntimeProjectileNpcDamageResult result = combat.TryStrikeProjectile(in projectile, target.Handle, hitDirection);
                if (result == RuntimeProjectileNpcDamageResult.Rejected)
                    continue;

                if (sharedOwnerImmunity)
                    MarkOwnerNpcCooldown(ownerRow, target.Handle.Slot, tick);
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
            VanillaProjectileBehaviorFamily.Boomerang;
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

    private bool TryResolveOwnerRow(PlayerHandle trustedOwner, out int ownerRow)
    {
        ownerRow = -1;
        if (!trustedOwner.IsAssigned ||
            !players.TryGetPlayer(trustedOwner.Slot, out PlayerStateSnapshot currentOwner) ||
            currentOwner.Player != trustedOwner)
        {
            return false;
        }

        byte spawner = trustedOwner.Slot.Value;
        if (ownerGenerations[spawner] != trustedOwner.Generation)
        {
            ownerGenerations[spawner] = trustedOwner.Generation;
            Array.Fill(lastOwnerNpcHitTick, long.MinValue, spawner * npcs.Capacity, npcs.Capacity);
        }

        ownerRow = spawner * npcs.Capacity;
        return true;
    }

    private bool IsOwnerNpcOnCooldown(int ownerRow, ushort npcSlot, long tick)
    {
        long previous = lastOwnerNpcHitTick[ownerRow + npcSlot];
        return previous != long.MinValue &&
            tick - previous < VanillaProjectileNpcCombatFacts.BaselineOwnerNpcHitCooldownTicks;
    }

    private void MarkOwnerNpcCooldown(int ownerRow, ushort npcSlot, long tick) =>
        lastOwnerNpcHitTick[ownerRow + npcSlot] = tick;

}
