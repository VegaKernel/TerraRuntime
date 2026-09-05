using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

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
/// Lock-free staged control registry for one authoritative NPC store. Each mutation clones the latest immutable
/// staging image and publishes it with CAS. CommitPending() snapshots one staging version at the authoritative tick
/// boundary; a concurrent newer mutation remains staged automatically for the next tick instead of being lost.
/// </summary>
public sealed class RuntimeNpcActorControlRegistry
{
    private readonly RuntimeNpcStore _npcs;
    private RuntimeNpcActorControlSnapshot _published;
    private StagedImage _staged;
    private ulong _committedStagingVersion;
    private ulong _nextRevision;

    public RuntimeNpcActorControlRegistry(RuntimeNpcStore npcs)
    {
        ArgumentNullException.ThrowIfNull(npcs);
        _npcs = npcs;
        var empty = new NpcActorControlBinding?[npcs.Capacity];
        _staged = new StagedImage(empty, version: 0);
        _published = new RuntimeNpcActorControlSnapshot(empty, revision: 0);
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

        while (true)
        {
            StagedImage current = Volatile.Read(ref _staged);
            NpcActorControlBinding? existing = current.Bindings[npc.Slot];
            if (existing is not null && existing.Value.Npc == npc)
            {
                lease = null;
                return NpcActorControlAcquireResult.AlreadyControlled;
            }

            NpcActorControlBinding?[] nextBindings = CloneBindings(current);
            nextBindings[npc.Slot] = new NpcActorControlBinding(
                controllerId,
                npc,
                NpcActorIntent.Stop());
            StagedImage next = CreateNext(current, nextBindings);
            if (!ReferenceEquals(Interlocked.CompareExchange(ref _staged, next, current), current))
                continue;

            lease = new NpcActorControlLease(this, npc, controllerId);
            return NpcActorControlAcquireResult.Acquired;
        }
    }

    /// <summary>
    /// Publishes one immutable staged image without taking a monitor. If a newer staging image races this commit,
    /// the newer version remains in <see cref="_staged"/> and is committed at the following tick boundary.
    /// </summary>
    public bool CommitPending()
    {
        StagedImage staged = Volatile.Read(ref _staged);
        if (staged.Version == _committedStagingVersion)
            return false;

        if (_nextRevision == ulong.MaxValue)
            throw new InvalidOperationException("NPC actor control snapshot revision exhausted.");

        _nextRevision++;
        Volatile.Write(
            ref _published,
            new RuntimeNpcActorControlSnapshot(staged.Bindings, _nextRevision));
        _committedStagingVersion = staged.Version;
        return true;
    }

    internal bool TrySetIntent(
        NpcHandle npc,
        ActorControllerId controllerId,
        in NpcActorIntent intent)
    {
        if (!intent.IsValid || !npc.IsAssigned || npc.Slot >= _npcs.Capacity)
            return false;

        while (true)
        {
            StagedImage current = Volatile.Read(ref _staged);
            NpcActorControlBinding? existing = current.Bindings[npc.Slot];
            if (existing is null ||
                existing.Value.Npc != npc ||
                existing.Value.ControllerId != controllerId)
            {
                return false;
            }

            NpcActorControlBinding?[] nextBindings = CloneBindings(current);
            nextBindings[npc.Slot] = existing.Value with { Intent = intent };
            StagedImage next = CreateNext(current, nextBindings);
            if (ReferenceEquals(Interlocked.CompareExchange(ref _staged, next, current), current))
                return true;
        }
    }

    internal void Release(NpcHandle npc, ActorControllerId controllerId)
    {
        if (!npc.IsAssigned || npc.Slot >= _npcs.Capacity)
            return;

        while (true)
        {
            StagedImage current = Volatile.Read(ref _staged);
            NpcActorControlBinding? existing = current.Bindings[npc.Slot];
            if (existing is null ||
                existing.Value.Npc != npc ||
                existing.Value.ControllerId != controllerId)
            {
                return;
            }

            NpcActorControlBinding?[] nextBindings = CloneBindings(current);
            nextBindings[npc.Slot] = null;
            StagedImage next = CreateNext(current, nextBindings);
            if (ReferenceEquals(Interlocked.CompareExchange(ref _staged, next, current), current))
                return;
        }
    }

    private static NpcActorControlBinding?[] CloneBindings(StagedImage image) =>
        (NpcActorControlBinding?[])image.Bindings.Clone();

    private static StagedImage CreateNext(StagedImage current, NpcActorControlBinding?[] bindings)
    {
        if (current.Version == ulong.MaxValue)
            throw new InvalidOperationException("NPC actor control staging version exhausted.");

        return new StagedImage(bindings, current.Version + 1);
    }

    private sealed class StagedImage(NpcActorControlBinding?[] bindings, ulong version)
    {
        public NpcActorControlBinding?[] Bindings { get; } = bindings;
        public ulong Version { get; } = version;
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
