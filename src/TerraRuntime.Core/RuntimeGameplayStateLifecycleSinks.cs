using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Allocation-free hot-path fan-out for immutable NPC commit events. Composition happens once on the cold path;
/// the authoritative store still owns event order and invokes every sink synchronously after a successful commit.
/// </summary>
public sealed class NpcStateCommitSinkFanout : INpcStateCommitSink
{
    private readonly INpcStateCommitSink[] sinks;

    public NpcStateCommitSinkFanout(params INpcStateCommitSink[] sinks)
    {
        ArgumentNullException.ThrowIfNull(sinks);
        this.sinks = (INpcStateCommitSink[])sinks.Clone();
        foreach (INpcStateCommitSink sink in this.sinks)
            ArgumentNullException.ThrowIfNull(sink);
    }

    public int Count => sinks.Length;

    public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot)
    {
        foreach (INpcStateCommitSink sink in sinks)
            sink.NpcStateCommitted(kind, in snapshot);
    }
}

/// <summary>
/// Allocation-free hot-path fan-out for immutable projectile commit events.
/// </summary>
public sealed class ProjectileStateCommitSinkFanout : IProjectileStateCommitSink
{
    private readonly IProjectileStateCommitSink[] sinks;

    public ProjectileStateCommitSinkFanout(params IProjectileStateCommitSink[] sinks)
    {
        ArgumentNullException.ThrowIfNull(sinks);
        this.sinks = (IProjectileStateCommitSink[])sinks.Clone();
        foreach (IProjectileStateCommitSink sink in this.sinks)
            ArgumentNullException.ThrowIfNull(sink);
    }

    public int Count => sinks.Length;

    public void ProjectileStateCommitted(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot)
    {
        foreach (IProjectileStateCommitSink sink in sinks)
            sink.ProjectileStateCommitted(kind, in snapshot);
    }
}

/// <summary>
/// Binds one extension-owned NPC side-state store to authoritative NPC generation lifetime. Spawn activates the
/// exact generation and despawn retires it. Update commits deliberately do nothing so state survives ordinary AI
/// revisions. A mismatch is diagnostic only because the authoritative commit has already succeeded when this sink
/// runs and must not be retroactively reported as failed.
/// </summary>
public sealed class RuntimeNpcExtensionStateLifecycleSink<TState>(RuntimeNpcExtensionStateStore<TState> stateStore)
    : INpcStateCommitSink
{
    private readonly RuntimeNpcExtensionStateStore<TState> stateStore =
        stateStore ?? throw new ArgumentNullException(nameof(stateStore));

    public int MismatchCount { get; private set; }

    public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot)
    {
        bool matched = kind switch
        {
            NpcStateCommitKind.Spawn => stateStore.TryActivate(snapshot.Handle),
            NpcStateCommitKind.Despawn => stateStore.TryRetire(snapshot.Handle),
            _ => true
        };

        if (!matched)
            MismatchCount++;
    }
}

/// <summary>
/// Binds one extension-owned projectile side-state store to authoritative projectile generation lifetime. Both
/// network Despawn and silent authoritative Remove retire the exact generation. Vanilla in-place slot replacement
/// arrives as a new Spawn generation and therefore resets old extension state without requiring a synthetic kill.
/// </summary>
public sealed class RuntimeProjectileExtensionStateLifecycleSink<TState>(RuntimeProjectileExtensionStateStore<TState> stateStore)
    : IProjectileStateCommitSink
{
    private readonly RuntimeProjectileExtensionStateStore<TState> stateStore =
        stateStore ?? throw new ArgumentNullException(nameof(stateStore));

    public int MismatchCount { get; private set; }

    public void ProjectileStateCommitted(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot)
    {
        bool matched = kind switch
        {
            ProjectileStateCommitKind.Spawn => stateStore.TryActivate(snapshot.Handle),
            ProjectileStateCommitKind.Despawn or ProjectileStateCommitKind.Remove => stateStore.TryRetire(snapshot.Handle),
            _ => true
        };

        if (!matched)
            MismatchCount++;
    }
}
