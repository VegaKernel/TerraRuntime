using System.Collections.Concurrent;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Operations;

/// <summary>
/// Maintains a bounded immutable player read model from already validated authoritative events.
/// The TUI never reads ServerRuntimeState or mutable player objects directly.
/// </summary>
internal sealed class RuntimePlayerOperationsTelemetry : global::TerraRuntime.IRuntimePlayerEventSink, IPlayerOperations
{
    private readonly ConcurrentDictionary<byte, RuntimePlayerSnapshot> players = new();
    private readonly Dictionary<GameCommandSourceId, PendingPlayerState> pending = [];

    public RuntimePlayersSnapshot CaptureSnapshot()
    {
        RuntimePlayerSnapshot[] snapshot = players.Values.ToArray();
        Array.Sort(snapshot, static (left, right) => left.Slot.CompareTo(right.Slot));
        return new RuntimePlayersSnapshot(snapshot.AsMemory(), DateTimeOffset.UtcNow);
    }

    public void PlayerAppearanceUpdated(ConnectionHandle connection, in PlayerAppearanceCommitRequest request)
    {
        string name = request.Name;
        PendingPlayerState state = GetPending(connection.Source);
        state.Name = name;
        UpdateLive(connection, current => current with { Name = name });
    }

    public void PlayerEquipmentUpdated(ConnectionHandle connection, in PlayerEquipmentCommitRequest request)
    {
    }

    public void PlayerHealthUpdated(ConnectionHandle connection, in PlayerHealthCommitRequest request)
    {
        short life = request.Life;
        short maxLife = request.MaxLife;
        PendingPlayerState state = GetPending(connection.Source);
        state.HasHealth = true;
        state.Life = life;
        state.MaxLife = maxLife;
        UpdateLive(
            connection,
            current => current with
            {
                HasHealth = true,
                Life = life,
                MaxLife = maxLife
            });
    }

    public void PlayerManaUpdated(ConnectionHandle connection, in PlayerManaCommitRequest request)
    {
        short mana = request.Mana;
        short maxMana = request.MaxMana;
        PendingPlayerState state = GetPending(connection.Source);
        state.HasMana = true;
        state.Mana = mana;
        state.MaxMana = maxMana;
        UpdateLive(
            connection,
            current => current with
            {
                HasMana = true,
                Mana = mana,
                MaxMana = maxMana
            });
    }

    public void PlayerSpawned(ConnectionHandle connection, in PlayerSpawnCommitRequest request)
    {
        pending.TryGetValue(connection.Source, out PendingPlayerState? state);
        RuntimePlayerSnapshot snapshot = new(
            ConnectionId: connection.Source.Value,
            Slot: connection.Player.Slot.Value,
            Generation: connection.Player.Generation.Value,
            Name: state?.Name ?? string.Empty,
            Team: request.Team,
            PositionX: request.SpawnX * 16f,
            PositionY: request.SpawnY * 16f,
            HasHealth: state?.HasHealth ?? false,
            Life: state?.Life ?? 0,
            MaxLife: state?.MaxLife ?? 0,
            HasMana: state?.HasMana ?? false,
            Mana: state?.Mana ?? 0,
            MaxMana: state?.MaxMana ?? 0);
        players[request.ClaimedSlot.Value] = snapshot;
    }

    public void PlayerMoved(ConnectionHandle connection, in PlayerMovementCommitRequest request)
    {
        float positionX = request.PositionX;
        float positionY = request.PositionY;
        UpdateLive(
            connection,
            current => current with
            {
                PositionX = positionX,
                PositionY = positionY
            });
    }

    public void PlayerDisconnected(ConnectionHandle connection)
    {
        pending.Remove(connection.Source);
        byte slot = connection.Player.Slot.Value;
        if (players.TryGetValue(slot, out RuntimePlayerSnapshot current) &&
            current.Generation == connection.Player.Generation.Value &&
            current.ConnectionId == connection.Source.Value)
        {
            players.TryRemove(slot, out _);
        }
    }

    private PendingPlayerState GetPending(GameCommandSourceId source)
    {
        if (!pending.TryGetValue(source, out PendingPlayerState? state))
        {
            state = new PendingPlayerState();
            pending.Add(source, state);
        }

        return state;
    }

    private void UpdateLive(
        ConnectionHandle connection,
        Func<RuntimePlayerSnapshot, RuntimePlayerSnapshot> update)
    {
        byte slot = connection.Player.Slot.Value;
        while (players.TryGetValue(slot, out RuntimePlayerSnapshot current))
        {
            if (current.Generation != connection.Player.Generation.Value ||
                current.ConnectionId != connection.Source.Value)
            {
                return;
            }

            RuntimePlayerSnapshot next = update(current);
            if (players.TryUpdate(slot, next, current))
                return;
        }
    }

    private sealed class PendingPlayerState
    {
        public string Name { get; set; } = string.Empty;
        public bool HasHealth { get; set; }
        public short Life { get; set; }
        public short MaxLife { get; set; }
        public bool HasMana { get; set; }
        public short Mana { get; set; }
        public short MaxMana { get; set; }
    }
}
