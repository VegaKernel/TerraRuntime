using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal readonly record struct RuntimeProjectileChildSpawnEvent(
    ProjectileSnapshot Projectile,
    NpcHandle SourceNpc);

/// <summary>
/// Bounded post-commit handoff for source-backed projectile children created by Projectile.Kill(). The exact
/// NPC generation that owned the parent is retained by the executor termination contract and copied to every
/// child, so a reused NPC slot cannot gain ownership of an old projectile chain.
/// </summary>
internal sealed class RuntimeProjectileChildSpawnQueue : IProjectileTerminationCommitSink
{
    private readonly RuntimeProjectileChildSpawnEvent[] events;
    private int count;

    public RuntimeProjectileChildSpawnQueue(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        events = new RuntimeProjectileChildSpawnEvent[capacity];
    }

    public ReadOnlySpan<RuntimeProjectileChildSpawnEvent> Events => events.AsSpan(0, count);

    public void Reset() => count = 0;

    public void ProjectileTerminated(in ProjectileTerminationCommit termination)
    {
        // TerrariaServer 1.4.5.8 Projectile.Kill(), type 385. Dedicated-server owner 255 executes the
        // owner == Main.myPlayer branch. World-bounds removal is not a vanilla Kill() and must not spawn.
        if (!termination.SourceNpc.IsAssigned ||
            termination.Reason == ProjectileSimulationTerminationReason.WorldBounds ||
            termination.FinalProjectile.Type != VanillaProjectileIds.SharknadoBolt)
        {
            return;
        }

        if (count >= events.Length)
            throw new InvalidOperationException("Projectile child-spawn queue capacity was exceeded by one simulation tick.");

        events[count++] = new RuntimeProjectileChildSpawnEvent(termination.FinalProjectile, termination.SourceNpc);
    }
}

/// <summary>Fan-out sink so one committed termination can feed independent bounded side-effect queues.</summary>
internal sealed class RuntimeProjectileTerminationEffectSink : IProjectileTerminationCommitSink
{
    private readonly RuntimeProjectileExplosionQueue explosions;
    private readonly RuntimeProjectileChildSpawnQueue children;

    public RuntimeProjectileTerminationEffectSink(
        RuntimeProjectileExplosionQueue explosions,
        RuntimeProjectileChildSpawnQueue children)
    {
        this.explosions = explosions ?? throw new ArgumentNullException(nameof(explosions));
        this.children = children ?? throw new ArgumentNullException(nameof(children));
    }

    public void ProjectileTerminated(in ProjectileTerminationCommit termination)
    {
        explosions.ProjectileTerminated(in termination);
        children.ProjectileTerminated(in termination);
    }
}

/// <summary>TerrariaServer 1.4.5.8 Projectile.Kill() child facts for Sharknado Bolt (#385).</summary>
internal static class RuntimeSharknadoChildSpawn1458
{
    public static bool TryCreateIntent(
        in RuntimeProjectileChildSpawnEvent child,
        WorldTileStore? tiles,
        bool expertMode,
        out NpcAiProjectileIntent intent)
    {
        ProjectileSnapshot parent = child.Projectile;
        if (!child.SourceNpc.IsAssigned || parent.Type != VanillaProjectileIds.SharknadoBolt ||
            !VanillaDefinitionCatalog.TryGet(parent.Type, out VanillaProjectileDefinition parentDefinition))
        {
            intent = default;
            return false;
        }

        float centerX = parent.PositionX + parentDefinition.Width * 0.5f;
        float centerY = parent.PositionY + parentDefinition.Height * 0.5f;

        if (parent.Ai.Ai1 < 1f)
        {
            // Entity.direction defaults to +1 and aiStyle 65 never rewrites it. Projectile.Kill() therefore
            // creates the ordinary Sharknado 30 px to the left with a tiny leftward velocity.
            VanillaDefinitionCatalog.TryGet(VanillaProjectileIds.Sharknado, out VanillaProjectileDefinition childDefinition);
            intent = new NpcAiProjectileIntent(
                VanillaProjectileIds.Sharknado,
                centerX - 30f - childDefinition.Width * 0.5f,
                centerY - 4f - childDefinition.Height * 0.5f,
                -0.01f,
                0f,
                expertMode ? 25 : 40,
                4f)
            {
                InitialAi = new ProjectileAiState(16f, 15f, 0f)
            };
            return true;
        }

        if (tiles is null || tiles.Dimensions.WidthTiles < 20 || tiles.Dimensions.HeightTiles < 110)
        {
            intent = default;
            return false;
        }

        int tileY = (int)(centerY / 16f);
        int tileX = (int)(centerX / 16f);
        const int searchDepth = 100;
        tileX = Math.Clamp(tileX, 10, tiles.Dimensions.WidthTiles - 10);
        tileY = Math.Clamp(tileY, 10, tiles.Dimensions.HeightTiles - searchDepth - 10);

        int landingY = tileY + 15;
        int endY = tileY + searchDepth;
        for (int y = tileY; y < endY; y++)
        {
            WorldTile tile = tiles.Get(tileX, y);
            if (tile.IsActive && (VanillaTileCollisionCatalog.IsSolid(tile.TileType) || tile.LiquidAmount != 0))
            {
                landingY = y;
                break;
            }
        }

        VanillaDefinitionCatalog.TryGet(VanillaProjectileIds.Cthulunado, out VanillaProjectileDefinition cthulunadoDefinition);
        intent = new NpcAiProjectileIntent(
            VanillaProjectileIds.Cthulunado,
            tileX * 16f + 8f - cthulunadoDefinition.Width * 0.5f,
            landingY * 16f - 24f - cthulunadoDefinition.Height * 0.5f,
            0f,
            0f,
            expertMode ? 50 : 80,
            4f)
        {
            InitialAi = new ProjectileAiState(16f, 24f, 0f)
        };
        return true;
    }
}
