using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime;

/// <summary>
/// Keeps authoritative state mutation decoupled from individual replication services.
/// Connection/AOI routing, player-vitals synchronization and operations telemetry remain independently replaceable.
/// </summary>
internal sealed class RuntimePlayerEventDispatcher : IRuntimePlayerEventSink
{
    private readonly RuntimeConnectionRegistry _connections;
    private readonly RuntimePlayerVitalsReplicator _vitals;
    private readonly IRuntimePlayerEventSink? _operationsObserver;

    public RuntimePlayerEventDispatcher(
        RuntimeConnectionRegistry connections,
        RuntimePlayerVitalsReplicator vitals,
        IRuntimePlayerEventSink? operationsObserver = null)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(vitals);
        _connections = connections;
        _vitals = vitals;
        _operationsObserver = operationsObserver;
    }

    public void PlayerAppearanceUpdated(ConnectionHandle connection, in PlayerAppearanceCommitRequest request)
    {
        _connections.PlayerAppearanceUpdated(connection, in request);
        _operationsObserver?.PlayerAppearanceUpdated(connection, in request);
    }

    public void PlayerEquipmentUpdated(ConnectionHandle connection, in PlayerEquipmentCommitRequest request)
    {
        _connections.PlayerEquipmentUpdated(connection, in request);
        _operationsObserver?.PlayerEquipmentUpdated(connection, in request);
    }

    public void PlayerHealthUpdated(ConnectionHandle connection, in PlayerHealthCommitRequest request)
    {
        _vitals.PlayerHealthUpdated(connection, in request);
        _operationsObserver?.PlayerHealthUpdated(connection, in request);
    }

    public void PlayerAuthoritativeHealthUpdated(ConnectionHandle connection, in PlayerHealthCommitRequest request)
    {
        _vitals.PlayerAuthoritativeHealthUpdated(connection, in request);
        _operationsObserver?.PlayerHealthUpdated(connection, in request);
    }

    public void PlayerManaUpdated(ConnectionHandle connection, in PlayerManaCommitRequest request)
    {
        _vitals.PlayerManaUpdated(connection, in request);
        _operationsObserver?.PlayerManaUpdated(connection, in request);
    }

    public void PlayerSpawned(ConnectionHandle connection, in PlayerSpawnCommitRequest request)
    {
        // Keep the broad vanilla SyncOnePlayer envelope: active/appearance/equipment first,
        // then health and mana baselines.
        _connections.PlayerSpawned(connection, in request);
        _vitals.PlayerSpawned(connection, in request);
        _operationsObserver?.PlayerSpawned(connection, in request);
    }

    public void PlayerMoved(ConnectionHandle connection, in PlayerMovementCommitRequest request)
    {
        _connections.PlayerMoved(connection, in request);
        _operationsObserver?.PlayerMoved(connection, in request);
    }

    public void PlayerDamageAvoided(PlayerHandle player, float positionX, float positionY, string text)
    {
        _connections.PlayerDamageAvoided(player, positionX, positionY, text);
        _operationsObserver?.PlayerDamageAvoided(player, positionX, positionY, text);
    }

    public void PlayerDisconnected(ConnectionHandle connection)
    {
        _connections.PlayerDisconnected(connection);
        _vitals.PlayerDisconnected(connection);
        _operationsObserver?.PlayerDisconnected(connection);
    }
}
