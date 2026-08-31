using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Core;

public enum GameplayBehaviorStage : byte
{
    Pre = 0,
    Replacement = 1,
    Post = 2
}

public enum GameplayBehaviorRegistrationResult : byte
{
    Registered = 0,
    InvalidId = 1,
    InvalidStage = 2,
    DuplicateId = 3,
    ReplacementConflict = 4
}

public readonly record struct GameplayBehaviorBinding<TBehavior>(
    GameplayExtensionId Id,
    int Order,
    TBehavior Behavior)
    where TBehavior : class;

/// <summary>
/// Immutable per-target dispatch plan published by <see cref="RuntimeGameplayBehaviorRegistry{TTarget,TBehavior}"/>.
/// Arrays are exposed as read-only memories so the authoritative hot path can enumerate without locks or allocation.
/// </summary>
public sealed class GameplayBehaviorDispatchPlan<TBehavior>
    where TBehavior : class
{
    private readonly GameplayBehaviorBinding<TBehavior>[] pre;
    private readonly GameplayBehaviorBinding<TBehavior>[] post;

    internal GameplayBehaviorDispatchPlan(
        GameplayBehaviorBinding<TBehavior>[] pre,
        bool hasReplacement,
        GameplayBehaviorBinding<TBehavior> replacement,
        GameplayBehaviorBinding<TBehavior>[] post)
    {
        this.pre = pre;
        HasReplacement = hasReplacement;
        Replacement = replacement;
        this.post = post;
    }

    public ReadOnlyMemory<GameplayBehaviorBinding<TBehavior>> Pre => pre;

    public bool HasReplacement { get; }

    public GameplayBehaviorBinding<TBehavior> Replacement { get; }

    public ReadOnlyMemory<GameplayBehaviorBinding<TBehavior>> Post => post;
}

/// <summary>
/// Immutable registry image consumed by gameplay hot paths. A snapshot is replaced as one reference at an
/// authoritative safe point; registration and retirement never mutate an already-published image.
/// </summary>
public sealed class RuntimeGameplayBehaviorSnapshot<TTarget, TBehavior>
    where TTarget : notnull
    where TBehavior : class
{
    private readonly Dictionary<TTarget, GameplayBehaviorDispatchPlan<TBehavior>> plans;
    private readonly HashSet<GameplayExtensionId> publishedIds;

    internal RuntimeGameplayBehaviorSnapshot(
        ulong revision,
        Dictionary<TTarget, GameplayBehaviorDispatchPlan<TBehavior>> plans,
        HashSet<GameplayExtensionId> publishedIds)
    {
        Revision = revision;
        this.plans = plans;
        this.publishedIds = publishedIds;
    }

    public ulong Revision { get; }

    public int TargetCount => plans.Count;

    public int RegistrationCount => publishedIds.Count;

    public bool TryGetPlan(TTarget target, out GameplayBehaviorDispatchPlan<TBehavior>? plan) =>
        plans.TryGetValue(target, out plan);

    internal bool Contains(GameplayExtensionId id) => publishedIds.Contains(id);
}

public interface IGameplayBehaviorRegistrationLease : IDisposable
{
    GameplayExtensionId Id { get; }

    bool IsRetirementPending { get; }

    bool IsRetired { get; }
}

/// <summary>
/// AOT-safe explicit gameplay behavior registry. Registration is a cold-path operation. The authoritative loop
/// calls <see cref="CommitPending"/> at a safe boundary and thereafter performs allocation-free snapshot lookups.
/// Ordering is deterministic by explicit numeric order and then stable extension ID, never dictionary enumeration.
/// </summary>
public sealed class RuntimeGameplayBehaviorRegistry<TTarget, TBehavior>
    where TTarget : notnull
    where TBehavior : class
{
    private readonly object gate = new();
    private readonly Dictionary<GameplayExtensionId, StagedRegistration> staged = [];
    private readonly HashSet<GameplayExtensionId> retiringIds = [];
    private RuntimeGameplayBehaviorSnapshot<TTarget, TBehavior> published = CreateEmptySnapshot();
    private ulong nextRevision;
    private bool dirty;

    public RuntimeGameplayBehaviorSnapshot<TTarget, TBehavior> Snapshot => Volatile.Read(ref published);

    public bool HasPendingChanges
    {
        get
        {
            lock (gate)
            {
                return dirty;
            }
        }
    }

    public GameplayBehaviorRegistrationResult TryRegister(
        GameplayExtensionId id,
        TTarget target,
        GameplayBehaviorStage stage,
        int order,
        TBehavior behavior,
        out IGameplayBehaviorRegistrationLease? lease)
    {
        ArgumentNullException.ThrowIfNull(behavior);
        lease = null;

        if (!id.IsAssigned)
        {
            return GameplayBehaviorRegistrationResult.InvalidId;
        }

        if (stage is not GameplayBehaviorStage.Pre and
            not GameplayBehaviorStage.Replacement and
            not GameplayBehaviorStage.Post)
        {
            return GameplayBehaviorRegistrationResult.InvalidStage;
        }

        lock (gate)
        {
            if (staged.ContainsKey(id) || retiringIds.Contains(id) || published.Contains(id))
            {
                return GameplayBehaviorRegistrationResult.DuplicateId;
            }

            if (stage == GameplayBehaviorStage.Replacement)
            {
                EqualityComparer<TTarget> targetComparer = EqualityComparer<TTarget>.Default;
                foreach (StagedRegistration existing in staged.Values)
                {
                    if (existing.Stage == GameplayBehaviorStage.Replacement &&
                        targetComparer.Equals(existing.Target, target))
                    {
                        return GameplayBehaviorRegistrationResult.ReplacementConflict;
                    }
                }
            }

            staged.Add(id, new StagedRegistration(id, target, stage, order, behavior));
            dirty = true;
            lease = new RegistrationLease(this, id);
            return GameplayBehaviorRegistrationResult.Registered;
        }
    }

    /// <summary>
    /// Publishes all staged additions/removals as one immutable image. This is the only operation intended to run
    /// at the authoritative tick boundary; callers that register off-thread never expose a half-updated registry.
    /// </summary>
    public RuntimeGameplayBehaviorSnapshot<TTarget, TBehavior> CommitPending()
    {
        lock (gate)
        {
            if (!dirty)
            {
                return published;
            }

            ulong revision = AdvanceRevision(nextRevision);
            RuntimeGameplayBehaviorSnapshot<TTarget, TBehavior> next = BuildSnapshot(revision);
            nextRevision = revision;
            Volatile.Write(ref published, next);
            retiringIds.RemoveWhere(id => !next.Contains(id));
            dirty = false;
            return next;
        }
    }

    private void StageRetirement(GameplayExtensionId id)
    {
        lock (gate)
        {
            if (staged.Remove(id))
            {
                retiringIds.Add(id);
                dirty = true;
                return;
            }

            if (published.Contains(id))
            {
                retiringIds.Add(id);
                dirty = true;
            }
        }
    }

    private bool IsRetired(GameplayExtensionId id)
    {
        lock (gate)
        {
            return !staged.ContainsKey(id) && !published.Contains(id) && !retiringIds.Contains(id);
        }
    }

    private RuntimeGameplayBehaviorSnapshot<TTarget, TBehavior> BuildSnapshot(ulong revision)
    {
        var grouped = new Dictionary<TTarget, List<StagedRegistration>>();
        var publishedIds = new HashSet<GameplayExtensionId>();

        foreach (StagedRegistration registration in staged.Values)
        {
            if (retiringIds.Contains(registration.Id))
            {
                continue;
            }

            if (!grouped.TryGetValue(registration.Target, out List<StagedRegistration>? registrations))
            {
                registrations = [];
                grouped.Add(registration.Target, registrations);
            }

            registrations.Add(registration);
            publishedIds.Add(registration.Id);
        }

        var plans = new Dictionary<TTarget, GameplayBehaviorDispatchPlan<TBehavior>>(grouped.Count);
        foreach ((TTarget target, List<StagedRegistration> registrations) in grouped)
        {
            plans.Add(target, BuildPlan(registrations));
        }

        return new RuntimeGameplayBehaviorSnapshot<TTarget, TBehavior>(revision, plans, publishedIds);
    }

    private static GameplayBehaviorDispatchPlan<TBehavior> BuildPlan(List<StagedRegistration> registrations)
    {
        int preCount = 0;
        int postCount = 0;
        bool hasReplacement = false;
        GameplayBehaviorBinding<TBehavior> replacement = default;

        foreach (StagedRegistration registration in registrations)
        {
            switch (registration.Stage)
            {
                case GameplayBehaviorStage.Pre:
                    preCount++;
                    break;
                case GameplayBehaviorStage.Replacement:
                    hasReplacement = true;
                    replacement = registration.ToBinding();
                    break;
                case GameplayBehaviorStage.Post:
                    postCount++;
                    break;
            }
        }

        var pre = new GameplayBehaviorBinding<TBehavior>[preCount];
        var post = new GameplayBehaviorBinding<TBehavior>[postCount];
        int preIndex = 0;
        int postIndex = 0;

        foreach (StagedRegistration registration in registrations)
        {
            if (registration.Stage == GameplayBehaviorStage.Pre)
            {
                pre[preIndex++] = registration.ToBinding();
            }
            else if (registration.Stage == GameplayBehaviorStage.Post)
            {
                post[postIndex++] = registration.ToBinding();
            }
        }

        Array.Sort(pre, BindingComparer.Instance);
        Array.Sort(post, BindingComparer.Instance);
        return new GameplayBehaviorDispatchPlan<TBehavior>(pre, hasReplacement, replacement, post);
    }

    private static RuntimeGameplayBehaviorSnapshot<TTarget, TBehavior> CreateEmptySnapshot() =>
        new(0, [], []);

    private static ulong AdvanceRevision(ulong current) => current == ulong.MaxValue ? 1 : current + 1;

    private sealed record StagedRegistration(
        GameplayExtensionId Id,
        TTarget Target,
        GameplayBehaviorStage Stage,
        int Order,
        TBehavior Behavior)
    {
        public GameplayBehaviorBinding<TBehavior> ToBinding() => new(Id, Order, Behavior);
    }

    private sealed class BindingComparer : IComparer<GameplayBehaviorBinding<TBehavior>>
    {
        public static BindingComparer Instance { get; } = new();

        public int Compare(GameplayBehaviorBinding<TBehavior> x, GameplayBehaviorBinding<TBehavior> y)
        {
            int order = x.Order.CompareTo(y.Order);
            return order != 0 ? order : x.Id.CompareTo(y.Id);
        }
    }

    private sealed class RegistrationLease(
        RuntimeGameplayBehaviorRegistry<TTarget, TBehavior> owner,
        GameplayExtensionId id) : IGameplayBehaviorRegistrationLease
    {
        private int disposed;

        public GameplayExtensionId Id => id;

        public bool IsRetirementPending => Volatile.Read(ref disposed) != 0 && !IsRetired;

        public bool IsRetired => Volatile.Read(ref disposed) != 0 && owner.IsRetired(id);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                owner.StageRetirement(id);
            }
        }
    }
}
