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

        Assert.True(RuntimeNpcPacketProjection.TryCreate(
            in npc,
            RuntimeNpcSyncKind.Spawn,
            out var state));

        Assert.Equal((byte)1, state.Generation);
        Assert.Equal(VanillaNpcIds.BlueSlime.Value, state.NpcType);
        Assert.Equal((short)-3, state.NpcNetId);
        // -3 = Green Slime variant (scale 0.9, life 14) applied via VanillaNpcNetVariantCatalog.
        Assert.Equal(14, state.Life);
        Assert.Equal(14, state.LifeMax);
        Assert.True(state.SpawnNeedsSyncing);
        Assert.Equal(-1, state.SpriteDirection);
    }

    [Fact]
    public void Zombie_projection_carries_authoritative_sprite_direction_and_life()
    {
        NpcSnapshot npc = CreateNpc(type: 3, netId: 3, generation: 7) with
        {
            Simulation = NpcSimulationState.Initial with
            {
                DirectionX = -1,
                DirectionY = 1,
                SpriteDirection = 1,
                Life = 17,
                LifeMax = 45,
                TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft
            }
        };

        Assert.True(RuntimeNpcPacketProjection.TryCreate(
            in npc,
            RuntimeNpcSyncKind.Update,
            out var state));

        Assert.Equal(VanillaNpcIds.Zombie.Value, state.NpcType);
        Assert.Equal((short)3, state.NpcNetId);
        Assert.Equal(-1, state.DirectionX);
        Assert.Equal(1, state.SpriteDirection);
        Assert.Equal(17, state.Life);
        Assert.Equal(45, state.LifeMax);
        Assert.False(state.SpawnNeedsSyncing);
    }

    [Fact]
    public void Despawn_projection_keeps_generation_and_identity_but_sends_zero_life()
    {
        NpcSnapshot npc = CreateNpc(type: 2, netId: 2, generation: 255);

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
    public void King_slime_projection_applies_live_hitbox_sync_anchor()
    {
        NpcSnapshot npc = CreateNpc(
            type: VanillaNpcIds.KingSlime.Value,
            netId: checked((short)VanillaNpcIds.KingSlime.Value),
            generation: 1) with
        {
            Simulation = NpcSimulationState.Initial with
            {
                Scale = 1.25f,
                Life = 2_000,
                LifeMax = 2_000
            }
        };

        Assert.True(RuntimeNpcPacketProjection.TryCreate(
            in npc,
            RuntimeNpcSyncKind.Update,
            out var state));

        Assert.Equal(161f, state.PositionX);
        Assert.Equal(315f, state.PositionY);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(21)]
    public void Every_other_admitted_definition_crosses_packet_projection(int rawType)
    {
        NpcSnapshot npc = CreateNpc(rawType, checked((short)rawType), generation: 1);

        Assert.True(RuntimeNpcPacketProjection.TryCreate(
            in npc,
            RuntimeNpcSyncKind.Update,
            out var state));
        Assert.Equal(rawType, state.NpcType);
    }

    [Fact]
    public void Unverified_type_is_not_fabricated_for_network_sync()
    {
        // 99 = SeekerBody is now admitted via VanillaWormNpcCatalog; use 900 as truly unverified.
        NpcSnapshot npc = CreateNpc(type: 900, netId: 900, generation: 1);

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
