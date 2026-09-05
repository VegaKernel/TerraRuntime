using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Core.Worlds;

public enum WorldGenerationPassRegistrationResult : byte
{
    Registered = 0,
    DuplicateId = 1,
    InvalidDescriptor = 2
}

public enum WorldGenerationPlanCommitStatus : byte
{
    Published = 0,
    NoChanges = 1,
    MissingRequiredDependency = 2,
    DependencyCycle = 3
}

public readonly record struct WorldGenerationPlanCommitResult(
    WorldGenerationPlanCommitStatus Status,
    WorldGenerationPassId PassId = default,
    WorldGenerationPassId DependencyId = default)
{
    public bool Succeeded => Status is WorldGenerationPlanCommitStatus.Published or WorldGenerationPlanCommitStatus.NoChanges;
}

public interface IWorldGenerationPassRegistrationLease : IDisposable
{
    WorldGenerationPassId Id { get; }
    bool IsRetirementPending { get; }
    bool IsRetired { get; }
}

public readonly record struct RuntimeWorldGenerationPlanEntry<TPass>(
    WorldGenerationPassDescriptor Descriptor,
    TPass Pass)
    where TPass : class;

/// <summary>Immutable, deterministically ordered generation plan published at a safe runtime boundary.</summary>
public sealed class RuntimeWorldGenerationPlan<TPass>
    where TPass : class
{
    private readonly RuntimeWorldGenerationPlanEntry<TPass>[] entries;

    internal RuntimeWorldGenerationPlan(ulong revision, RuntimeWorldGenerationPlanEntry<TPass>[] entries)
    {
        Revision = revision;
        this.entries = entries;
    }

    public ulong Revision { get; }
    public int Count => entries.Length;
    public ReadOnlyMemory<RuntimeWorldGenerationPlanEntry<TPass>> Entries => entries;
}

/// <summary>
/// Explicit AOT-safe registry and deterministic topological planner for world-generation passes. An invalid pending
/// graph is never partially published: the previously active plan remains intact until missing hard dependencies or
/// cycles are resolved. Registration order is not a tie-breaker; ready passes are ordered by stable ordinal ID.
/// </summary>
public sealed class RuntimeWorldGenerationPassRegistry<TPass>
    where TPass : class
{
    private readonly object gate = new();
    private readonly Dictionary<WorldGenerationPassId, Registration> staged = [];
    private readonly HashSet<WorldGenerationPassId> retiring = [];
    private RuntimeWorldGenerationPlan<TPass> published = new(0, []);
    private ulong nextRevision;
    private bool dirty;

    public RuntimeWorldGenerationPlan<TPass> Plan => Volatile.Read(ref published);

    public bool HasPendingChanges
    {
        get
        {
            lock (gate)
                return dirty;
        }
    }

    public WorldGenerationPassRegistrationResult TryRegister(
        WorldGenerationPassDescriptor descriptor,
        TPass pass,
        out IWorldGenerationPassRegistrationLease? lease)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(pass);
        lease = null;
        if (!descriptor.Id.IsAssigned || !Enum.IsDefined(descriptor.RngMode))
            return WorldGenerationPassRegistrationResult.InvalidDescriptor;

        lock (gate)
        {
            if (staged.ContainsKey(descriptor.Id) || retiring.Contains(descriptor.Id) || ContainsPublished(descriptor.Id))
                return WorldGenerationPassRegistrationResult.DuplicateId;

            staged.Add(descriptor.Id, new Registration(descriptor, pass));
            dirty = true;
            lease = new RegistrationLease(this, descriptor.Id);
            return WorldGenerationPassRegistrationResult.Registered;
        }
    }

    public WorldGenerationPlanCommitResult CommitPending()
    {
        lock (gate)
        {
            if (!dirty)
                return new WorldGenerationPlanCommitResult(WorldGenerationPlanCommitStatus.NoChanges);

            var candidate = new Dictionary<WorldGenerationPassId, Registration>(staged.Count);
            foreach ((WorldGenerationPassId id, Registration registration) in staged)
            {
                if (!retiring.Contains(id))
                    candidate.Add(id, registration);
            }

            foreach ((WorldGenerationPassId id, Registration registration) in candidate)
            {
                foreach (WorldGenerationPassId dependency in registration.Descriptor.RequiredAfter.Span)
                {
                    if (!candidate.ContainsKey(dependency))
                    {
                        return new WorldGenerationPlanCommitResult(
                            WorldGenerationPlanCommitStatus.MissingRequiredDependency,
                            id,
                            dependency);
                    }
                }
            }

            var outgoing = new Dictionary<WorldGenerationPassId, HashSet<WorldGenerationPassId>>(candidate.Count);
            var indegree = new Dictionary<WorldGenerationPassId, int>(candidate.Count);
            foreach (WorldGenerationPassId id in candidate.Keys)
            {
                outgoing.Add(id, []);
                indegree.Add(id, 0);
            }

            foreach ((WorldGenerationPassId id, Registration registration) in candidate)
            {
                foreach (WorldGenerationPassId dependency in registration.Descriptor.RequiredAfter.Span)
                    AddEdge(dependency, id, outgoing, indegree);
                foreach (WorldGenerationPassId dependency in registration.Descriptor.OptionalAfter.Span)
                {
                    if (candidate.ContainsKey(dependency))
                        AddEdge(dependency, id, outgoing, indegree);
                }
                foreach (WorldGenerationPassId successor in registration.Descriptor.OptionalBefore.Span)
                {
                    if (candidate.ContainsKey(successor))
                        AddEdge(id, successor, outgoing, indegree);
                }
            }

            var ready = new SortedSet<WorldGenerationPassId>();
            foreach ((WorldGenerationPassId id, int count) in indegree)
            {
                if (count == 0)
                    ready.Add(id);
            }

            var orderedIds = new List<WorldGenerationPassId>(candidate.Count);
            while (ready.Count != 0)
            {
                WorldGenerationPassId id = ready.Min;
                ready.Remove(id);
                orderedIds.Add(id);

                foreach (WorldGenerationPassId successor in outgoing[id])
                {
                    int count = indegree[successor] - 1;
                    indegree[successor] = count;
                    if (count == 0)
                        ready.Add(successor);
                }
            }

            if (orderedIds.Count != candidate.Count)
            {
                WorldGenerationPassId cycleMember = default;
                foreach ((WorldGenerationPassId id, int count) in indegree)
                {
                    if (count > 0 && (!cycleMember.IsAssigned || id.CompareTo(cycleMember) < 0))
                        cycleMember = id;
                }

                return new WorldGenerationPlanCommitResult(
                    WorldGenerationPlanCommitStatus.DependencyCycle,
                    cycleMember);
            }

            var entries = new RuntimeWorldGenerationPlanEntry<TPass>[orderedIds.Count];
            for (int index = 0; index < orderedIds.Count; index++)
            {
                Registration registration = candidate[orderedIds[index]];
                entries[index] = new RuntimeWorldGenerationPlanEntry<TPass>(registration.Descriptor, registration.Pass);
            }

            ulong revision = nextRevision == ulong.MaxValue ? 1 : nextRevision + 1;
            var next = new RuntimeWorldGenerationPlan<TPass>(revision, entries);
            nextRevision = revision;
            Volatile.Write(ref published, next);

            foreach (WorldGenerationPassId retiredId in retiring)
                staged.Remove(retiredId);
            retiring.Clear();
            dirty = false;
            return new WorldGenerationPlanCommitResult(WorldGenerationPlanCommitStatus.Published);
        }
    }

    private static void AddEdge(
        WorldGenerationPassId from,
        WorldGenerationPassId to,
        Dictionary<WorldGenerationPassId, HashSet<WorldGenerationPassId>> outgoing,
        Dictionary<WorldGenerationPassId, int> indegree)
    {
        if (outgoing[from].Add(to))
            indegree[to]++;
    }

    private bool ContainsPublished(WorldGenerationPassId id)
    {
        foreach (RuntimeWorldGenerationPlanEntry<TPass> entry in published.Entries.Span)
        {
            if (entry.Descriptor.Id == id)
                return true;
        }

        return false;
    }

    private void StageRetirement(WorldGenerationPassId id)
    {
        lock (gate)
        {
            if (staged.ContainsKey(id) || ContainsPublished(id))
            {
                retiring.Add(id);
                dirty = true;
            }
        }
    }

    private bool IsRetired(WorldGenerationPassId id)
    {
        lock (gate)
            return !staged.ContainsKey(id) && !ContainsPublished(id) && !retiring.Contains(id);
    }

    private sealed record Registration(WorldGenerationPassDescriptor Descriptor, TPass Pass);

    private sealed class RegistrationLease : IWorldGenerationPassRegistrationLease
    {
        private readonly RuntimeWorldGenerationPassRegistry<TPass> owner;
        private int disposed;

        public RegistrationLease(RuntimeWorldGenerationPassRegistry<TPass> owner, WorldGenerationPassId id)
        {
            this.owner = owner;
            Id = id;
        }

        public WorldGenerationPassId Id { get; }
        public bool IsRetirementPending => Volatile.Read(ref disposed) != 0 && !IsRetired;
        public bool IsRetired => Volatile.Read(ref disposed) != 0 && owner.IsRetired(Id);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
                owner.StageRetirement(Id);
        }
    }
}

/// <summary>Independent deterministic RNG stream for one custom world-generation pass.</summary>
public struct WorldGenerationPassRandom
{
    private ulong state;

    private WorldGenerationPassRandom(ulong seed) => state = seed;

    public static WorldGenerationPassRandom Create(ulong worldSeed, WorldGenerationPassId passId)
    {
        if (!passId.IsAssigned)
            throw new ArgumentException("A deterministic world-generation RNG requires an assigned pass ID.", nameof(passId));

        ulong seed = Mix(worldSeed ^ 0xD6E8FEB86659FD93UL);
        seed = Mix(seed ^ StableIdHash(passId));
        return new WorldGenerationPassRandom(seed);
    }

    public ulong NextUInt64()
    {
        state = unchecked(state + 0x9E3779B97F4A7C15UL);
        return Mix(state);
    }

    public uint NextUInt32() => (uint)(NextUInt64() >> 32);

    public int NextInt32(int exclusiveMax)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveMax);
        return (int)(((ulong)NextUInt32() * (uint)exclusiveMax) >> 32);
    }

    private static ulong StableIdHash(WorldGenerationPassId id)
    {
        ulong hash = 0xCBF29CE484222325UL;
        foreach (char character in id.Value)
        {
            hash ^= (byte)character;
            hash = unchecked(hash * 0x100000001B3UL);
            hash ^= (byte)(character >> 8);
            hash = unchecked(hash * 0x100000001B3UL);
        }
        return hash;
    }

    private static ulong Mix(ulong value)
    {
        value ^= value >> 30;
        value = unchecked(value * 0xBF58476D1CE4E5B9UL);
        value ^= value >> 27;
        value = unchecked(value * 0x94D049BB133111EBUL);
        return value ^ (value >> 31);
    }
}
