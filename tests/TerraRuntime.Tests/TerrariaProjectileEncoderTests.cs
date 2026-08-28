using global::Multiplicity.Packets;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaProjectileEncoderTests
{
    [Fact]
    public void Update_round_trips_key_state_and_presence_flags()
    {
        TerrariaProjectileUpdateState state = CreateUpdateState() with
        {
            Ai0 = 1.25f,
            Ai2 = -3.5f,
            BannerIdToRespondTo = 9,
            Damage = 42,
            KnockBack = 2.5f,
            OriginalDamage = 50
        };

        Assert.True(TerrariaProjectileEncoder.TryEncodeUpdate(in state, out byte[] encoded));
        ProjectileNew packet = Assert.IsType<ProjectileNew>(
            TerrariaPacket.Deserialize((ReadOnlyMemory<byte>)encoded));

        Assert.Equal((byte)3, packet.Key.Spawner);
        Assert.Equal((ushort)1000, packet.Key.Index);
        Assert.Equal((ushort)16383, packet.Key.Generation);
        Assert.Equal((short)14, packet.Type);
        Assert.Equal(100f, packet.PositionX);
        Assert.Equal(200f, packet.PositionY);
        Assert.Equal(4f, packet.VelocityX);
        Assert.Equal(-5f, packet.VelocityY);
        Assert.True((packet.Flags & ProjectileNewFlags.HasAI0) != 0);
        Assert.False((packet.Flags & ProjectileNewFlags.HasAI1) != 0);
        Assert.True((packet.Flags & ProjectileNewFlags.HasExtraFlags) != 0);
        Assert.True((packet.ExtraFlags & ProjectileNewExtraFlags.HasAI2) != 0);
        Assert.True((packet.Flags & ProjectileNewFlags.HasBannerIdToRespondTo) != 0);
        Assert.True((packet.Flags & ProjectileNewFlags.HasDamage) != 0);
        Assert.True((packet.Flags & ProjectileNewFlags.HasKnockBack) != 0);
        Assert.True((packet.Flags & ProjectileNewFlags.HasOriginalDamage) != 0);
        Assert.Equal(1.25f, packet.AI0);
        Assert.Equal(-3.5f, packet.AI2);
        Assert.Equal((ushort)9, packet.BannerIdToRespondTo);
        Assert.Equal((short)42, packet.Damage);
        Assert.Equal(2.5f, packet.KnockBack);
        Assert.Equal((short)50, packet.OriginalDamage);
    }

    [Fact]
    public void Zero_optional_values_do_not_force_presence_flags()
    {
        TerrariaProjectileUpdateState state = CreateUpdateState();

        Assert.True(TerrariaProjectileEncoder.TryEncodeUpdate(in state, out byte[] encoded));
        ProjectileNew packet = Assert.IsType<ProjectileNew>(
            TerrariaPacket.Deserialize((ReadOnlyMemory<byte>)encoded));

        Assert.Equal(ProjectileNewFlags.None, packet.Flags);
        Assert.Equal(ProjectileNewExtraFlags.None, packet.ExtraFlags);
    }

    [Fact]
    public void Destroy_round_trips_packed_key_and_final_position()
    {
        var state = new TerrariaProjectileDestroyState(
            new TerrariaProjectileKeyState(7, 21, 33),
            PositionX: 123.5f,
            PositionY: 456.25f);

        Assert.True(TerrariaProjectileEncoder.TryEncodeDestroy(in state, out byte[] encoded));
        ProjectileDestroy packet = Assert.IsType<ProjectileDestroy>(
            TerrariaPacket.Deserialize((ReadOnlyMemory<byte>)encoded));

        Assert.Equal((byte)7, packet.Key.Spawner);
        Assert.Equal((ushort)21, packet.Key.Index);
        Assert.Equal((ushort)33, packet.Key.Generation);
        Assert.Equal(123.5f, packet.PositionX);
        Assert.Equal(456.25f, packet.PositionY);
    }

    [Fact]
    public void Invalid_key_or_unrepresentable_type_is_rejected()
    {
        TerrariaProjectileUpdateState oversizedIndex = CreateUpdateState() with
        {
            Key = new TerrariaProjectileKeyState(3, 1001, 1)
        };
        TerrariaProjectileUpdateState zeroGeneration = CreateUpdateState() with
        {
            Key = new TerrariaProjectileKeyState(3, 1, 0)
        };
        TerrariaProjectileUpdateState oversizedType = CreateUpdateState() with
        {
            ProjectileType = short.MaxValue + 1
        };

        Assert.False(TerrariaProjectileEncoder.TryEncodeUpdate(in oversizedIndex, out _));
        Assert.False(TerrariaProjectileEncoder.TryEncodeUpdate(in zeroGeneration, out _));
        Assert.False(TerrariaProjectileEncoder.TryEncodeUpdate(in oversizedType, out _));
    }

    private static TerrariaProjectileUpdateState CreateUpdateState() =>
        new(
            Key: new TerrariaProjectileKeyState(3, 1000, 16383),
            ProjectileType: 14,
            PositionX: 100f,
            PositionY: 200f,
            VelocityX: 4f,
            VelocityY: -5f,
            Ai0: 0f,
            Ai1: 0f,
            Ai2: 0f,
            BannerIdToRespondTo: 0,
            Damage: 0,
            KnockBack: 0f,
            OriginalDamage: 0);
}
