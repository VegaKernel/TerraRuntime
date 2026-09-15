using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

/// <summary>Read-only lookup of an opaque vanilla projectile key, independent of physical runtime slots.</summary>
public interface IVanillaNpcProjectileAnchorLookup
{
    bool TryGetProjectile(float keyBits, out ProjectileSnapshot projectile);

    /// <summary>Returns an active Moon Leech's retained key when its addressed player has the leech effect.</summary>
    bool TryGetHealingAnchor(ushort physicalSlot, out float keyBits) { keyBits = default; return false; }
}
