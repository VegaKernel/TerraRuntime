using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

public enum NpcActorControlAcquireResult : byte
{
    Acquired = 0,
    InvalidActor = 1,
    InvalidController = 2,
    UnsupportedNpcType = 3,
    AlreadyControlled = 4
}

public readonly record struct NpcActorControlBinding(
    ActorControllerId ControllerId,
    NpcHandle Npc,
    NpcActorIntent Intent);

/// <summary>
/// Immutable hot-path view of actor controls. Commands are prepared on the control path and become visible only
/// after CommitPending(), which is intended to run on the authoritative tick boundary.
/// </summary>
public sealed class RuntimeNpcActorControlSnapshot
{
    private readonly NpcActorControlBinding?[] _bindings;

    internal RuntimeNpcActorControlSnapshot(NpcActorControlBinding?[] bindings, ulong revision)
    {
        _bindings = bindings;
        Revision = revision;
    }

    public ulong Revision { get; }

    public bool TryGet(NpcHandle npc, out NpcActorControlBinding binding)
    {
        if (!npc.IsAssigned || npc.Slot >= _bindings.Length)
        {
            binding = default;
            return false;
        }

        NpcActorControlBinding? candidate = _bindings[npc.Slot];
        if (candidate is null || candidate.Value.Npc != npc)
        {
            binding = default;
            return false;
        }

        binding = candidate.Value;
        return true;
    }
}

/// <summary>
/// Control-plane registry for one authoritative NPC store. The only initially supported physical actor family is
/// ordinary Zombie presentation because TerraRuntime already has source-backed walking/gravity/step/collision
/// motion for it. More motion families can be admitted deliberately as their authoritative physics paths exist.
/// </summary>
public sealed class RuntimeNpcActorControlRegistry
{
    private readonly object _gate = new();
    private readonly RuntimeNpcStore _npcs;
    private RuntimeNpcActorControlSnapshot _published;
    private NpcActorControlBinding?[]? _pending;
    private ulong _nextRevision;

    public RuntimeNpcActorControlRegistry(RuntimeNpcStore npcs)
    {
        ArgumentNullException.ThrowIfNull(npcs);
        _npcs = npcs;
        _published = new RuntimeNpcActorControlSnapshot(new NpcActorControlBinding?[npcs.Capacity], revision: 0);
    }

    public RuntimeNpcActorControlSnapshot Snapshot => Volatile.Read(ref _published);

    public NpcActorControlAcquireResult TryAcquire(
        NpcHandle npc,
        ActorControllerId controllerId,
        out NpcActorControlLease? lease)
    {
        if (!npc.IsAssigned || !_npcs.TryGet(npc, out NpcSnapshot snapshot))
        {
            lease = null;
            return NpcActorControlAcquireResult.InvalidActor;
        }

        if (!controllerId.IsAssigned)
        {
            lease = null;
            return NpcActorControlAcquireResult.InvalidController;
        }

        if (snapshot.TypeIdentity != VanillaNpcIds.Zombie)
        {
            lease = null;
            return NpcActorControlAcquireResult.UnsupportedNpcType;
        }

        lock (_gate)
        {
            NpcActorControlBinding?[] working = GetPendingForMutation();
            NpcActorControlBinding? current = working[npc.Slot];
            if (current is not null && current.Value.Npc == npc)
            {
                lease = null;
                return NpcActorControlAcquireResult.AlreadyControlled;
            }

            working[npc.Slot] = new NpcActorControlBinding(
                controllerId,
                npc,
                NpcActorIntent.Stop());
            lease = new NpcActorControlLease(this, npc, controllerId);
            return NpcActorControlAcquireResult.Acquired;
        }
    }

    /// <summary>Publishes all staged control changes atomically. Returns true only when a new snapshot was published.</summary>
    public bool CommitPending()
    {
        lock (_gate)
        {
            if (_pending is null)
                return false;

            if (_nextRevision == ulong.MaxValue)
                throw new InvalidOperationException("NPC actor control snapshot revision exhausted.");

            _nextRevision++;
            var snapshot = new RuntimeNpcActorControlSnapshot(_pending, _nextRevision);
            _pending = null;
            Volatile.Write(ref _published, snapshot);
            return true;
        }
    }

    internal bool TrySetIntent(
        NpcHandle npc,
        ActorControllerId controllerId,
        in NpcActorIntent intent)
    {
        if (!intent.IsValid)
            return false;

        lock (_gate)
        {
            NpcActorControlBinding?[] working = GetPendingForMutation();
            NpcActorControlBinding? current = working[npc.Slot];
            if (current is null ||
                current.Value.Npc != npc ||
                current.Value.ControllerId != controllerId)
            {
                return false;
            }

            working[npc.Slot] = current.Value with { Intent = intent };
            return true;
        }
    }

    internal void Release(NpcHandle npc, ActorControllerId controllerId)
    {
        lock (_gate)
        {
            NpcActorControlBinding?[] working = GetPendingForMutation();
            NpcActorControlBinding? current = working[npc.Slot];
            if (current is null ||
                current.Value.Npc != npc ||
                current.Value.ControllerId != controllerId)
            {
                return;
            }

            working[npc.Slot] = null;
        }
    }

    private NpcActorControlBinding?[] GetPendingForMutation()
    {
        if (_pending is not null)
            return _pending;

        RuntimeNpcActorControlSnapshot published = Snapshot;
        var copy = new NpcActorControlBinding?[_npcs.Capacity];
        for (int slot = 0; slot < copy.Length; slot++)
        {
            byte runtimeSlot = checked((byte)slot);
            if (_npcs.TryGetActive(runtimeSlot, out NpcSnapshot active) &&
                published.TryGet(active.Handle, out NpcActorControlBinding binding))
            {
                copy[slot] = binding;
            }
        }

        _pending = copy;
        return copy;
    }
}

/// <summary>
/// Exclusive controller ownership for one exact NPC generation. Disposing the lease stages retirement; the change
/// becomes visible to simulation on the next registry CommitPending().
/// </summary>
public sealed class NpcActorControlLease : IDisposable
{
    private RuntimeNpcActorControlRegistry? _registry;
    private readonly NpcHandle _npc;
    private readonly ActorControllerId _controllerId;

    internal NpcActorControlLease(
        RuntimeNpcActorControlRegistry registry,
        NpcHandle npc,
        ActorControllerId controllerId)
    {
        _registry = registry;
        _npc = npc;
        _controllerId = controllerId;
    }

    public NpcHandle Npc => _npc;

    public ActorControllerId ControllerId => _controllerId;

    public bool IsRetirementPending => _registry is null;

    public bool TryStop(NpcActorMotionOptions? motion = null) =>
        TrySet(NpcActorIntent.Stop(motion));

    public bool TryMoveTo(
        float targetX,
        float targetY,
        NpcActorMotionOptions? motion = null) =>
        TrySet(NpcActorIntent.MoveTo(targetX, targetY, motion));

    public bool TryFollowPlayer(
        PlayerHandle target,
        NpcActorMotionOptions? motion = null) =>
        TrySet(NpcActorIntent.FollowPlayer(target, motion));

    public void Dispose()
    {
        RuntimeNpcActorControlRegistry? registry = Interlocked.Exchange(ref _registry, null);
        registry?.Release(_npc, _controllerId);
    }

    private bool TrySet(NpcActorIntent intent)
    {
        RuntimeNpcActorControlRegistry? registry = Volatile.Read(ref _registry);
        return registry is not null && registry.TrySetIntent(_npc, _controllerId, in intent);
    }
}
