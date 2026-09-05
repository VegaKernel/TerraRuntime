using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Application;

/// <summary>
/// Small in-process fan-out used when independent synchronization subsystems need the same authoritative
/// player commit. It deliberately carries events only after ServerRuntimeState validation/mutation.
/// </summary>
internal sealed class RuntimePlayerEventFanout(
    IRuntimePlayerEventSink first,
    IRuntimePlayerEventSink second) : IRuntimePlayerEventSink
{
    private readonly IRuntimePlayerEventSink first = first ?? throw new ArgumentNullException(nameof(first));
    private readonly IRuntimePlayerEventSink second = second ?? throw new ArgumentNullException(nameof(second));

    public void PlayerAppearanceUpdated(ConnectionHandle connection, in PlayerAppearanceCommitRequest request)
    {
        first.PlayerAppearanceUpdated(connection, in request);
        second.PlayerAppearanceUpdated(connection, in request);
    }

    public void PlayerEquipmentUpdated(ConnectionHandle connection, in PlayerEquipmentCommitRequest request)
    {
        first.PlayerEquipmentUpdated(connection, in request);
        second.PlayerEquipmentUpdated(connection, in request);
    }

    public void PlayerHealthUpdated(ConnectionHandle connection, in PlayerHealthCommitRequest request)
    {
        first.PlayerHealthUpdated(connection, in request);
        second.PlayerHealthUpdated(connection, in request);
    }

    public void PlayerAuthoritativeHealthUpdated(ConnectionHandle connection, in PlayerHealthCommitRequest request)
    {
        first.PlayerAuthoritativeHealthUpdated(connection, in request);
        second.PlayerAuthoritativeHealthUpdated(connection, in request);
    }

    public void PlayerManaUpdated(ConnectionHandle connection, in PlayerManaCommitRequest request)
    {
        first.PlayerManaUpdated(connection, in request);
        second.PlayerManaUpdated(connection, in request);
    }

    public void PlayerSpawned(ConnectionHandle connection, in PlayerSpawnCommitRequest request)
    {
        first.PlayerSpawned(connection, in request);
        second.PlayerSpawned(connection, in request);
    }

    public void PlayerRespawned(ConnectionHandle connection, in PlayerSpawnCommitRequest request)
    {
        first.PlayerRespawned(connection, in request);
        second.PlayerRespawned(connection, in request);
    }

    public void PlayerTeleported(ConnectionHandle connection, float positionX, float positionY, byte style, bool failed)
    {
        first.PlayerTeleported(connection, positionX, positionY, style, failed);
        second.PlayerTeleported(connection, positionX, positionY, style, failed);
    }

    public void PlayerMoved(ConnectionHandle connection, in PlayerMovementCommitRequest request)
    {
        first.PlayerMoved(connection, in request);
        second.PlayerMoved(connection, in request);
    }

    public void PlayerAuthoritativeMovementCorrected(ConnectionHandle connection, in PlayerStateSnapshot player)
    {
        first.PlayerAuthoritativeMovementCorrected(connection, in player);
        second.PlayerAuthoritativeMovementCorrected(connection, in player);
    }

    public void PlayerDamageAvoided(PlayerHandle player, float positionX, float positionY, string text)
    {
        first.PlayerDamageAvoided(player, positionX, positionY, text);
        second.PlayerDamageAvoided(player, positionX, positionY, text);
    }

    public void PlayerDisconnected(ConnectionHandle connection)
    {
        first.PlayerDisconnected(connection);
        second.PlayerDisconnected(connection);
    }
}
