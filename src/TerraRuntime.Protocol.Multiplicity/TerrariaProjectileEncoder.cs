using global::Multiplicity.Packets;
using global::Multiplicity.Packets.Models;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>
/// Serializes authoritative projectile lifecycle state through Multiplicity's protocol-326 packet models.
/// Packed identity and presence flags are adapter concerns and never leak into Core.
/// </summary>
public static class TerrariaProjectileEncoder
{
    public static bool TryEncodeUpdate(in TerrariaProjectileUpdateState state, out byte[] bytes)
    {
        if (!state.IsValid)
        {
            bytes = [];
            return false;
        }

        TerrariaProjectileKeyState key = state.Key;
        var packet = new ProjectileNew
        {
            Key = new ProjectileKey(key.Spawner, key.ProjectileIndex, key.Generation),
            PositionX = state.PositionX,
            PositionY = state.PositionY,
            VelocityX = state.VelocityX,
            VelocityY = state.VelocityY,
            Type = checked((short)state.ProjectileType),
            AI0 = state.Ai0,
            AI1 = state.Ai1,
            AI2 = state.Ai2,
            BannerIdToRespondTo = state.BannerIdToRespondTo,
            Damage = state.Damage,
            KnockBack = state.KnockBack,
            OriginalDamage = state.OriginalDamage
        };

        return packet.TrySerialize(out bytes);
    }

    public static bool TryEncodeDestroy(in TerrariaProjectileDestroyState state, out byte[] bytes)
    {
        if (!state.IsValid)
        {
            bytes = [];
            return false;
        }

        TerrariaProjectileKeyState key = state.Key;
        var packet = new ProjectileDestroy
        {
            Key = new ProjectileKey(key.Spawner, key.ProjectileIndex, key.Generation),
            PositionX = state.PositionX,
            PositionY = state.PositionY
        };

        return packet.TrySerialize(out bytes);
    }
}
