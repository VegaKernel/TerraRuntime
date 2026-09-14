using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Tests;

// Initial allocation evidence remains distinct from subsequent same-tick AI commits.
internal sealed class NpcSpawnRecorder : INpcStateCommitSink
{
    public List<NpcSnapshot> Spawned { get; } = [];
    public List<NpcSnapshot> Updated { get; } = [];

    public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot)
    {
        if (kind == NpcStateCommitKind.Spawn)
            Spawned.Add(snapshot);
        else if (kind == NpcStateCommitKind.Update)
            Updated.Add(snapshot);
    }
}
