using global::Multiplicity.Packets;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaNpcUpdateEncoderTests
{
    [Fact]
    public void Encodes_full_life_spawn_with_generation_direction_ai_and_spawn_flag()
    {
        TerrariaNpcUpdateState state = CreateState(
            generation: 2,
            life: 25,
            lifeMax: 25,
            spawnNeedsSyncing: true) with
        {
            DirectionX = 1,
            DirectionY = -1,
            SpriteDirection = -1,
            Ai0 = 1.25f
        };

        Assert.True(TerrariaNpcUpdateEncoder.TryEncode(in state, out byte[] encoded));
        NpcUpdate packet = Assert.IsType<NpcUpdate>(
            TerrariaPacket.Deserialize((ReadOnlyMemory<byte>)encoded));

        Assert.Equal((byte)5, packet.NpcSlot);
        Assert.Equal((byte)2, packet.Generation);
        Assert.Equal(1, packet.NpcType);
        Assert.Equal(100f, packet.PositionX);
        Assert.Equal(200f, packet.PositionY);
        Assert.Equal(1.5f, packet.VelocityX);
        Assert.Equal(-2.5f, packet.VelocityY);
        Assert.Equal((ushort)7, packet.Target);
        Assert.True((packet.Flags & NpcUpdateFlags.DirectionXPositive) != 0);
        Assert.False((packet.Flags & NpcUpdateFlags.DirectionYPositive) != 0);
        Assert.False((packet.Flags & NpcUpdateFlags.SpriteDirectionPositive) != 0);
        Assert.True((packet.Flags & NpcUpdateFlags.HasAI0) != 0);
        Assert.True((packet.Flags & NpcUpdateFlags.LifeIsFull) != 0);
        Assert.True((packet.ExtraFlags & NpcUpdateExtraFlags.SpawnNeedsSyncing) != 0);
        Assert.Equal(1.25f, packet.AI[0]);
        Assert.Equal((short)1, packet.NpcNetId);
    }

    [Fact]
    public void Negative_variant_net_id_must_resolve_to_the_declared_gameplay_type()
    {
        TerrariaNpcUpdateState blueSlimeVariant = CreateState(1, 25, 25, false) with
        {
            NpcType = 1,
            NpcNetId = -3
        };
        TerrariaNpcUpdateState mismatch = blueSlimeVariant with { NpcType = 2 };

        Assert.True(TerrariaNpcUpdateEncoder.TryEncode(in blueSlimeVariant, out byte[] encoded));
        NpcUpdate packet = Assert.IsType<NpcUpdate>(
            TerrariaPacket.Deserialize((ReadOnlyMemory<byte>)encoded));
        Assert.Equal(1, packet.NpcType);
        Assert.Equal((short)-3, packet.NpcNetId);
        Assert.False(TerrariaNpcUpdateEncoder.TryEncode(in mismatch, out _));
    }

    [Fact]
    public void Despawn_uses_vanilla_life_width_selected_from_life_max()
    {
        TerrariaNpcUpdateState state = CreateState(
            generation: 255,
            life: 0,
            lifeMax: 200,
            spawnNeedsSyncing: false);

        Assert.True(TerrariaNpcUpdateEncoder.TryEncode(in state, out byte[] encoded));
        NpcUpdate packet = Assert.IsType<NpcUpdate>(
            TerrariaPacket.Deserialize((ReadOnlyMemory<byte>)encoded));

        Assert.False((packet.Flags & NpcUpdateFlags.LifeIsFull) != 0);
        Assert.Equal((byte)2, packet.LifeBytes);
        Assert.Equal(0, packet.Life);
        Assert.False((packet.ExtraFlags & NpcUpdateExtraFlags.SpawnNeedsSyncing) != 0);
    }

    [Fact]
    public void Rejects_zero_generation_and_invalid_target()
    {
        TerrariaNpcUpdateState zeroGeneration = CreateState(0, 25, 25, false);
        TerrariaNpcUpdateState invalidTarget = CreateState(1, 25, 25, false) with
        {
            Target = ushort.MaxValue
        };

        Assert.False(TerrariaNpcUpdateEncoder.TryEncode(in zeroGeneration, out _));
        Assert.False(TerrariaNpcUpdateEncoder.TryEncode(in invalidTarget, out _));
    }

    private static TerrariaNpcUpdateState CreateState(
        byte generation,
        int life,
        int lifeMax,
        bool spawnNeedsSyncing) =>
        new(
            NpcSlot: 5,
            Generation: generation,
            NpcType: 1,
            PositionX: 100f,
            PositionY: 200f,
            VelocityX: 1.5f,
            VelocityY: -2.5f,
            Target: 7,
            DirectionX: -1,
            DirectionY: 1,
            SpriteDirection: -1,
            Ai0: 0f,
            Ai1: 0f,
            Ai2: 0f,
            Ai3: 0f,
            NpcNetId: 1,
            Life: life,
            LifeMax: lifeMax,
            SpawnNeedsSyncing: spawnNeedsSyncing);
}
