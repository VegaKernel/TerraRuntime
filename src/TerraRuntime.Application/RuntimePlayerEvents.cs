using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Application;

/// <summary>
/// Receives authoritative player lifecycle events after validation and state mutation.
/// Implementations may plan outbound synchronization, but never mutate authoritative game state.
/// </summary>
internal interface IRuntimePlayerEventSink
{
    void PlayerAppearanceUpdated(ConnectionHandle connection, in PlayerAppearanceCommitRequest request);

    void PlayerEquipmentUpdated(ConnectionHandle connection, in PlayerEquipmentCommitRequest request);

    void PlayerHealthUpdated(ConnectionHandle connection, in PlayerHealthCommitRequest request)
    {
    }

    /// <summary>
    /// Replicates an authoritative server-owned health value back to the owning client as well as peers.
    /// Use this for server-side damage/corrections; ordinary client packet-16 commits use PlayerHealthUpdated.
    /// </summary>
    void PlayerAuthoritativeHealthUpdated(ConnectionHandle connection, in PlayerHealthCommitRequest request)
    {
        PlayerHealthUpdated(connection, in request);
    }

    void PlayerManaUpdated(ConnectionHandle connection, in PlayerManaCommitRequest request)
    {
    }

    void PlayerSpawned(ConnectionHandle connection, in PlayerSpawnCommitRequest request);

    void PlayerMoved(ConnectionHandle connection, in PlayerMovementCommitRequest request);

    void PlayerDamageAvoided(PlayerHandle player, float positionX, float positionY, string text)
    {
    }

    void PlayerDisconnected(ConnectionHandle connection);
}
