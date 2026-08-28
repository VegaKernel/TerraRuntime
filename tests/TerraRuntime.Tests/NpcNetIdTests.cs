using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class NpcNetIdTests
{
    [Fact]
    public void Signed_net_identity_remains_distinct_from_positive_gameplay_type()
    {
        var netIdentity = new NpcNetId(-17);
        var gameplayType = new NpcTypeId(17);

        Assert.Equal(-17, netIdentity.Value);
        Assert.Equal(17, gameplayType.Value);
    }

    [Fact]
    public void World_npc_persistence_exposes_typed_net_identity_without_reinterpreting_it()
    {
        var townNpc = new WorldTownNpc(-17, "npc", 10f, 20f, true, 0, 0, null, false);
        var persistentNpc = new WorldPersistentNpc(-23, 30f, 40f);

        Assert.Equal(new NpcNetId(-17), townNpc.NetIdentity);
        Assert.Equal(new NpcNetId(-23), persistentNpc.NetIdentity);
    }
}
