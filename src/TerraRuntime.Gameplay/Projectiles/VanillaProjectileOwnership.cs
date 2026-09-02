using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Projectiles;

/// <summary>
/// TerrariaServer 1.4.5.8 projectile ownership sentinels. Player projectile owners occupy 0..254;
/// 255 is the reserved non-player/server owner used by dedicated-server projectile paths. The official
/// Projectile.Update source guards player-array access with <c>owner &lt; 255</c> and handles owner 255
/// separately, so runtime code must not mistake the sentinel for a real player slot.
/// </summary>
public static class VanillaProjectileOwnership
{
    public const byte MaximumPlayerOwner = byte.MaxValue - 1;
    public const byte ServerOwner = byte.MaxValue;

    public static bool IsPlayerOwned(byte owner) => owner <= MaximumPlayerOwner;

    public static bool IsServerOwned(byte owner) => owner == ServerOwner;
}
