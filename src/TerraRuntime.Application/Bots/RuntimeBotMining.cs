using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.World;

namespace TerraRuntime.Application.Bots;

internal readonly record struct RuntimeBotMiningTarget(int X, int Y, TileTypeId Type, long SectionVersion);

/// <summary>Bounded ore discovery and one leased destination; tile/drop semantics remain in WorldTileAuthority.</summary>
internal sealed class RuntimeBotMining(BotState bot, ServerPlayerAuthority players, WorldTileStore tiles,
    WorldTileAuthority authority, RuntimeBotNavigation navigation, RuntimeBotResourceLeases leases, WorldRuntimeIdentity world)
{
    internal const int SearchRadius = 32, CellsPerTick = 256;
    private const int SearchWidth = SearchRadius * 2 + 1;
    private RuntimeBotMiningTarget? target;
    private int scan, originX, originY;
    private ulong goal;
    private long retryAt;
    private int cleared;
    private RuntimeBotLeaseOwner Owner => new(bot.Id, bot.Player, world);
    private RuntimeBotResourceKey Key(in RuntimeBotMiningTarget t) => new(world, RuntimeBotResourceKind.TileTarget, default, t.X, t.Y);

    public RuntimeBotMiningTarget? Observe(in PlayerStateSnapshot self, long tick)
    {
        if (goal != bot.GoalGeneration) { Finish(false); goal = bot.GoalGeneration; retryAt = 0; scan = 0; }
        if (bot.Configuration.Mode != RuntimeBotMode.Mining || self.IsDead ||
            tiles.WorldSurfaceTiles is not double surface || self.PositionY <= surface * 16d || tick < retryAt) return null;
        if (target is { } retained) return retained;
        if (scan == 0) { originX = (int)((self.PositionX + 10) / 16); originY = (int)((self.PositionY + 21) / 16); }
        for (int work = 0; work < CellsPerTick; work++)
        {
            int index = scan++;
            int x = originX + index / SearchWidth - SearchRadius, y = originY + index % SearchWidth - SearchRadius;
            if (scan == SearchWidth * SearchWidth) { scan = 0; retryAt = tick + 60; }
            if (x > 1 && x < tiles.Dimensions.WidthTiles - 2 && y > surface && y < tiles.Dimensions.HeightTiles - 2)
            {
                var tile = tiles.Get(x, y);
                if (tile.IsActive && tile.TileType.Value == (int)bot.Configuration.MiningOre && tile.Wall == 0 &&
                    tile.LiquidAmount == 0 && tile.Flags == WorldTileFlags.Active)
                {
                    var found = new RuntimeBotMiningTarget(x, y, tile.TileType,
                        tiles.GetSectionVersion(TerrariaSectionGeometry.FromTile(tiles.Dimensions, x, y)));
                    if (leases.IsAvailable(Owner, Key(found), tick)) { target = found; return found; }
                }
            }
            if (scan == 0) break;
        }
        return null;
    }

    public RuntimeBotActionResult Tick(in RuntimeBotObservationSnapshot observation)
    {
        if (!RuntimeBotObservationScope.IsCurrent(bot, world, observation))
            return RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.StaleDecision);
        if (observation.Underground is null) return RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.UnsupportedAction);
        if (observation.Underground != true) return RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.PermissionDenied);
        if (observation.MiningTarget is not { } t) { navigation.Stop(); return RuntimeBotActionResult.Pending(); }
        if (target != t || tiles.GetSectionVersion(TerrariaSectionGeometry.FromTile(tiles.Dimensions, t.X, t.Y)) != t.SectionVersion ||
            !tiles.Get(t.X, t.Y).IsActive || tiles.Get(t.X, t.Y).TileType != t.Type)
            return RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.TargetChanged);
        if (!leases.TryAcquire(Owner, Key(t), observation.Tick))
            return RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.PermissionDenied);
        // Stop before producing loot that cannot fit. Normal pickup later transfers the authoritative drop.
        ItemTypeId drop = VanillaTileDefinitionCatalog.Get(t.Type).DropRule.PrimaryItem;
        bool space = false;
        for (short slot = 1; slot < 50; slot++)
            if (players.TryGetItem(bot.ServerPlayerId, slot, out var item) &&
                (item.IsEmpty || item.ItemType == drop && item.Prefix.Value == 0 && item.Stack < 9999)) { space = true; break; }
        if (!space) return RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.InventoryFull);
        float dx = t.X * 16f + 8 - observation.Self.PositionX - 10, dy = t.Y * 16f + 8 - observation.Self.PositionY - 21;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        // Open a body-sized corridor, not a one-cell ray that leaves the player's head/feet trapped.
        // This is bounded navigation policy; every individual swing still requires the normal tile authority.
        if (TryFindObstruction(observation.Self, t, dx, dy, distance, out int bx, out int by))
        {
            var obstruction = tiles.Get(bx, by);
            if (obstruction.TileType != VanillaTileIds.Dirt && obstruction.TileType != VanillaTileIds.Stone)
                return RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.PermissionDenied);
            navigation.Stop(); // Do not jump into the cell being excavated during the pick animation.
            if (observation.Tick < bot.UseItemUntilTick) return RuntimeBotActionResult.Pending(cleared * 2048 - distance);
            long revision = tiles.GetSectionVersion(TerrariaSectionGeometry.FromTile(tiles.Dimensions, bx, by));
            if (!authority.TryMineServerPlayerOre(players, bot.ServerPlayerId, bx, by, obstruction.TileType, revision))
                return RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.PermissionDenied);
            bot.UseItemUntilTick = observation.Tick + 12;
            target = t with { SectionVersion = tiles.GetSectionVersion(TerrariaSectionGeometry.FromTile(tiles.Dimensions, t.X, t.Y)) };
            return RuntimeBotActionResult.Pending(++cleared * 2048 - distance);
        }
        // Approach closely enough that the ordinary pickup path can reach the drop after the swing.
        // Merely being within the pick's reach can leave an inaccessible item behind a remaining wall.
        if (Math.Abs(dx) <= 24 && Math.Abs(dy) <= 24)
        {
            navigation.Stop();
            if (observation.Tick < bot.UseItemUntilTick) return RuntimeBotActionResult.Pending(cleared * 2048 - distance);
            if (!authority.TryMineServerPlayerOre(players, bot.ServerPlayerId, t.X, t.Y, t.Type, t.SectionVersion))
                return RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.PermissionDenied);
            bot.UseItemUntilTick = observation.Tick + 12;
            return RuntimeBotActionResult.Success;
        }
        navigation.Move(observation, t.X * 16f + 8, t.Y * 16f + 8, 16);
        return RuntimeBotActionResult.Pending(cleared * 2048 - distance);
    }

    private bool TryFindObstruction(in PlayerStateSnapshot self, in RuntimeBotMiningTarget ore,
        float dx, float dy, float distance, out int x, out int y)
    {
        for (float step = 4; step <= Math.Min(distance, 32); step += 4)
        {
            float cx = self.PositionX + 10 + dx / distance * step, cy = self.PositionY + 21 + dy / distance * step;
            for (int tx = (int)((cx - 9) / 16); tx <= (int)((cx + 9) / 16); tx++)
            for (int ty = (int)((cy - 20) / 16); ty <= (int)((cy + 20) / 16); ty++)
            {
                if (tx == ore.X && ty == ore.Y || tx < 1 || ty < 1 ||
                    tx >= tiles.Dimensions.WidthTiles - 1 || ty >= tiles.Dimensions.HeightTiles - 1 ||
                    Math.Abs(tx * 16f + 8 - self.PositionX - 10) > 64 ||
                    Math.Abs(ty * 16f + 8 - self.PositionY - 21) > 48) continue;
                if (tiles.Get(tx, ty).IsActive) { x = tx; y = ty; return true; }
            }
        }
        x = y = 0;
        return false;
    }

    public void Finish(bool failed)
    {
        if (target is { } t) leases.Release(Owner, Key(t));
        target = null;
        cleared = 0;
        if (failed) retryAt = bot.CurrentTick + 60;
    }
}
