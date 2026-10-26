using TerraRuntime.Contracts.Runtime;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class FishingAuthority1458Tests
{
    [Fact]
    public void Genuine_Truffle_Worm_cast_bite_and_paid_pull_spawn_Fishron_once()
    {
        using var f = new FishingFixture(1, 2673);
        // Extend the same real cast pond beyond source Truffle Worm's 1000-cell
        // threshold; source water measurement, bite RNG and pull all still run.
        for (int x = 112; x <= 166; x++)
            for (int y = 100; y <= 139; y++)
                f.Tiles.Set(x, y, new() { LiquidAmount = y == 100 ? (byte)128 : (byte)255 });
        for (int y = 100; y <= 140; y++)
            foreach (int x in new[] { 111, 167 }) f.Tiles.Set(x, y, new() { Type = 1, Flags = WorldTileFlags.Active });
        for (int x = 111; x <= 167; x++) f.Tiles.Set(x, 140, new() { Type = 1, Flags = WorldTileFlags.Active });
        f.Cast();
        var bite = f.WaitForBite();
        Assert.Equal(370, bite.Result.NpcType);
        Assert.Equal(2673, bite.Result.BaitItemType);
        long before = f.Runtime.Npcs.AppliedSpawns;
        f.Pull();
        Assert.True(f.Item(f.BaitSlot).IsEmpty);
        Assert.Equal(0, f.Funded(f.BaitSlot));
        var actors = new NpcSnapshot[200];
        int count = f.Runtime.Npcs.CopyActive(actors);
        Assert.Equal(1, actors.Take(count).Count(actor => actor.Type == 370));
        Assert.Equal(before + 1, f.Runtime.Npcs.AppliedSpawns);
        f.Pull();
        count = f.Runtime.Npcs.CopyActive(actors);
        Assert.Equal(1, actors.Take(count).Count(actor => actor.Type == 370));
        Assert.Equal(before + 1, f.Runtime.Npcs.AppliedSpawns);
    }
}
