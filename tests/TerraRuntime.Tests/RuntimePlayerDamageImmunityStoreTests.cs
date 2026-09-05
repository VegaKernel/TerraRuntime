using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Tests;

public sealed class RuntimePlayerDamageImmunityStoreTests
{
    [Fact]
    public void ImmunityIsScopedToExactPlayerGenerationAndPveChannel()
    {
        var store = new RuntimePlayerDamageImmunityStore(byte.MaxValue + 1);
        var first = new PlayerHandle(new PlayerSlotId(7), new PlayerSessionGeneration(1));
        var reused = new PlayerHandle(new PlayerSlotId(7), new PlayerSessionGeneration(2));

        store.RecordPvp(first, immuneUntil: 120);
        Assert.True(store.IsPvpImmune(first, tick: 119));
        Assert.False(store.IsPvpImmune(first, tick: 120));
        Assert.False(store.IsPvpImmune(reused, tick: 119));

        store.RecordPve(first, VanillaPlayerImmunityChannel1458.General, immuneUntil: 130);
        store.RecordPve(first, VanillaPlayerImmunityChannel1458.BossNoCheese, immuneUntil: 140);
        Assert.True(store.IsPveImmune(first, VanillaPlayerImmunityChannel1458.General, tick: 129));
        Assert.True(store.IsPveImmune(first, VanillaPlayerImmunityChannel1458.BossNoCheese, tick: 139));
        Assert.False(store.IsPveImmune(reused, VanillaPlayerImmunityChannel1458.General, tick: 129));
        Assert.False(store.IsPveImmune(reused, VanillaPlayerImmunityChannel1458.BossNoCheese, tick: 139));

        store.RecordPve(reused, VanillaPlayerImmunityChannel1458.General, immuneUntil: 150);
        Assert.True(store.IsPveImmune(reused, VanillaPlayerImmunityChannel1458.General, tick: 149));
        Assert.False(store.IsPveImmune(reused, VanillaPlayerImmunityChannel1458.BossNoCheese, tick: 139));
    }
}
