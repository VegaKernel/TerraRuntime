using TerraRuntime.Contracts.Gameplay;
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

    void PlayerRespawned(ConnectionHandle connection, in PlayerSpawnCommitRequest request)
    {
    }

    void PlayerTeleported(ConnectionHandle connection, float positionX, float positionY, byte style, bool failed)
    {
    }

    void PlayerMoved(ConnectionHandle connection, in PlayerMovementCommitRequest request);

    void PlayerDamageAvoided(PlayerHandle player, float positionX, float positionY, string text)
    {
    }

    void PlayerGodModeChanged(PlayerHandle player, bool enabled)
    {
    }

    void PlayerBuffTypesUpdated(ConnectionHandle connection, in PlayerBuffTypesCommitRequest request)
    {
    }

    /// <summary>
    /// Relays a source-backed authoritative PvP buff to the exact target generation using Terraria packet 55.
    /// This is a network side effect only; exact buff duration remains client-owned as in vanilla PvP.
    /// </summary>
    void PlayerPvpBuffApplied(PlayerHandle player, BuffTypeId buffType, int durationTicks)
    {
    }

    void PlayerDisconnected(ConnectionHandle connection);
}
