using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaNpcCatchCatalog1458Tests
{
    [Theory]
    [InlineData(46, 2019, true)]
    [InlineData(375, 2673, false)]
    [InlineData(615, 0, true)]
    [InlineData(625, 0, true)]
    [InlineData(687, 2121, true)]
    [InlineData(688, 5511, true)]
    public void Critter_and_catch_item_are_distinct_source_facts(int npc, int catchItem, bool critter)
    {
        var type = new NpcTypeId(npc);
        Assert.Equal(critter, VanillaNpcCatchCatalog1458.CountsAsCritter(type));
        Assert.Equal(catchItem > 0, VanillaNpcCatchCatalog1458.TryGetCatchItem(type, out ItemTypeId item));
        if (catchItem > 0)
            Assert.Equal(catchItem, item.Value);
    }
}
