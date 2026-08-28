using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcPacketProjectionTests
{
    [Theory]
    [InlineData(1UL, 1)]
    [InlineData(255UL, 255)]
    [InlineData(256UL, 1)]
    [InlineData(510UL, 255)]
    [InlineData(511UL, 1)]
    public void Runtime_generation_maps_to_vanilla_nonzero_byte_cycle(ulong runtimeGeneration, byte expected)
    {
        Assert.Equal(
            expected,
            RuntimeNpcPacketProjection.ToProtocolGeneration(new NpcGeneration(runtimeGeneration)));
    }

    [Fact]
    public void Spawn_projection_marks_sync_and_uses_catalog_full_life()
    {
        NpcSnapshot npc = CreateNpc(type: 1, netId: -3, generation: 256);

        Assert.Equal(VanillaNpcIds.BlueSlime, npc.TypeIdentity);
        Assert.Equal(new NpcNetId(-3), npc.NetIdentity);
        Assert.True(RuntimeNpcPacketProjection.TryCreate(
            in npc,
            RuntimeNpcSyncKind.Spawn,
            out var state));

        Assert.Equal((byte)1, state.Generation);
        Assert.Equal(VanillaNpcIds.BlueSlime.Value, state.NpcType);
        Assert.Equal((short)-3, state.NpcNetId);
        Assert.Equal(25, state.Life);
        Assert.Equal(25, state.LifeMax);
        Assert.True(state.SpawnNeedsSyncing);
        Assert.Equal(-1, state.SpriteDirection);
    }

    [Fact]
    public void Despawn_projection_keeps_generation_and_identity_but_sends_zero_life()
    {
        NpcSnapshot npc = CreateNpc(type: 2, netId: 2, generation: 255);

        Assert.Equal(VanillaNpcIds.DemonEye, npc.TypeIdentity);
        Assert.Equal(new NpcNetId(2), npc.NetIdentity);
        Assert.True(RuntimeNpcPacketProjection.TryCreate(
            in npc,
            RuntimeNpcSyncKind.Despawn,
            out var state));

        Assert.Equal((byte)255, state.Generation);
        Assert.Equal(0, state.Life);
        Assert.Equal(60, state.LifeMax);
        Assert.False(state.SpawnNeedsSyncing);
    }

    [Fact]
    public void Unverified_type_is_not_fabricated_for_network_sync()
    {
        NpcSnapshot npc = CreateNpc(type: 99, netId: 99, generation: 1);

        Assert.False(RuntimeNpcPacketProjection.TryCreate(
            in npc,
            RuntimeNpcSyncKind.Update,
            out _));
    }

    private static NpcSnapshot CreateNpc(int type, short netId, ulong generation) =>
        new(
            Handle: new NpcHandle(7, new NpcGeneration(generation)),
            Revision: new NpcRevision(3),
            Type: type,
            NetId: netId,
            PositionX: 100f,
            PositionY: 200f,
            VelocityX: 1f,
            VelocityY: -2f,
            Target: 5,
            Ai: new NpcAiState(1f, 2f, 3f, 4f),
            Simulation: NpcSimulationState.Initial with
            {
                DirectionX = 1,
                DirectionY = -1
            });
}
