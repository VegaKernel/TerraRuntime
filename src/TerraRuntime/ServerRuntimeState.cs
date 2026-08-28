using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime;

internal sealed class ServerRuntimeState
{
    private readonly Dictionary<byte, RuntimePlayerState> _players = [];
    private readonly IRuntimePlayerEventSink? _playerEvents;
    private int lastWorkerResult;
    private int lastSpawnCommitResult = -1;

    public ServerRuntimeState(IRuntimePlayerEventSink? playerEvents = null)
    {
        _playerEvents = playerEvents;
    }

    public long AppliedCommands { get; private set; }

    public long Updates { get; private set; }

    public long AppliedPlayerAppearances { get; private set; }

    public long RejectedPlayerAppearances { get; private set; }

    public long AppliedPlayerEquipmentUpdates { get; private set; }

    public long RejectedPlayerEquipmentUpdates { get; private set; }

    public long CommittedPlayerSpawns { get; private set; }

    public long AppliedPlayerMovements { get; private set; }

    public long RejectedPlayerMovements { get; private set; }

    public long DisconnectedPlayers { get; private set; }

    public PlayerSlotId? LastMovementPlayerSlot { get; private set; }

    public float LastMovementPositionX { get; private set; }

    public float LastMovementPositionY { get; private set; }

    public int LastWorkerResult => Volatile.Read(ref lastWorkerResult);

    public PlayerSpawnCommitResult? LastSpawnCommitResult
    {
        get
        {
            int value = Volatile.Read(ref lastSpawnCommitResult);
            return value < 0 ? null : (PlayerSpawnCommitResult)value;
        }
    }

    public void Apply(RuntimeCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        AppliedCommands++;

        switch (command)
        {
            case WorkerResultCommand result:
                Volatile.Write(ref lastWorkerResult, result.Value);
                break;

            case PlayerAppearanceRuntimeCommand appearance:
                ApplyPlayerAppearance(appearance);
                break;

            case PlayerEquipmentRuntimeCommand equipment:
                ApplyPlayerEquipment(equipment);
                break;

            case PlayerSpawnRuntimeCommand spawn:
                ApplyPlayerSpawn(spawn);
                break;

            case PlayerMovementRuntimeCommand movement:
                ApplyPlayerMovement(movement);
                break;

            case PlayerDisconnectRuntimeCommand disconnect:
                ApplyPlayerDisconnect(disconnect);
                break;
        }
    }

    public void Tick()
    {
        Updates++;
    }

    private void ApplyPlayerAppearance(PlayerAppearanceRuntimeCommand appearance)
    {
        PlayerAppearanceCommitRequest request = appearance.Request;
        if (_players.TryGetValue(request.PlayerSlot.Value, out RuntimePlayerState? activePlayer) &&
            activePlayer.Source != appearance.Source)
        {
            RejectedPlayerAppearances++;
            return;
        }

        AppliedPlayerAppearances++;
        _playerEvents?.PlayerAppearanceUpdated(appearance.Source, in request);
    }

    private void ApplyPlayerEquipment(PlayerEquipmentRuntimeCommand equipment)
    {
        PlayerEquipmentCommitRequest request = equipment.Request;
        if (_players.TryGetValue(request.PlayerSlot.Value, out RuntimePlayerState? activePlayer) &&
            activePlayer.Source != equipment.Source)
        {
            RejectedPlayerEquipmentUpdates++;
            return;
        }

        AppliedPlayerEquipmentUpdates++;
        _playerEvents?.PlayerEquipmentUpdated(equipment.Source, in request);
    }

    private void ApplyPlayerSpawn(PlayerSpawnRuntimeCommand spawn)
    {
        PlayerSpawnCommitRequest request = spawn.Request;
        PlayerSpawnCommitResult commit = spawn.Session.TryCommitSpawn(request.ClaimedSlot);
        Volatile.Write(ref lastSpawnCommitResult, (int)commit);
        if (commit != PlayerSpawnCommitResult.Committed)
            return;

        CommittedPlayerSpawns++;
        _players[request.ClaimedSlot.Value] = new RuntimePlayerState
        {
            Source = spawn.Source,
            Slot = request.ClaimedSlot,
            Team = request.Team,
            PositionX = request.SpawnX * 16f,
            PositionY = request.SpawnY * 16f
        };
        _playerEvents?.PlayerSpawned(spawn.Source, in request);
    }

    private void ApplyPlayerMovement(PlayerMovementRuntimeCommand movement)
    {
        PlayerMovementCommitRequest request = movement.Request;
        if (!_players.TryGetValue(request.PlayerSlot.Value, out RuntimePlayerState? player) ||
            player.Source != movement.Source)
        {
            RejectedPlayerMovements++;
            return;
        }

        player.ControlFlags = request.ControlFlags;
        player.MovementFlags = request.MovementFlags;
        player.MiscFlags1 = request.MiscFlags1;
        player.MiscFlags2 = request.MiscFlags2;
        player.SelectedItem = request.SelectedItem;
        player.PositionX = request.PositionX;
        player.PositionY = request.PositionY;
        player.VelocityX = request.HasVelocity ? request.VelocityX : 0f;
        player.VelocityY = request.HasVelocity ? request.VelocityY : 0f;
        player.MountType = request.HasMount ? request.MountType : (ushort)0;
        player.PotionOfReturnOriginalPositionX = request.HasPotionOfReturnPositions
            ? request.PotionOfReturnOriginalPositionX
            : 0f;
        player.PotionOfReturnOriginalPositionY = request.HasPotionOfReturnPositions
            ? request.PotionOfReturnOriginalPositionY
            : 0f;
        player.PotionOfReturnHomePositionX = request.HasPotionOfReturnPositions
            ? request.PotionOfReturnHomePositionX
            : 0f;
        player.PotionOfReturnHomePositionY = request.HasPotionOfReturnPositions
            ? request.PotionOfReturnHomePositionY
            : 0f;
        player.CameraTargetX = request.HasCameraTarget ? request.CameraTargetX : 0f;
        player.CameraTargetY = request.HasCameraTarget ? request.CameraTargetY : 0f;

        AppliedPlayerMovements++;
        LastMovementPlayerSlot = request.PlayerSlot;
        LastMovementPositionX = request.PositionX;
        LastMovementPositionY = request.PositionY;
        _playerEvents?.PlayerMoved(movement.Source, in request);
    }

    private void ApplyPlayerDisconnect(PlayerDisconnectRuntimeCommand disconnect)
    {
        if (!_players.TryGetValue(disconnect.PlayerSlot.Value, out RuntimePlayerState? player) ||
            player.Source != disconnect.Source)
        {
            return;
        }

        _players.Remove(disconnect.PlayerSlot.Value);
        DisconnectedPlayers++;
        _playerEvents?.PlayerDisconnected(disconnect.Source, disconnect.PlayerSlot);
    }

    private sealed class RuntimePlayerState
    {
        public GameCommandSourceId Source { get; init; }
        public PlayerSlotId Slot { get; init; }
        public byte Team { get; init; }
        public byte ControlFlags { get; set; }
        public byte MovementFlags { get; set; }
        public byte MiscFlags1 { get; set; }
        public byte MiscFlags2 { get; set; }
        public byte SelectedItem { get; set; }
        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float VelocityX { get; set; }
        public float VelocityY { get; set; }
        public ushort MountType { get; set; }
        public float PotionOfReturnOriginalPositionX { get; set; }
        public float PotionOfReturnOriginalPositionY { get; set; }
        public float PotionOfReturnHomePositionX { get; set; }
        public float PotionOfReturnHomePositionY { get; set; }
        public float CameraTargetX { get; set; }
        public float CameraTargetY { get; set; }
    }
}

internal abstract record RuntimeCommand;

internal sealed record ProbeCommand : RuntimeCommand;

internal sealed record WorkerResultCommand(int Value) : RuntimeCommand;
