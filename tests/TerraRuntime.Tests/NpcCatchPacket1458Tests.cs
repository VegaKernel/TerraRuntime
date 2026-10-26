using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class NpcCatchPacket1458Tests
{
    [Fact]
    public void Statue_catch_smoke_matches_original_packet106_and_HalfVector2_rounding()
    {
        // Genuine original Linux and Windows CatchNPC: Center(648.57,1590)-(20,20),
        // then SendData's int X argument truncates 628.57 before HalfVector2 packing.
        Assert.True(TerrariaPoofOfSmokeCodec1458.TryEncode(628, 1570f, out var actual));
        Assert.Equal("07006AE8602266", Convert.ToHexString(actual));

        // Independent original HalfVector2 oracle on both platforms, including ties-to-even,
        // signed zero and half overflow. These packed words are observations, not our decoder.
        (float Input, ushort Packed)[] original =
        [
            (0f, 0x0000), (BitConverter.Int32BitsToSingle(int.MinValue), 0x8000),
            (1f, 0x3c00), (809f, 0x6252), (453.125f, 0x5f14),
            (453.375f, 0x5f16), (453.625f, 0x5f16), (453.875f, 0x5f18),
            (-453.125f, 0xdf14), (65504f, 0x7bff), (65520f, 0x7c00)
        ];
        foreach (var sample in original)
        {
            Assert.True(TerrariaPoofOfSmokeCodec1458.TryEncode(0, sample.Input, out actual));
            Assert.Equal($"07006A0000{sample.Packed & 255:X2}{sample.Packed >> 8:X2}", Convert.ToHexString(actual));
        }
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Assert.False(TerrariaPoofOfSmokeCodec1458.TryEncode(628, invalid, out actual));
            Assert.Empty(actual);
        }
    }

    [Fact]
    public void Live_statue_body_projects_original_active_and_inactive23_before_smoke()
    {
        NpcSnapshot npc = Statue();
        Assert.True(RuntimeNpcPacketProjection.TryCreate(in npc, RuntimeNpcSyncKind.Update, out var active));
        Assert.True(TerrariaNpcUpdateEncoder.TryEncode(in active, out var encoded));
        Assert.Equal("24001700017BE41F440080C54485EB113F000000000000CD020000803F00001C428602FF",
            Convert.ToHexString(encoded));
        Assert.True(RuntimeNpcPacketProjection.TryCreate(in npc, RuntimeNpcSyncKind.Despawn, out var inactive));
        Assert.True(TerrariaNpcUpdateEncoder.TryEncode(in inactive, out encoded));
        Assert.Equal("26001700017BE41F440080C54485EB113F0000000000004D020000803F00001C4286020100FF",
            Convert.ToHexString(encoded));
    }

    [Theory]
    [InlineData(null, 255)]
    [InlineData(-1, 255)]
    [InlineData(0, 0)]
    [InlineData(7, 7)]
    [InlineData(254, 254)]
    [InlineData(255, 255)]
    [InlineData(256, 255)]
    public void Release_owner_projects_only_an_owned_byte_without_losing_the_statue_flag(int? owner, byte expected)
    {
        var npc = Statue();
        npc = npc with { Simulation = npc.Simulation with { ReleaseOwner = owner is int value ? checked((short)value) : null } };
        Assert.True(RuntimeNpcPacketProjection.TryCreate(in npc, RuntimeNpcSyncKind.Update, out var projection));
        Assert.Equal(expected, projection.ReleaseOwner);
        Assert.True(projection.SpawnedFromStatue);
        Assert.True(TerrariaNpcUpdateEncoder.TryEncode(in projection, out var encoded));
        Assert.Equal(expected, encoded[^1]);
        projection = projection with { SpawnedFromStatue = false };
        Assert.True(TerrariaNpcUpdateEncoder.TryEncode(in projection, out encoded));
        Assert.Equal(0, encoded[24] & 2);
    }

    private static NpcSnapshot Statue() => new(new(0, new(1)), new(1), 646, 646,
        639.57f, 1580f, .57f, 0f, 0, new(1f, 39f, 0f, 0f),
        NpcSimulationState.Initial with
        {
            DirectionX = 1, DirectionY = -1, SpriteDirection = 1, Life = 5, LifeMax = 5,
            HitboxOverride = new(18, 20), SpawnedFromStatue = true, ReleaseOwner = 255
        });
}
