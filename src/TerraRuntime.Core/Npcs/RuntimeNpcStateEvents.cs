using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

public enum NpcStateCommitKind : byte
{
    Spawn = 0,
    Update = 1,
    Despawn = 2
}

/// <summary>
/// Receives immutable NPC snapshots only after authoritative store commits succeed. Despawn supplies
/// the final live snapshot before the slot is cleared, preserving identity, generation and wire state
/// needed by replication without exposing mutable store internals.
/// </summary>
public interface INpcStateCommitSink
{
    void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot);
}
