using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class RuntimeProjectilePacketProjectionTests
{
    [Fact]
    public void Update_projection_preserves_authoritative_state_and_key_identity()
    {
        ProjectileSnapshot projectile = CreateSnapshot(slot: 17, generation: 23) with
        {
            Ai = new ProjectileAiState(1f, 2f, 3f),
            Damage = 44,
            KnockBack = 2.25f,
            OriginalDamage = 55
        };

        Assert.True(RuntimeProjectilePacketProjection.TryCreateUpdate(
            in projectile,
            out TerrariaProjectileUpdateState state));

        Assert.Equal((byte)4, state.Key.Spawner);
        Assert.Equal((ushort)17, state.Key.ProjectileIndex);
        Assert.Equal((ushort)23, state.Key.Generation);
        Assert.Equal(14, state.ProjectileType);
        Assert.Equal(100f, state.PositionX);
        Assert.Equal(200f, state.PositionY);
        Assert.Equal(5f, state.VelocityX);
        Assert.Equal(-6f, state.VelocityY);
        Assert.Equal(1f, state.Ai0);
        Assert.Equal(2f, state.Ai1);
        Assert.Equal(3f, state.Ai2);
        Assert.Equal((short)44, state.Damage);
        Assert.Equal(2.25f, state.KnockBack);
        Assert.Equal((short)55, state.OriginalDamage);
    }

    [Fact]
    public void Runtime_generation_wraps_only_at_the_14_bit_wire_boundary()
    {
        Assert.Equal((ushort)1,
            RuntimeProjectilePacketProjection.ToProtocolGeneration(new ProjectileGeneration(1)));
        Assert.Equal((ushort)16383,
            RuntimeProjectilePacketProjection.ToProtocolGeneration(new ProjectileGeneration(16383)));
        Assert.Equal((ushort)1,
            RuntimeProjectilePacketProjection.ToProtocolGeneration(new ProjectileGeneration(16384)));
        Assert.Equal((ushort)2,
            RuntimeProjectilePacketProjection.ToProtocolGeneration(new ProjectileGeneration(16385)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RuntimeProjectilePacketProjection.ToProtocolGeneration(default));
    }

    [Fact]
    public void Destroy_projection_keeps_exact_final_key_and_position()
    {
        ProjectileSnapshot projectile = CreateSnapshot(slot: 1000, generation: 16384);

        Assert.True(RuntimeProjectilePacketProjection.TryCreateDestroy(
            in projectile,
            out TerrariaProjectileDestroyState state));

        Assert.Equal((byte)4, state.Key.Spawner);
        Assert.Equal((ushort)1000, state.Key.ProjectileIndex);
        Assert.Equal((ushort)1, state.Key.Generation);
        Assert.Equal(100f, state.PositionX);
        Assert.Equal(200f, state.PositionY);
    }

    [Fact]
    public void Inactive_protocol_unaddressable_or_unknown_wire_type_snapshot_is_rejected()
    {
        ProjectileSnapshot inactive = default;
        ProjectileSnapshot oversizedSlot = CreateSnapshot(slot: 1001, generation: 1);
        ProjectileSnapshot noneType = CreateSnapshot(slot: 1, generation: 1) with
        {
            Type = VanillaProjectileIds.None
        };
        ProjectileSnapshot unknownType = CreateSnapshot(slot: 1, generation: 1) with
        {
            Type = new ProjectileTypeId(VanillaProjectileIds.Count)
        };

        Assert.False(RuntimeProjectilePacketProjection.TryCreateUpdate(in inactive, out _));
        Assert.False(RuntimeProjectilePacketProjection.TryCreateDestroy(in inactive, out _));
        Assert.False(RuntimeProjectilePacketProjection.TryCreateUpdate(in oversizedSlot, out _));
        Assert.False(RuntimeProjectilePacketProjection.TryCreateDestroy(in oversizedSlot, out _));
        Assert.False(RuntimeProjectilePacketProjection.TryCreateUpdate(in noneType, out _));
        Assert.False(RuntimeProjectilePacketProjection.TryCreateUpdate(in unknownType, out _));

        Assert.True(RuntimeProjectilePacketProjection.TryCreateDestroy(in noneType, out _));
        Assert.True(RuntimeProjectilePacketProjection.TryCreateDestroy(in unknownType, out _));
    }

    private static ProjectileSnapshot CreateSnapshot(ushort slot, ulong generation) =>
        new(
            Handle: new ProjectileHandle(slot, new ProjectileGeneration(generation)),
            Revision: new ProjectileRevision(1),
            Type: new ProjectileTypeId(14),
            Spawner: 4,
            PositionX: 100f,
            PositionY: 200f,
            VelocityX: 5f,
            VelocityY: -6f,
            Ai: new ProjectileAiState(0f, 0f, 0f),
            BannerIdToRespondTo: 7,
            Damage: 0,
            KnockBack: 0f,
            OriginalDamage: 0);
}
