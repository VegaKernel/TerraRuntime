using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcPhysicalHitboxTests
{
    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, -1)]
    [InlineData(4097, 100)]
    [InlineData(100, int.MaxValue)]
    public void Invalid_physical_body_never_commits_or_projects(int width, int height)
    {
        var store = new RuntimeNpcStore(4);
        var update = CreateUpdate() with { Simulation = NpcSimulationState.Initial with
            { HitboxOverride = new NpcHitboxDimensions(width, height) } };
        Assert.False(update.Simulation.IsValid);
        Assert.False(store.TrySpawn(0, in update, out _));
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.DetonatingBubble, out var definition));
        Assert.False(definition.TryResolveHitbox(update.Simulation, out _));
    }

    [Fact]
    public void Body_is_preserved_by_same_definition_update_but_not_new_definition_defaults()
    {
        var store = new RuntimeNpcStore(4);
        var update = CreateUpdate() with { Simulation = NpcSimulationState.Initial with
            { HitboxOverride = new NpcHitboxDimensions(100, 100) } };
        Assert.True(store.TrySpawn(0, in update, out var spawned));
        var stateOnly = CreateUpdate();
        Assert.True(store.TryUpdate(spawned.Handle, in stateOnly, out var retained));
        Assert.Equal(new NpcHitboxDimensions(100, 100), retained.Simulation.HitboxOverride);
        var transformed = stateOnly with { Type = VanillaNpcIds.Zombie.Value, NetId = (short)VanillaNpcIds.Zombie.Value };
        Assert.True(store.TryUpdate(spawned.Handle, in transformed, out var changed));
        Assert.Null(changed.Simulation.HitboxOverride);
    }

    [Theory]
    [InlineData(.8f)]
    [InlineData(1.2f)]
    public void Packet_anchor_uses_physical_body_not_visual_scale(float scale)
    {
        var store = new RuntimeNpcStore(4);
        var update = CreateUpdate();
        Assert.True(store.TrySpawn(0, in update, out var spawned));
        update = update with { Simulation = spawned.Simulation with
            { Scale = scale, HitboxOverride = new NpcHitboxDimensions(100, 100) } };
        Assert.True(store.TryUpdate(spawned.Handle, in update, out var expanded));
        Assert.True(RuntimeNpcPacketProjection.TryCreate(expanded, RuntimeNpcSyncKind.Update, out var packet));
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.DetonatingBubble, out var definition));
        Assert.Equal(68f + 100f * definition.SyncAnchor.X, packet.PositionX);
        Assert.Equal(68f + 100f * definition.SyncAnchor.Y, packet.PositionY);
    }

    private static NpcStateUpdate CreateUpdate() => new(
        VanillaNpcIds.DetonatingBubble.Value, (short)VanillaNpcIds.DetonatingBubble.Value,
        68, 68, 0, 0, 0, default, NpcSimulationState.Initial);
}
