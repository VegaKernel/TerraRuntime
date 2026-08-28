using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime;

/// <summary>
/// Receives authoritative player lifecycle events after validation and state mutation.
/// Implementations may plan outbound synchronization, but never mutate authoritative game state.
/// </summary>
internal interface IRuntimePlayerEventSink
{
    void PlayerAppearanceUpdated(ConnectionHandle connection, in PlayerAppearanceCommitRequest request);

    void PlayerEquipmentUpdated(ConnectionHandle connection, in PlayerEquipmentCommitRequest request);

    void PlayerSpawned(ConnectionHandle connection, in PlayerSpawnCommitRequest request);

    void PlayerMoved(ConnectionHandle connection, in PlayerMovementCommitRequest request);

    void PlayerDisconnected(ConnectionHandle connection);
}
