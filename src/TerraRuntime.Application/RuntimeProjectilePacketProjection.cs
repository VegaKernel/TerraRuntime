using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol;

namespace TerraRuntime;

/// <summary>
/// Converts generation-safe authoritative projectile state into protocol-neutral packet projections.
/// Runtime generations stay monotonic ulongs; only server-created fallback identities wrap into
/// ProjectileKey's 14-bit generation field. Inbound client projectiles can supply their exact preserved
/// wire key so physical runtime slots never leak into protocol identity.
/// </summary>
internal static class RuntimeProjectilePacketProjection
{
    public static bool TryCreateUpdate(
        in ProjectileSnapshot projectile,
        out TerrariaProjectileUpdateState state)
    {
        if (!TryCreateCanonicalKey(in projectile, out TerrariaProjectileKeyState key))
        {
            state = default;
            return false;
        }

        return TryCreateUpdate(in projectile, in key, out state);
    }

    public static bool TryCreateUpdate(
        in ProjectileSnapshot projectile,
        in TerrariaProjectileKeyState key,
        out TerrariaProjectileUpdateState state)
    {
        if (!projectile.IsActive ||
            !key.IsValid ||
            key.Spawner != projectile.Spawner ||
            !VanillaProjectileLifecycleFacts.IsDefinedLiveType(projectile.Type))
        {
            state = default;
            return false;
        }

        ProjectileAiState ai = projectile.Ai;
        state = new TerrariaProjectileUpdateState(
            Key: key,
            ProjectileType: projectile.Type.Value,
            PositionX: projectile.PositionX,
            PositionY: projectile.PositionY,
            VelocityX: projectile.VelocityX,
            VelocityY: projectile.VelocityY,
            Ai0: ai.Ai0,
            Ai1: ai.Ai1,
            Ai2: ai.Ai2,
            BannerIdToRespondTo: projectile.BannerIdToRespondTo,
            Damage: projectile.Damage,
            KnockBack: projectile.KnockBack,
            OriginalDamage: projectile.OriginalDamage);
        return state.IsValid;
    }

    public static bool TryCreateDestroy(
        in ProjectileSnapshot projectile,
        out TerrariaProjectileDestroyState state)
    {
        if (!TryCreateCanonicalKey(in projectile, out TerrariaProjectileKeyState key))
        {
            state = default;
            return false;
        }

        return TryCreateDestroy(in projectile, in key, out state);
    }

    public static bool TryCreateDestroy(
        in ProjectileSnapshot projectile,
        in TerrariaProjectileKeyState key,
        out TerrariaProjectileDestroyState state)
    {
        if (!projectile.IsActive || !key.IsValid || key.Spawner != projectile.Spawner)
        {
            state = default;
            return false;
        }

        state = new TerrariaProjectileDestroyState(
            Key: key,
            PositionX: projectile.PositionX,
            PositionY: projectile.PositionY);
        return state.IsValid;
    }

    internal static bool TryCreateCanonicalKey(
        in ProjectileSnapshot projectile,
        out TerrariaProjectileKeyState key)
    {
        if (!projectile.IsActive ||
            projectile.Handle.Slot > TerrariaProjectileKeyState.MaximumProjectileIndex)
        {
            key = default;
            return false;
        }

        key = new TerrariaProjectileKeyState(
            Spawner: projectile.Spawner,
            ProjectileIndex: projectile.Handle.Slot,
            Generation: ToProtocolGeneration(projectile.Handle.Generation));
        return key.IsValid;
    }

    internal static ushort ToProtocolGeneration(ProjectileGeneration generation)
    {
        if (!generation.IsAssigned)
            throw new ArgumentOutOfRangeException(nameof(generation));

        ulong zeroBased =
            (generation.Value - 1UL) % TerrariaProjectileKeyState.MaximumGeneration;
        return checked((ushort)(zeroBased + 1UL));
    }
}
