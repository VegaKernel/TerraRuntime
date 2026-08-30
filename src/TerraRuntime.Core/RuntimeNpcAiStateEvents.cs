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
