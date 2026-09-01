using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime;

/// <summary>
/// Source-shaped projectile/NPC reflection pass for committed projectile positions. Terraria Projectile.Update
/// performs movement before Damage(), and Damage() tests targetNPC.reflectsProjectiles before NPC damage. This
/// pass therefore runs after the authoritative projectile simulation commit for the world tick and applies only
/// the reflection short-circuit. Ordinary projectile-to-NPC damage remains a separate combat slice.
/// </summary>
internal sealed class RuntimeNpcProjectileReflectionPass
{
    private const float PlayerWidth = 20f;
    private const float PlayerHeight = 42f;

    private readonly RuntimeNpcStore npcs;
    private readonly RuntimeProjectileStore projectiles;
    private readonly IRuntimePlayerSlotSnapshotLookup players;
    private readonly IVanillaProjectileReflectionRandom random;
    private readonly NpcSnapshot[] npcScratch;
    private readonly ProjectileSnapshot[] projectileScratch;

    public RuntimeNpcProjectileReflectionPass(
        RuntimeNpcStore npcs,
        RuntimeProjectileStore projectiles,
        IRuntimePlayerSlotSnapshotLookup players,
        IVanillaProjectileReflectionRandom? random = null)
    {
        this.npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        this.projectiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.random = random ?? new SystemProjectileReflectionRandom();
        npcScratch = new NpcSnapshot[npcs.Capacity];
        projectileScratch = new ProjectileSnapshot[projectiles.Capacity];
    }

    public int Tick()
    {
        int npcCount = npcs.CopyActive(npcScratch);
        int projectileCount = projectiles.CopyActive(projectileScratch);
        int reflected = 0;

        for (int projectileIndex = 0; projectileIndex < projectileCount; projectileIndex++)
        {
            ProjectileSnapshot projectile = projectileScratch[projectileIndex];
            if (!projectiles.TryGetLifecycle(projectile.Handle, out ProjectileLifecycleState lifecycle) ||
                !VanillaProjectileDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition projectileDefinition) ||
                !VanillaProjectileReflection1458.CanBeReflected(in projectile, in lifecycle, in projectileDefinition) ||
                !players.TryGetPlayer(new PlayerSlotId(projectile.Spawner), out PlayerStateSnapshot owner) ||
                !owner.Player.IsAssigned)
            {
                continue;
            }

            for (int npcIndex = 0; npcIndex < npcCount; npcIndex++)
            {
                NpcSnapshot npc = npcScratch[npcIndex];
                if (!npc.IsActive ||
                    !npc.Simulation.ReflectsProjectiles ||
                    !VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out VanillaNpcDefinition npcDefinition) ||
                    !npcDefinition.TryResolveHitbox(npc.Simulation.Scale, out VanillaNpcHitboxSize npcHitbox) ||
                    !Intersects(in npc, in npcHitbox, in projectile, in projectileDefinition))
                {
                    continue;
                }

                if (!VanillaProjectileReflection1458.TryResolve(
                        in projectile,
                        in lifecycle,
                        owner.PositionX + PlayerWidth * 0.5f,
                        owner.PositionY + PlayerHeight * 0.5f,
                        random,
                        out VanillaProjectileReflectionResult mutation))
                {
                    break;
                }

                if (projectiles.TryReflect(
                        projectile.Handle,
                        mutation.VelocityX,
                        mutation.VelocityY,
                        mutation.Damage,
                        out _))
                {
                    reflected++;
                }
                break;
            }
        }

        return reflected;
    }

    private static bool Intersects(
        in NpcSnapshot npc,
        in VanillaNpcHitboxSize npcHitbox,
        in ProjectileSnapshot projectile,
        in VanillaProjectileDefinition projectileDefinition) =>
        projectile.PositionX < npc.PositionX + npcHitbox.Width &&
        projectile.PositionX + projectileDefinition.Width > npc.PositionX &&
        projectile.PositionY < npc.PositionY + npcHitbox.Height &&
        projectile.PositionY + projectileDefinition.Height > npc.PositionY;

    private sealed class SystemProjectileReflectionRandom : IVanillaProjectileReflectionRandom
    {
        public int NextInt32(int inclusiveMin, int exclusiveMax) =>
            Random.Shared.Next(inclusiveMin, exclusiveMax);
    }
}
