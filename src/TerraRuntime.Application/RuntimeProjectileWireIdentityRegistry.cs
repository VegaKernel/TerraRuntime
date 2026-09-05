using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

/// <summary>
/// Maintains the protocol-326 ProjectileKey lookup independently from TerraRuntime's generation-safe
/// physical projectile slots. Vanilla 1.4.5.8 addresses an inbound projectile through
/// keyToIndex[Spawner, Index] and then validates the complete key, while each physical projectile retains
/// its own exact key. A newer generation for the same (Spawner, Index) therefore replaces only the
/// forward lookup; an older still-live physical projectile keeps its reverse identity for replication.
/// </summary>
internal sealed class RuntimeProjectileWireIdentityRegistry
{
    private const int SpawnerCount = byte.MaxValue + 1;
    private const int WireIndexCount = TerrariaProjectileKeyState.MaximumProjectileIndex + 1;

    private readonly ForwardEntry[,] forward = new ForwardEntry[SpawnerCount, WireIndexCount];
    private readonly ReverseEntry[] reverse;

    public RuntimeProjectileWireIdentityRegistry(
        int runtimeCapacity = RuntimeProjectileStore.MaximumProtocolAddressableCapacity)
    {
        if (runtimeCapacity <= 0 || runtimeCapacity > RuntimeProjectileStore.MaximumProtocolAddressableCapacity)
            throw new ArgumentOutOfRangeException(nameof(runtimeCapacity));

        reverse = new ReverseEntry[runtimeCapacity];
    }

    public int RuntimeCapacity => reverse.Length;

    /// <summary>
    /// Resolves only the key currently selected by vanilla-style (Spawner, Index) lookup. A previous
    /// generation with the same pair intentionally stops resolving even if its physical projectile is
    /// still live and therefore remains available through <see cref="TryGetWireKey"/>.
    /// </summary>
    public bool TryResolve(in TerrariaProjectileKeyState key, out ProjectileHandle handle)
    {
        if (!key.IsValid)
        {
            handle = default;
            return false;
        }

        ref readonly ForwardEntry entry = ref forward[key.Spawner, key.ProjectileIndex];
        if (!entry.Handle.IsAssigned || entry.Key != key)
        {
            handle = default;
            return false;
        }

        handle = entry.Handle;
        return true;
    }

    /// <summary>
    /// Binds an exact wire key to one generation-safe runtime projectile. Rebinding the same wire
    /// (Spawner, Index) to a newer key shadows the old forward lookup without erasing the old handle's
    /// reverse key, matching TerrariaServer 1.4.5.8 keyToIndex behavior.
    /// </summary>
    public bool TryBind(in TerrariaProjectileKeyState key, ProjectileHandle handle)
    {
        if (!key.IsValid || !IsAddressable(handle))
            return false;

        ref ReverseEntry existingReverse = ref reverse[handle.Slot];
        if (existingReverse.Handle.IsAssigned &&
            (existingReverse.Handle != handle || existingReverse.Key != key))
        {
            TerrariaProjectileKeyState previousKey = existingReverse.Key;
            ClearForwardIfCurrent(in previousKey, existingReverse.Handle);
        }

        forward[key.Spawner, key.ProjectileIndex] = new ForwardEntry(key, handle);
        existingReverse = new ReverseEntry(key, handle);
        return true;
    }

    /// <summary>Returns the exact wire key retained by this physical runtime projectile generation.</summary>
    public bool TryGetWireKey(ProjectileHandle handle, out TerrariaProjectileKeyState key)
    {
        if (!IsAddressable(handle))
        {
            key = default;
            return false;
        }

        ref readonly ReverseEntry entry = ref reverse[handle.Slot];
        if (entry.Handle != handle)
        {
            key = default;
            return false;
        }

        key = entry.Key;
        return true;
    }

    /// <summary>
    /// Releases one exact runtime generation. The current forward mapping is cleared only when it still
    /// points at this exact binding; despawning an older shadowed projectile must not erase a newer key.
    /// </summary>
    public bool TryUnbind(ProjectileHandle handle, out TerrariaProjectileKeyState key)
    {
        if (!IsAddressable(handle))
        {
            key = default;
            return false;
        }

        ref ReverseEntry entry = ref reverse[handle.Slot];
        if (entry.Handle != handle)
        {
            key = default;
            return false;
        }

        key = entry.Key;
        entry = default;
        ClearForwardIfCurrent(in key, handle);
        return true;
    }

    private bool IsAddressable(ProjectileHandle handle) =>
        handle.IsAssigned && handle.Slot < reverse.Length;

    private void ClearForwardIfCurrent(
        in TerrariaProjectileKeyState key,
        ProjectileHandle handle)
    {
        if (!key.IsValid)
            return;

        ref ForwardEntry entry = ref forward[key.Spawner, key.ProjectileIndex];
        if (entry.Handle == handle && entry.Key == key)
            entry = default;
    }

    private readonly record struct ForwardEntry(
        TerrariaProjectileKeyState Key,
        ProjectileHandle Handle);

    private readonly record struct ReverseEntry(
        TerrariaProjectileKeyState Key,
        ProjectileHandle Handle);
}
