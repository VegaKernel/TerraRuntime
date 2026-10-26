using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

public enum NpcStateCommitKind : byte
{
    Spawn = 0,
    Update = 1,
    Despawn = 2,
    ForcedUpdate = 3
}

/// <summary>
/// Receives immutable NPC snapshots only after authoritative store commits succeed. Despawn supplies
/// the final live snapshot before the slot is cleared, preserving identity, generation and wire state
/// needed by replication without exposing mutable store internals.
/// </summary>
public interface INpcStateCommitSink
{
    void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot);

    /// <summary>One accepted vanilla UpdateNetworkCode phase, distinct from immediate combat publications.</summary>
    void NpcVanillaAiStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot, bool lowPriority = false) =>
        NpcStateCommitted(kind, in snapshot);

    /// <summary>CatchNPC retires a retained birth with one inactive wire packet. Ordinary lifecycle
    /// observers still receive their activation/retirement pair for that generation.</summary>
    void NpcCatchCommitted(in NpcSnapshot snapshot, bool spawnNeedsSyncing)
    {
        if (spawnNeedsSyncing) NpcStateCommitted(NpcStateCommitKind.Spawn, in snapshot);
        NpcStateCommitted(NpcStateCommitKind.Despawn, in snapshot);
    }
}
