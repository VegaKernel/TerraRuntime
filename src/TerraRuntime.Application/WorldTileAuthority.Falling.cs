using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    // Runtime load policy, not source timing. Mutation wakeups outrank the resumable initial/overflow scan.
    internal const int FallingChecksPerTick = 256, FallingSpawnsPerTick = 16;
    internal int LastFallingChecks { get; private set; }
    internal int LastFallingSpawns { get; private set; }

    internal void TickFallingBlocks(ProjectileAuthority projectiles)
    {
        LastFallingChecks = LastFallingSpawns = 0;
        if (tiles is null || mutations is null) return;
        var queue = tiles.EnableFallingBlockUpdates();
        for (int count = 0; count < FallingSpawnsPerTick && projectiles.FallingBlocks.TryPeek(out var landed); count++)
        {
            if (!TryLandFallingBlock(landed)) break;
            projectiles.FallingBlocks.Complete();
        }
        while (LastFallingChecks < FallingChecksPerTick && LastFallingSpawns < FallingSpawnsPerTick &&
            projectiles.FallingBlocks.CanSpawn && queue.TryTake(tiles.Count, out int index))
        {
            LastFallingChecks++;
            int x = index / tiles.Dimensions.HeightTiles, y = index % tiles.Dimensions.HeightTiles;
            if (x < 1 || y < 1 || x >= tiles.Dimensions.WidthTiles - 1 || y >= tiles.Dimensions.HeightTiles - 2) continue;
            WorldTile before = tiles.Get(x, y);
            if (!before.IsActive || !VanillaFallingBlock1458.TryGetProjectile(before.TileType, out _) || !BelowMakesFall(x, y)) continue;
            var above = tiles.Get(x, y - 1);
            if (above.IsActive && VanillaFallingBlock1458.PreventsFallAbove(above.TileType)) continue;
            if (!projectiles.TrySpawnFallingBlock(before.TileType, x, y, out var projectile)) { queue.Wake(index); break; }
            if (!ApplyTileMutation(mutations, WorldTileMutationKind.ClearTile, x, y))
            { projectiles.CancelFallingSpawn(projectile.Handle); continue; }
            // ClearTile with no KillTile drop: material now belongs to the trusted projectile generation.
            LastFallingSpawns++;
            replication?.TryPublishTileSquareToAll(tiles, x, y);
        }
    }

    private bool BelowMakesFall(int x, int y)
    {
        var below = tiles!.Get(x, y + 1);
        if (!below.IsActive || (below.Flags & WorldTileFlags.Inactive) != 0) return true;
        var second = tiles.Get(x, y + 2);
        return ((!second.IsActive || (second.Flags & WorldTileFlags.Inactive) != 0) &&
            !VanillaTileCollisionCatalog.IsSolid(below.TileType)) || below.Type == 165; // Stalactite.
    }

    private bool TryLandFallingBlock(ProjectileSnapshot projectile)
    {
        if (!VanillaFallingBlock1458.TryGetTile(projectile.Type, out var type)) return true;
        int x = (int)(projectile.PositionX + 5) / 16, y = (int)(projectile.PositionY + 5) / 16;
        if (x < 1 || y < 1 || x >= tiles!.Dimensions.WidthTiles - 1 || y >= tiles.Dimensions.HeightTiles - 2)
            return true; // Source world-edge deactivation has no Kill/terrain restoration.
        WorldTile target = tiles.Get(x, y);
        if (target.IsActive && (target.Flags & WorldTileFlags.Inactive) == 0 && target.Shape == 1 &&
            projectile.VelocityY > 0 && Math.Abs(projectile.VelocityY) > Math.Abs(projectile.VelocityX)) target = tiles.Get(x, --y);
        var support = tiles.Get(x, y + 1);
        bool itemOnly = support.IsActive && (VanillaFallingBlock1458.ConvertsLandingToItem(support.TileType) || BelowMakesFall(x, y));
        if (!target.IsActive && !itemOnly)
        {
            if (support.Shape != 0 && (!support.IsActive ||
                !VanillaTileDefinitionCatalog.TryGet(support.TileType, out var definition) ||
                definition.IsFrameImportant || definition.IsSolidTop || !definition.IsSolid)) return false;
            if (!mutations!.Apply(new(WorldTileMutationKind.PlaceTile, x, y, type)).Applied) return false;
            if (support.Shape != 0)
            {
                if (!mutations.Apply(new(WorldTileMutationKind.SetShape, x, y + 1)).Applied)
                    throw new InvalidOperationException("Validated falling-block support changed during the writer transaction.");
                replication?.TryPublishTileSquareToAll(tiles, x, y + 1);
            }
            replication?.TryPublishTileSquareToAll(tiles, x, y);
            return true;
        }
        // Reuse the existing fixed-item materialization/reservation owner, correcting the source drop center.
        if (!TryPrepareSimpleBreak(x, y, new WorldTile { Type = checked((ushort)type.Value), Flags = WorldTileFlags.Active }, out var prepared)) return false;
        var old = prepared.Outcome.Drop;
        float offsetY = target.IsActive ? 0 : -2;
        float halfSize = projectile.Type.Value == 812 ? 7 : 6; // ShellPile uses DefaultToPlaceableTile (14x14); others 12x12.
        var drop = old with { PositionX = projectile.PositionX + 5 - halfSize, PositionY = projectile.PositionY + 5 + offsetY - halfSize };
        prepared = prepared with { Outcome = prepared.Outcome with { Drop = drop } };
        CommitPreparedBreak(prepared);
        return true;
    }
}
