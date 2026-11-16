using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    private WorldItemAuthority? statueItems;
    private Func<long>? statueTick;

    internal void BindWiringStatues(WorldItemAuthority items, Func<long> tick)
    {
        if (statueItems is not null || !items.OwnsWiringItems(worldItems, worldItemSpawnRandom))
            throw new InvalidOperationException("Statue items must share this world's existing item owner.");
        statueItems = items;
        statueTick = tick ?? throw new ArgumentNullException(nameof(tick));
    }

    private bool TryFireStatue(int x, int y, int style)
    {
        if (tiles is null || wiring is null || statueItems is null || statueTick is null ||
            !VanillaItemStatue1458.TryResolve(style, out var item, out int offset) && style is not (40 or 41) && !npcAuthority.CanCreateWiringStatue(style)) return false;
        bool itemStatue = !item.IsNone;
        if (itemStatue ? !statueItems.CanCreateWiringStatue(item) : !npcAuthority.OwnsWiringRandom(worldItemSpawnRandom)) return false;
        var footprint = new WorldTile[6];
        for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 3; dy++) footprint[dx * 3 + dy] = tiles.Get(x + dx, y + dy);
        // NPC NewNPC reads WetCollision beyond the statue itself. Retain the small
        // surrounding section set too; a concurrent/reentrant terrain change refuses
        // the birth conservatively, even if that edit misses the final hitbox.
        var sections = itemStatue ? [] : new[]
        {
            TerrariaSectionGeometry.FromTile(tiles.Dimensions, Math.Max(0, x - 16), Math.Max(0, y - 16)),
            TerrariaSectionGeometry.FromTile(tiles.Dimensions, Math.Min(tiles.Dimensions.WidthTiles - 1, x + 16), Math.Max(0, y - 16)),
            TerrariaSectionGeometry.FromTile(tiles.Dimensions, Math.Max(0, x - 16), Math.Min(tiles.Dimensions.HeightTiles - 1, y + 16)),
            TerrariaSectionGeometry.FromTile(tiles.Dimensions, Math.Min(tiles.Dimensions.WidthTiles - 1, x + 16), Math.Min(tiles.Dimensions.HeightTiles - 1, y + 16))
        }.Distinct().Select(s => (Section: s, Version: tiles.GetSectionVersion(s))).ToArray();
        long tick = statueTick();
        bool Current()
        {
            if (statueTick() != tick || sections.Any(s => tiles.GetSectionVersion(s.Section) != s.Version)) return false;
            for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 3; dy++)
                if (!tiles.Get(x + dx, y + dy).Equals(footprint[dx * 3 + dy])) return false;
            return true;
        }
        int cooldown = style is 40 or 41 ? TerraRuntime.Gameplay.Npcs.VanillaTownStatue1458.Cooldown :
            itemStatue ? VanillaItemStatue1458.Cooldown : TerraRuntime.Gameplay.Npcs.VanillaNpcStatue1458.Cooldown;
        if (!Current()) return false;
        int selected = 0;
        bool extraAllowed = true;
        if (!itemStatue && style is not (40 or 41))
            selected = npcAuthority.SelectWiringStatue(style, x * 16 + 16, (y + 3) * 16, out extraAllowed);
        if (!wiring.Register(x, y, cooldown)) return false;
        if (style is 40 or 41)
            return npcAuthority.TryTeleportWiringResident(style, x * 16 + 16, (y + 3) * 16, Current);
        // Source CheckMech commits before population checks: a crowded statue
        // consumes its cooldown, including when the subsequent entity birth refuses.
        if (itemStatue)
            return statueItems.TryCreateWiringStatue(item, x * 16 + 16, (y + 3) * 16, offset, Current);
        bool Blocked()
        {
            VanillaNpcStatue1458.NeedsClearance(style, out int left, out int right, out int bottom);
            if (x + left < 0 || x + right >= tiles.Dimensions.WidthTiles || y < 0 || y + bottom >= tiles.Dimensions.HeightTiles - 40) return true;
            for (int cx = x + left; cx <= x + right; cx++) for (int cy = y; cy <= y + bottom; cy++)
            {
                var tile = tiles.Get(cx, cy);
                if (tile.IsActive && !tile.IsActuated && VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
                    !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType)) return true;
            }
            return false;
        }
        bool created = npcAuthority.TryCreateWiringStatue(style, selected, extraAllowed, x * 16 + 16, (y + 3) * 16, Blocked, Current, out bool smoke);
        if (smoke) replication?.TryPublishPoofOfSmoke(x * 16 + 2, (y + 3) * 16 - 32);
        return created;
    }
}
