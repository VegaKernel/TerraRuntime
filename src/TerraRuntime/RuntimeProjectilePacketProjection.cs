using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol;

namespace TerraRuntime;

/// <summary>
/// Converts generation-safe authoritative projectile state into protocol-neutral packet projections.
/// Runtime generations stay monotonic ulongs; only the wire projection wraps into ProjectileKey's 14-bit field.
/// </summary>
internal static class RuntimeProjectilePacketProjection
{
    public static bool TryCreateUpdate(
        in ProjectileSnapshot projectile,
        out TerrariaProjectileUpdateState state)
    {
        if (!projectile.IsActive ||
            projectile.Handle.Slot > TerrariaProjectileKeyState.MaximumProjectileIndex)
        {
            state = default;
            return false;
        }

        ProjectileAiState ai = projectile.Ai;
        state = new TerrariaProjectileUpdateState(
            Key: CreateKey(in projectile),
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
        if (!projectile.IsActive ||
            projectile.Handle.Slot > TerrariaProjectileKeyState.MaximumProjectileIndex)
        {
            state = default;
            return false;
        }

        state = new TerrariaProjectileDestroyState(
            Key: CreateKey(in projectile),
            PositionX: projectile.PositionX,
            PositionY: projectile.PositionY);
        return state.IsValid;
    }

    internal static ushort ToProtocolGeneration(ProjectileGeneration generation)
    {
        if (!generation.IsAssigned)
            throw new ArgumentOutOfRangeException(nameof(generation));

        ulong zeroBased =
            (generation.Value - 1UL) % TerrariaProjectileKeyState.MaximumGeneration;
        return checked((ushort)(zeroBased + 1UL));
    }

    private static TerrariaProjectileKeyState CreateKey(in ProjectileSnapshot projectile) =>
        new(
            Spawner: projectile.Spawner,
            ProjectileIndex: projectile.Handle.Slot,
            Generation: ToProtocolGeneration(projectile.Handle.Generation));
}
