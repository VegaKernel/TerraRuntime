from pathlib import Path


def replace_once(path: Path, old: str, new: str) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{path}: expected exactly one replacement target, found {count}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8")


state = Path("src/TerraRuntime/ServerRuntimeState.cs")
host = Path("src/TerraRuntime/TerrariaServerHost.cs")

replace_once(
    state,
    "internal sealed class ServerRuntimeState\n{",
    "internal sealed class ServerRuntimeState : IRuntimePlayerSnapshotLookup\n{",
)

replace_once(
    state,
    """    private readonly RuntimeNpcStore _npcs;\n    private readonly RuntimeNpcAiStateExecutor _npcAiExecutor;\n    private readonly INpcAiStateStepper _npcAiStepper;\n""",
    """    private readonly RuntimeNpcStore _npcs;\n    private readonly RuntimeNpcAiStateExecutor _npcAiExecutor;\n    private readonly RuntimeNpcActorControlRegistry _npcActorControls;\n    private readonly RuntimeNpcActorControlCommandService _npcActorCommands;\n    private readonly RuntimeServerPlayerStateStore? _serverPlayerStates;\n    private readonly INpcAiStateStepper _npcAiStepper;\n""",
)

replace_once(
    state,
    """        RuntimeWorldItemStore? worldItems = null,\n        RuntimeProjectileReplicationRegistry? projectileReplication = null,\n        RuntimeTileManipulationReplicationRegistry? tileManipulationReplication = null)\n""",
    """        RuntimeWorldItemStore? worldItems = null,\n        RuntimeProjectileReplicationRegistry? projectileReplication = null,\n        RuntimeTileManipulationReplicationRegistry? tileManipulationReplication = null,\n        RuntimeServerPlayerStateStore? serverPlayerStates = null)\n""",
)

replace_once(
    state,
    """        _npcs = npcs ?? new RuntimeNpcStore();\n        _npcAiExecutor = new RuntimeNpcAiStateExecutor(_npcs);\n""",
    """        _npcs = npcs ?? new RuntimeNpcStore();\n        _npcAiExecutor = new RuntimeNpcAiStateExecutor(_npcs);\n        _serverPlayerStates = serverPlayerStates;\n        _npcActorControls = new RuntimeNpcActorControlRegistry(_npcs);\n        _npcActorCommands = new RuntimeNpcActorControlCommandService(_npcs, _npcActorControls);\n""",
)

replace_once(
    state,
    """        if (npcAiStepper is null)\n        {\n            _vanillaNpcTargetingAiStepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());\n            if (worldTiles is null)\n            {\n                _npcAiStepper = _vanillaNpcTargetingAiStepper;\n            }\n            else\n            {\n                var worldMotion = new VanillaNpcWorldMotionAiStepper(_vanillaNpcTargetingAiStepper, worldTiles);\n                _vanillaNpcCheckActiveAiStepper = new VanillaNpcCheckActiveAiStepper(worldMotion);\n                _npcAiStepper = _vanillaNpcCheckActiveAiStepper;\n            }\n        }\n        else\n        {\n            _npcAiStepper = npcAiStepper;\n        }\n""",
    """        if (npcAiStepper is null)\n        {\n            _vanillaNpcTargetingAiStepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());\n            var actorIntent = new RuntimeNpcActorIntentStateStepper(\n                _vanillaNpcTargetingAiStepper,\n                _npcActorControls,\n                this);\n            if (worldTiles is null)\n            {\n                _npcAiStepper = actorIntent;\n            }\n            else\n            {\n                double worldSurfaceTiles = worldTiles.WorldSurfaceTiles ??\n                    Math.Max(1d, worldTiles.Dimensions.HeightTiles / 3d);\n                _vanillaNpcTargetingAiStepper.EnableBlueSlimeMotion(worldSurfaceTiles);\n                _vanillaNpcTargetingAiStepper.EnableZombieMotion(worldSurfaceTiles);\n                var worldMotion = new VanillaNpcWorldMotionAiStepper(\n                    actorIntent,\n                    worldTiles,\n                    worldSurfaceTiles);\n                _vanillaNpcCheckActiveAiStepper = new VanillaNpcCheckActiveAiStepper(worldMotion);\n                _npcAiStepper = _vanillaNpcCheckActiveAiStepper;\n            }\n        }\n        else\n        {\n            _npcAiStepper = npcAiStepper;\n        }\n""",
)

replace_once(
    state,
    """    internal bool TryCapturePlayerSnapshot(PlayerHandle player, out PlayerStateSnapshot snapshot)\n    {\n        if (!_players.TryGetValue(player.Slot.Value, out RuntimePlayerState? state) ||\n            state.Connection.Player != player)\n        {\n            snapshot = default;\n            return false;\n        }\n\n        snapshot = state.CaptureSnapshot();\n        return true;\n    }\n\n""",
    """    internal bool TryCapturePlayerSnapshot(PlayerHandle player, out PlayerStateSnapshot snapshot)\n    {\n        if (!_players.TryGetValue(player.Slot.Value, out RuntimePlayerState? state) ||\n            state.Connection.Player != player)\n        {\n            snapshot = default;\n            return false;\n        }\n\n        snapshot = state.CaptureSnapshot();\n        return true;\n    }\n\n    private bool TryCaptureRuntimePlayerSnapshot(PlayerHandle player, out PlayerStateSnapshot snapshot)\n    {\n        if (TryCapturePlayerSnapshot(player, out snapshot))\n            return true;\n\n        if (_serverPlayerStates is not null && _serverPlayerStates.TryGet(player, out snapshot))\n            return true;\n\n        snapshot = default;\n        return false;\n    }\n\n    bool IRuntimePlayerSnapshotLookup.TryGetPlayer(\n        PlayerHandle player,\n        out PlayerStateSnapshot snapshot) =>\n        TryCaptureRuntimePlayerSnapshot(player, out snapshot);\n\n""",
)

replace_once(
    state,
    """        ArgumentNullException.ThrowIfNull(command);\n        AppliedCommands++;\n\n        switch (command)\n""",
    """        ArgumentNullException.ThrowIfNull(command);\n        AppliedCommands++;\n\n        if (_npcActorCommands.TryApply(command))\n            return;\n\n        switch (command)\n""",
)

replace_once(
    state,
    """    public void Tick()\n    {\n        if (_vanillaNpcTargetingAiStepper is not null)\n""",
    """    public void Tick()\n    {\n        _npcActorCommands.CommitPending();\n\n        if (_vanillaNpcTargetingAiStepper is not null)\n""",
)

replace_once(
    state,
    """    private void CompletePlayerSnapshot(PlayerStateSnapshotRuntimeCommand command)\n    {\n        PlayerStateSnapshot? result = TryCapturePlayerSnapshot(command.Player, out PlayerStateSnapshot snapshot)\n            ? snapshot\n            : null;\n        command.Completion.TrySetResult(result);\n    }\n""",
    """    private void CompletePlayerSnapshot(PlayerStateSnapshotRuntimeCommand command)\n    {\n        PlayerStateSnapshot? result = TryCaptureRuntimePlayerSnapshot(command.Player, out PlayerStateSnapshot snapshot)\n            ? snapshot\n            : null;\n        command.Completion.TrySetResult(result);\n    }\n""",
)

replace_once(
    host,
    """        var playerEvents = new RuntimePlayerEventFanout(playerNetworkEvents, chestAndEntityReplicationEvents);\n        var state = new ServerRuntimeState(\n            playerEvents,\n            npcs: npcStore,\n            worldTiles: world.Tiles,\n            worldClock: worldClock,\n            projectiles: projectileStore,\n            worldItems: worldItems,\n            projectileReplication: projectileReplication,\n            tileManipulationReplication: tileManipulationReplication);\n""",
    """        var playerEvents = new RuntimePlayerEventFanout(playerNetworkEvents, chestAndEntityReplicationEvents);\n        var slots = new PlayerSlotPool(options.MaxPlayers);\n        var serverPlayerIdentities = new RuntimeServerPlayerSlotRegistry(slots);\n        var serverPlayerStates = new RuntimeServerPlayerStateStore(serverPlayerIdentities, slots.Capacity);\n        var state = new ServerRuntimeState(\n            playerEvents,\n            npcs: npcStore,\n            worldTiles: world.Tiles,\n            worldClock: worldClock,\n            projectiles: projectileStore,\n            worldItems: worldItems,\n            projectileReplication: projectileReplication,\n            tileManipulationReplication: tileManipulationReplication,\n            serverPlayerStates: serverPlayerStates);\n""",
)

replace_once(
    host,
    """        var disconnectIngress = new RuntimePlayerDisconnectIngress(commandIngress);\n        var slots = new PlayerSlotPool(options.MaxPlayers);\n        var admission = new TerrariaConnectionAdmissionGate(options.MaxPlayers);\n""",
    """        var disconnectIngress = new RuntimePlayerDisconnectIngress(commandIngress);\n        var admission = new TerrariaConnectionAdmissionGate(options.MaxPlayers);\n""",
)

print("G6 actor production wiring applied")
