using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime;

/// <summary>
/// Receives authoritative player lifecycle events after validation and state mutation.
/// Implementations may plan outbound synchronization, but never mutate authoritative game state.
/// </summary>
internal interface IRuntimePlayerEventSink
{
    void PlayerSpawned(GameCommandSourceId source, PlayerSlotId slot);

    void PlayerMoved(GameCommandSourceId source, in PlayerMovementCommitRequest request);

    void PlayerDisconnected(GameCommandSourceId source, PlayerSlotId slot);
}
