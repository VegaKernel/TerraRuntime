using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

internal sealed class RuntimeNpcProjectileAnchors(RuntimeProjectileStore projectiles, RuntimeProjectileWireIdentityRegistry identities, PlayerAuthority? players = null)
    : IVanillaNpcProjectileAnchorLookup
{
    public bool TryGetHealingAnchor(ushort physicalSlot, out float keyBits)
    {
        keyBits = default;
        if (players is null || !projectiles.TryGetActive(physicalSlot, out var projectile) ||
            projectile.Type != VanillaProjectileIds.MoonLeech) return false;
        int slot = (int)projectile.Ai.Ai1;
        if ((uint)slot > byte.MaxValue || !players.HasMoonLeech((byte)slot) ||
            !identities.TryGetWireKey(projectile.Handle, out var key)) return false;
        uint bits = key.Spawner | ((uint)key.ProjectileIndex << 8) | ((uint)key.Generation << 18);
        keyBits = BitConverter.UInt32BitsToSingle(bits);
        return true;
    }

    public bool TryGetProjectile(float keyBits, out ProjectileSnapshot projectile)
    {
        uint bits = BitConverter.SingleToUInt32Bits(keyBits);
        var key = new TerrariaProjectileKeyState((byte)bits, (ushort)((bits >> 8) & 1023), (ushort)(bits >> 18));
        projectile = default;
        return identities.TryResolve(in key, out var handle) && projectiles.TryGet(handle, out projectile);
    }
}
