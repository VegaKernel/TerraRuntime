using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Core;

public enum GameplayArchetypeRegistrationResult : byte
{
    Registered = 0,
    InvalidDescriptor = 1,
    DuplicateId = 2
}

public interface IGameplayArchetypeRegistrationLease : IDisposable
{
    GameplayArchetypeId Id { get; }

    bool IsRetirementPending { get; }

    bool IsRetired { get; }
}

/// <summary>
/// Immutable archetype image published only at an authoritative safe boundary. The contained dictionary never
/// escapes, so readers can resolve stable archetype identity without locking or observing partial registration.
/// </summary>
public sealed class RuntimeGameplayArchetypeSnapshot<TDescriptor>
    where TDescriptor : struct
{
    private readonly Dictionary<GameplayArchetypeId, TDescriptor> descriptors;

    internal RuntimeGameplayArchetypeSnapshot(
        ulong revision,
        Dictionary<GameplayArchetypeId, TDescriptor> descriptors)
    {
        Revision = revision;
        this.descriptors = descriptors;
    }

    public ulong Revision { get; }

    public int Count => descriptors.Count;

    public bool TryGet(GameplayArchetypeId id, out TDescriptor descriptor) => descriptors.TryGetValue(id, out descriptor);

    internal bool Contains(GameplayArchetypeId id) => descriptors.ContainsKey(id);
}

/// <summary>
/// Runtime-owned NPC archetype catalog. Presentation validation is intentionally tied to the source-backed NPC
/// defaults currently supported by this TerraRuntime build instead of accepting arbitrary positive wire IDs.
/// </summary>
public sealed class RuntimeNpcArchetypeRegistry
{
    private readonly RuntimeGameplayArchetypeRegistry<NpcArchetypeDescriptor> registry =
        new(static descriptor => descriptor.Id, IsValid);

    public RuntimeGameplayArchetypeSnapshot<NpcArchetypeDescriptor> Snapshot => registry.Snapshot;

    public bool HasPendingChanges => registry.HasPendingChanges;

    public GameplayArchetypeRegistrationResult TryRegister(
        NpcArchetypeDescriptor descriptor,
        out IGameplayArchetypeRegistrationLease? lease) =>
        registry.TryRegister(descriptor, out lease);

    public RuntimeGameplayArchetypeSnapshot<NpcArchetypeDescriptor> CommitPending() => registry.CommitPending();

    private static bool IsValid(NpcArchetypeDescriptor descriptor) =>
        descriptor.Id.IsAssigned &&
        descriptor.VanillaPresentationType.IsAssigned &&
        Enum.IsDefined(descriptor.Role) &&
        VanillaNpcDefinitionCatalog.TryGet(descriptor.VanillaPresentationType, out _);
}

/// <summary>
/// Runtime-owned projectile archetype catalog. A presentation is accepted only when the pinned vanilla projectile
/// lifecycle catalog recognizes it as a live wire type.
/// </summary>
public sealed class RuntimeProjectileArchetypeRegistry
{
    private readonly RuntimeGameplayArchetypeRegistry<ProjectileArchetypeDescriptor> registry =
        new(static descriptor => descriptor.Id, IsValid);

    public RuntimeGameplayArchetypeSnapshot<ProjectileArchetypeDescriptor> Snapshot => registry.Snapshot;

    public bool HasPendingChanges => registry.HasPendingChanges;

    public GameplayArchetypeRegistrationResult TryRegister(
        ProjectileArchetypeDescriptor descriptor,
        out IGameplayArchetypeRegistrationLease? lease) =>
        registry.TryRegister(descriptor, out lease);

    public RuntimeGameplayArchetypeSnapshot<ProjectileArchetypeDescriptor> CommitPending() => registry.CommitPending();

    private static bool IsValid(ProjectileArchetypeDescriptor descriptor) =>
        descriptor.Id.IsAssigned && VanillaProjectileLifecycleFacts.IsDefinedLiveType(descriptor.VanillaPresentationType);
}

internal sealed class RuntimeGameplayArchetypeRegistry<TDescriptor>
    where TDescriptor : struct
{
    private readonly object gate = new();
    private readonly Func<TDescriptor, GameplayArchetypeId> getId;
    private readonly Func<TDescriptor, bool> validate;
    private readonly Dictionary<GameplayArchetypeId, TDescriptor> staged = [];
    private readonly HashSet<GameplayArchetypeId> retiringIds = [];
    private RuntimeGameplayArchetypeSnapshot<TDescriptor> published = new(0, []);
    private ulong nextRevision;
    private bool dirty;

    public RuntimeGameplayArchetypeRegistry(
        Func<TDescriptor, GameplayArchetypeId> getId,
        Func<TDescriptor, bool> validate)
    {
        this.getId = getId ?? throw new ArgumentNullException(nameof(getId));
        this.validate = validate ?? throw new ArgumentNullException(nameof(validate));
    }

    public RuntimeGameplayArchetypeSnapshot<TDescriptor> Snapshot => Volatile.Read(ref published);

    public bool HasPendingChanges
    {
        get => Volatile.Read(ref dirty);
    }

    public GameplayArchetypeRegistrationResult TryRegister(
        TDescriptor descriptor,
        out IGameplayArchetypeRegistrationLease? lease)
    {
        lease = null;
        GameplayArchetypeId id = getId(descriptor);
        if (!id.IsAssigned || !validate(descriptor))
            return GameplayArchetypeRegistrationResult.InvalidDescriptor;

        lock (gate)
        {
            if (staged.ContainsKey(id) || retiringIds.Contains(id) || published.Contains(id))
                return GameplayArchetypeRegistrationResult.DuplicateId;

            staged.Add(id, descriptor);
            Volatile.Write(ref dirty, true);
            lease = new RegistrationLease(this, id);
            return GameplayArchetypeRegistrationResult.Registered;
        }
    }

    public RuntimeGameplayArchetypeSnapshot<TDescriptor> CommitPending()
    {
        if (!Volatile.Read(ref dirty))
            return published;

        lock (gate)
        {
            if (!dirty)
                return published;

            var descriptors = new Dictionary<GameplayArchetypeId, TDescriptor>(staged.Count);
            foreach ((GameplayArchetypeId id, TDescriptor descriptor) in staged)
            {
                if (!retiringIds.Contains(id))
                    descriptors.Add(id, descriptor);
            }

            ulong revision = nextRevision == ulong.MaxValue ? 1 : nextRevision + 1;
            var next = new RuntimeGameplayArchetypeSnapshot<TDescriptor>(revision, descriptors);
            nextRevision = revision;
            Volatile.Write(ref published, next);
            retiringIds.RemoveWhere(id => !next.Contains(id));
            Volatile.Write(ref dirty, false);
            return next;
        }
    }

    private void StageRetirement(GameplayArchetypeId id)
    {
        lock (gate)
        {
            if (staged.Remove(id))
            {
                retiringIds.Add(id);
                Volatile.Write(ref dirty, true);
                return;
            }

            if (published.Contains(id))
            {
                retiringIds.Add(id);
                Volatile.Write(ref dirty, true);
            }
        }
    }

    private bool IsRetired(GameplayArchetypeId id)
    {
        lock (gate)
            return !staged.ContainsKey(id) && !published.Contains(id) && !retiringIds.Contains(id);
    }

    private sealed class RegistrationLease : IGameplayArchetypeRegistrationLease
    {
        private readonly RuntimeGameplayArchetypeRegistry<TDescriptor> owner;
        private readonly GameplayArchetypeId id;
        private int disposed;

        public RegistrationLease(RuntimeGameplayArchetypeRegistry<TDescriptor> owner, GameplayArchetypeId id)
        {
            this.owner = owner;
            this.id = id;
        }

        public GameplayArchetypeId Id => id;

        public bool IsRetirementPending => Volatile.Read(ref disposed) != 0 && !IsRetired;

        public bool IsRetired => Volatile.Read(ref disposed) != 0 && owner.IsRetired(id);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
                owner.StageRetirement(id);
        }
    }
}
