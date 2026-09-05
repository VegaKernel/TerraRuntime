using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Application;

/// <summary>
/// Keeps authoritative NPC commits available to independent replication and operations observers without
/// exposing RuntimeNpcStore or making either consumer the owner of the other.
/// </summary>
internal sealed class RuntimeNpcStateCommitFanout(
    INpcStateCommitSink first,
    INpcStateCommitSink second) : INpcStateCommitSink
{
    private readonly INpcStateCommitSink first = first ?? throw new ArgumentNullException(nameof(first));
    private readonly INpcStateCommitSink second = second ?? throw new ArgumentNullException(nameof(second));

    public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot)
    {
        first.NpcStateCommitted(kind, in snapshot);
        second.NpcStateCommitted(kind, in snapshot);
    }
}
