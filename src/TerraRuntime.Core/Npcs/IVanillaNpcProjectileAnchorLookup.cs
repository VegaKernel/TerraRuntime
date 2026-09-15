using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

/// <summary>Read-only lookup of an opaque vanilla projectile key, independent of physical runtime slots.</summary>
public interface IVanillaNpcProjectileAnchorLookup
{
    bool TryGetProjectile(float keyBits, out ProjectileSnapshot projectile);
}
