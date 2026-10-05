using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ServerPlayerHealthContext1458Tests
{
    [Fact]
    public void Source_birth_update_dead_outside_and_new_generation_retain_the_owned_health_context()
    {
        var identities = new ServerPlayerSlotRegistry(new PlayerSlotPool(1));
        var states = new ServerPlayerStateStore(identities, 1);
        var authority = new ServerPlayerAuthority(states, identities);
        var tiles = new WorldTileStore(new WorldDimensions(80, 200));
        var id = new ServerPlayerId("test:health-context");
        var created = authority.Create(id, 320, 320);
        Assert.True(created.IsCreated);
        Assert.True(states.TryGet(created.Player, out var birth));
        Assert.Equal(100, birth.DerivedLifeMax);
        Assert.Equal(100, birth.BaseLifeMax);
        Assert.Equal(new PlayerDebuffSnapshot1458(false, false, false), birth.Debuffs);
        var alive = new ServerPlayerVitalsState(400, 400, 20, 20);
        Assert.True(authority.SetVitals(id, in alive));
        authority.TickHealthContext(tiles);
        Assert.True(states.TryGet(created.Player, out var updated));
        Assert.Equal(400, updated.DerivedLifeMax);
        Assert.Equal(400, updated.BaseLifeMax);
        authority.TickHealthContext(tiles);
        Assert.True(states.TryGet(created.Player, out var unchanged));
        Assert.Equal(updated.Revision, unchanged.Revision);
        var dead = new ServerPlayerVitalsState(0, 500, 20, 20);
        Assert.True(authority.SetVitals(id, in dead));
        authority.TickHealthContext(tiles);
        Assert.True(states.TryGet(created.Player, out var retained));
        Assert.Equal(400, retained.DerivedLifeMax);
        Assert.Equal(500, retained.BaseLifeMax);
        Assert.True(authority.SetVitals(id, in alive));
        Assert.True(authority.TryTeleport(id, 0, 0));
        authority.TickHealthContext(tiles);
        Assert.True(states.TryGet(created.Player, out var outside));
        Assert.Equal(400, outside.DerivedLifeMax);
        Assert.True(authority.Despawn(id));
        var replacement = authority.Create(id, 320, 320);
        Assert.True(states.TryGet(replacement.Player, out var fresh));
        Assert.Equal(100, fresh.DerivedLifeMax);
        Assert.Equal(100, fresh.BaseLifeMax);
        Assert.False(states.TrySetDerivedLifeMax(in retained, 999));
    }

    [Fact]
    public void Health_context_commit_rejects_changed_revision_and_invalid_value_without_mutation()
    {
        var identities = new ServerPlayerSlotRegistry(new PlayerSlotPool(1));
        var states = new ServerPlayerStateStore(identities, 1);
        var authority = new ServerPlayerAuthority(states, identities);
        var created = authority.Create(new ServerPlayerId("test:health-currentness"), 320, 320);
        Assert.True(states.TryGet(created.Player, out var captured));
        Assert.True(states.TrySetMotion(created.Player, 321, 320, 0, 0, out var changed));
        Assert.False(states.TrySetDerivedLifeMax(in captured, 400));
        Assert.False(states.TrySetDerivedLifeMax(in changed, -1));
        Assert.True(states.TryGet(created.Player, out var after));
        Assert.Equal(changed, after);
    }
}
