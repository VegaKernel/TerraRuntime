using TerraRuntime.Gameplay.Items;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    private WorldItemAuthority? statueItems;
    private Func<long>? statueTick;

    internal void BindWiringItemStatues(WorldItemAuthority items, Func<long> tick)
    {
        if (statueItems is not null || !items.OwnsWiringItems(worldItems, worldItemSpawnRandom))
            throw new InvalidOperationException("Statue items must share this world's existing item owner.");
        statueItems = items;
        statueTick = tick ?? throw new ArgumentNullException(nameof(tick));
    }

    private bool TryFireItemStatue(int x, int y, int style)
    {
        if (tiles is null || wiring is null || statueItems is null || statueTick is null ||
            !VanillaItemStatue1458.TryResolve(style, out var item, out int offset) || !statueItems.CanCreateWiringStatue(item)) return false;
        var footprint = new WorldTile[6];
        for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 3; dy++) footprint[dx * 3 + dy] = tiles.Get(x + dx, y + dy);
        long tick = statueTick();
        bool Current()
        {
            if (statueTick() != tick) return false;
            for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 3; dy++)
                if (!tiles.Get(x + dx, y + dy).Equals(footprint[dx * 3 + dy])) return false;
            return true;
        }
        if (!Current() || !wiring.Register(x, y, VanillaItemStatue1458.Cooldown)) return false;
        // Source CheckMech commits before population checks: a crowded statue
        // consumes its cooldown, including when the subsequent item birth refuses.
        return statueItems.TryCreateWiringStatue(item, x * 16 + 16, (y + 3) * 16, offset, Current);
    }
}
