using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Observes only NPC AI state transitions that were successfully committed to the generation-safe
/// authoritative store. The observer is called synchronously on the simulation thread after TryUpdate
/// succeeds, so it can publish immutable snapshots without re-reading or rescanning the NPC table.
/// </summary>
public interface INpcAiStateCommitSink
{
    void NpcAiStateCommitted(in NpcSnapshot snapshot);
}

/// <summary>
/// Optional capability owned by an AI composition layer that must react to its own proposed transition only after
/// the exact NPC generation has committed. Unlike the external commit sink this receives both the pre-pass snapshot
/// and committed revision, allowing gameplay side effects to prove they correspond to the accepted transition.
/// </summary>
public interface INpcAiStatePostCommitObserver
{
    void NpcAiStateCommitted(in NpcSnapshot before, in NpcSnapshot committed);
}

/// <summary>
/// Narrow authoritative mutation surface for irreversible NPC side effects that must occur only after the source
/// NPC generation has committed. It deliberately exposes spawn and exact-generation update only, rather than the
/// mutable NPC store, so side-effect implementations cannot bypass lifecycle validation accidentally.
/// </summary>
public interface INpcAiCommittedNpcMutationSink
{
    bool TrySpawn(in NpcAiSpawnIntent intent, out NpcSnapshot spawned);

    bool TryUpdateVelocity(NpcHandle npc, float velocityX, float velocityY, out NpcSnapshot committed);
}

/// <summary>
/// Optional post-commit gameplay effect. This runs after the exact source state update succeeds and may perform
/// source-ordered irreversible mutations through <see cref="INpcAiCommittedNpcMutationSink"/>. It is distinct from
/// speculative spawn planning so RNG and world effects do not escape a rejected/stale source transition.
/// </summary>
public interface INpcAiStatePostCommitEffect
{
    void ApplyCommittedEffect(
        in NpcSnapshot before,
        in NpcSnapshot committed,
        INpcAiCommittedNpcMutationSink mutations);
}
