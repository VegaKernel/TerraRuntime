using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>
/// World-backed target lookup used by the modern AI_009 controlled-magic slice. Slot ordering, 800 px range and
/// rectangle line-of-sight follow TerrariaServer 1.4.5.8 Projectile.FindTargetWithLineOfSight. The current NPC
/// runtime does not expose vanilla's transient chaseable/immortal flags; verified town NPCs, critters, dead and
/// invulnerable NPCs are therefore rejected explicitly while ordinary verified hostile definitions remain eligible.
/// </summary>
internal sealed class VanillaProjectileNpcTargetResolver : IVanillaProjectileNpcTargetResolver
{
    private readonly RuntimeNpcStore npcs;
    private readonly WorldTileStore tiles;

    public VanillaProjectileNpcTargetResolver(RuntimeNpcStore npcs, WorldTileStore tiles)
    {
        this.npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
    }

    public bool TryFindClosestTargetWithLineOfSight(
        in ProjectileSnapshot projectile,
        in VanillaProjectileDefinition projectileDefinition,
        float maxRange,
        out int npcSlot,
        out float targetCenterX,
        out float targetCenterY)
    {
        npcSlot = -1;
        targetCenterX = 0f;
        targetCenterY = 0f;
        if (!(maxRange > 0f) || !float.IsFinite(maxRange))
            return false;

        float projectileCenterX = projectile.PositionX + projectileDefinition.Width * 0.5f;
        float projectileCenterY = projectile.PositionY + projectileDefinition.Height * 0.5f;
        float closest = maxRange;

        // Main.npc is scanned in physical slot order. The strict distance comparison intentionally preserves the
        // first slot on exact ties, matching FindTargetWithLineOfSight rather than introducing sort/allocation work.
        for (int slot = 0; slot < npcs.Capacity; slot++)
        {
            if (!npcs.TryGetActive(checked((byte)slot), out NpcSnapshot candidate) ||
                !TryResolveChaseableTarget(in candidate, out VanillaNpcHitboxSize hitbox, out float centerX, out float centerY))
            {
                continue;
            }

            float dx = centerX - projectileCenterX;
            float dy = centerY - projectileCenterY;
            float distance = MathF.Sqrt(dx * dx + dy * dy);
            if (!(distance < closest) || !float.IsFinite(distance) ||
                !VanillaWorldCanHit.HasLineOfSight(
                    tiles,
                    projectile.PositionX,
                    projectile.PositionY,
                    projectileDefinition.Width,
                    projectileDefinition.Height,
                    candidate.PositionX,
                    candidate.PositionY,
                    hitbox.Width,
                    hitbox.Height))
            {
                continue;
            }

            closest = distance;
            npcSlot = slot;
            targetCenterX = centerX;
            targetCenterY = centerY;
        }

        return npcSlot >= 0;
    }

    public bool TryGetChaseableTargetCenter(int npcSlot, out float targetCenterX, out float targetCenterY)
    {
        targetCenterX = 0f;
        targetCenterY = 0f;
        if ((uint)npcSlot >= (uint)npcs.Capacity ||
            !npcs.TryGetActive(checked((byte)npcSlot), out NpcSnapshot candidate) ||
            !TryResolveChaseableTarget(in candidate, out _, out targetCenterX, out targetCenterY))
        {
            return false;
        }
        return true;
    }

    public bool IsNpcSlotAddressable(int npcSlot) => (uint)npcSlot < (uint)npcs.Capacity;

    public bool TryGetActiveNpc(int npcSlot, out NpcSnapshot npc)
    {
        if ((uint)npcSlot >= (uint)npcs.Capacity)
        {
            npc = default;
            return false;
        }

        return npcs.TryGetActive(checked((byte)npcSlot), out npc);
    }

    private static bool TryResolveChaseableTarget(
        in NpcSnapshot candidate,
        out VanillaNpcHitboxSize hitbox,
        out float centerX,
        out float centerY)
    {
        centerX = 0f;
        centerY = 0f;
        if (!candidate.IsActive ||
            candidate.Simulation.Life <= 0 ||
            candidate.Simulation.LifeMax <= 5 ||
            candidate.Simulation.DontTakeDamage ||
            VanillaNpcCatchCatalog1458.CountsAsCritter(candidate.TypeIdentity) ||
            !VanillaNpcDefinitionCatalog.TryGet(
                candidate.TypeIdentity,
                candidate.NetIdentity,
                out VanillaNpcDefinition definition) ||
            definition.Role == NpcArchetypeRole.Town ||
            !definition.TryResolveHitbox(candidate.Simulation.Scale, out hitbox))
        {
            hitbox = default;
            return false;
        }

        centerX = candidate.PositionX + hitbox.Width * 0.5f;
        centerY = candidate.PositionY + hitbox.Height * 0.5f;
        return float.IsFinite(centerX) && float.IsFinite(centerY);
    }
}
