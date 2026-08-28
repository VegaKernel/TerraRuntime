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
