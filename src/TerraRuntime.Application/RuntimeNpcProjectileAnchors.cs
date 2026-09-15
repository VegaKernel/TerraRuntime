using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

internal sealed class RuntimeNpcProjectileAnchors(RuntimeProjectileStore projectiles, RuntimeProjectileWireIdentityRegistry identities)
    : IVanillaNpcProjectileAnchorLookup
{
    public bool TryGetProjectile(float keyBits, out ProjectileSnapshot projectile)
    {
        uint bits = BitConverter.SingleToUInt32Bits(keyBits);
        var key = new TerrariaProjectileKeyState((byte)bits, (ushort)((bits >> 8) & 1023), (ushort)(bits >> 18));
        projectile = default;
        return identities.TryResolve(in key, out var handle) && projectiles.TryGet(handle, out projectile);
    }
}
