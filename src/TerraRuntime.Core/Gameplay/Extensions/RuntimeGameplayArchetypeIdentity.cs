using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Extensions;

/// <summary>
/// Generation-safe server archetype identity for live NPCs. Vanilla NPCs deliberately have no binding. The
/// identity is runtime metadata and is never projected into the vanilla type/net-id fields sent to clients.
/// Wire this store into the authoritative NPC commit sink chain so ordinary despawn/slot reuse also clears it.
/// </summary>
public sealed class RuntimeNpcArchetypeIdentityStore : INpcStateCommitSink
{
    private readonly ArchetypeIdentitySlot[] slots;

    public RuntimeNpcArchetypeIdentityStore(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        if (capacity > RuntimeNpcStore.MaximumAddressableCapacity)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        slots = new ArchetypeIdentitySlot[capacity];
    }

    public int Capacity => slots.Length;

    public bool TryBind(NpcHandle handle, GameplayArchetypeId archetypeId) =>
        archetypeId.IsAssigned &&
        handle.IsAssigned &&
        handle.Slot < slots.Length &&
        slots[handle.Slot].TryBind(handle.Generation.Value, archetypeId);

    public bool TryGet(NpcHandle handle, out GameplayArchetypeId archetypeId)
    {
        if (handle.IsAssigned && handle.Slot < slots.Length)
            return slots[handle.Slot].TryGet(handle.Generation.Value, out archetypeId);

        archetypeId = default;
        return false;
    }

    public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot)
    {
        if (snapshot.Handle.Slot >= slots.Length)
            return;

        ref ArchetypeIdentitySlot slot = ref slots[snapshot.Handle.Slot];
        switch (kind)
        {
            case NpcStateCommitKind.Spawn:
                slot.ObserveSpawn(snapshot.Handle.Generation.Value);
                break;
            case NpcStateCommitKind.Despawn:
                slot.ObserveRetirement(snapshot.Handle.Generation.Value);
                break;
        }
    }
}

/// <summary>
/// Generation-safe server archetype identity for live projectiles. A new Spawn generation always clears the old
/// binding, including Terraria's in-place full-pool replacement path where no prior Despawn/Remove is emitted.
/// </summary>
public sealed class RuntimeProjectileArchetypeIdentityStore : IProjectileStateCommitSink
{
    private readonly ArchetypeIdentitySlot[] slots;

    public RuntimeProjectileArchetypeIdentityStore(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        if (capacity > RuntimeProjectileStore.MaximumProtocolAddressableCapacity)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        slots = new ArchetypeIdentitySlot[capacity];
    }

    public int Capacity => slots.Length;

    public bool TryBind(ProjectileHandle handle, GameplayArchetypeId archetypeId) =>
        archetypeId.IsAssigned &&
        handle.IsAssigned &&
        handle.Slot < slots.Length &&
        slots[handle.Slot].TryBind(handle.Generation.Value, archetypeId);

    public bool TryGet(ProjectileHandle handle, out GameplayArchetypeId archetypeId)
    {
        if (handle.IsAssigned && handle.Slot < slots.Length)
            return slots[handle.Slot].TryGet(handle.Generation.Value, out archetypeId);

        archetypeId = default;
        return false;
    }

    public void ProjectileStateCommitted(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot)
    {
        if (snapshot.Handle.Slot >= slots.Length)
            return;

        ref ArchetypeIdentitySlot slot = ref slots[snapshot.Handle.Slot];
        switch (kind)
        {
            case ProjectileStateCommitKind.Spawn:
                slot.ObserveSpawn(snapshot.Handle.Generation.Value);
                break;
            case ProjectileStateCommitKind.Despawn:
            case ProjectileStateCommitKind.Remove:
                slot.ObserveRetirement(snapshot.Handle.Generation.Value);
                break;
        }
    }
}

internal struct ArchetypeIdentitySlot
{
    private ulong generation;
    private bool active;
    private bool bound;
    private GameplayArchetypeId archetypeId;

    public void ObserveSpawn(ulong nextGeneration)
    {
        if (nextGeneration == 0 || nextGeneration < generation)
            return;

        if (nextGeneration != generation || !active)
        {
            generation = nextGeneration;
            active = true;
            bound = false;
            archetypeId = default;
        }
    }

    public void ObserveRetirement(ulong expectedGeneration)
    {
        if (!active || generation != expectedGeneration)
            return;

        active = false;
        bound = false;
        archetypeId = default;
    }

    public bool TryBind(ulong expectedGeneration, GameplayArchetypeId value)
    {
        if (!active || generation != expectedGeneration || !value.IsAssigned)
            return false;

        archetypeId = value;
        bound = true;
        return true;
    }

    public bool TryGet(ulong expectedGeneration, out GameplayArchetypeId value)
    {
        if (active && bound && generation == expectedGeneration)
        {
            value = archetypeId;
            return true;
        }

        value = default;
        return false;
    }
}
