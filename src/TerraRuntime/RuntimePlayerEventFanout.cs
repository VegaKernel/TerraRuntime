using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime;

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

    public void PlayerMoved(ConnectionHandle connection, in PlayerMovementCommitRequest request)
    {
        first.PlayerMoved(connection, in request);
        second.PlayerMoved(connection, in request);
    }

    public void PlayerDisconnected(ConnectionHandle connection)
    {
        first.PlayerDisconnected(connection);
        second.PlayerDisconnected(connection);
    }
}
