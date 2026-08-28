from pathlib import Path


def replace_once(path: Path, old: str, new: str, label: str) -> None:
    text = path.read_text()
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly once, found {count}")
    path.write_text(text.replace(old, new, 1))


state = Path("src/TerraRuntime/ServerRuntimeState.cs")
replace_once(
    state,
    """    private readonly INpcAiStateStepper _npcAiStepper;\n    private readonly VanillaNpcTargetingAiStepper? _vanillaNpcTargetingAiStepper;\n""",
    """    private readonly INpcAiStateStepper _npcAiStepper;\n    private readonly VanillaNpcTargetingAiStepper? _vanillaNpcTargetingAiStepper;\n    private readonly RuntimeWorldClock? _worldClock;\n""",
    "state field",
)
replace_once(
    state,
    """        RuntimeNpcStore? npcs = null,\n        INpcAiStateStepper? npcAiStepper = null,\n        WorldTileStore? worldTiles = null)\n    {\n        _playerEvents = playerEvents;\n""",
    """        RuntimeNpcStore? npcs = null,\n        INpcAiStateStepper? npcAiStepper = null,\n        WorldTileStore? worldTiles = null,\n        RuntimeWorldClock? worldClock = null)\n    {\n        _playerEvents = playerEvents;\n        _worldClock = worldClock;\n""",
    "state constructor",
)
replace_once(
    state,
    """        if (_vanillaNpcTargetingAiStepper is not null)\n        {\n            int candidateCount = CopyVanillaNpcTargetCandidates(_npcTargetCandidates);\n            _vanillaNpcTargetingAiStepper.SetCandidates(_npcTargetCandidates.AsSpan(0, candidateCount));\n        }\n\n        LastNpcAiTick = _npcAiExecutor.Tick(_npcAiStepper);\n        Updates++;\n""",
    """        if (_vanillaNpcTargetingAiStepper is not null)\n        {\n            int candidateCount = CopyVanillaNpcTargetCandidates(_npcTargetCandidates);\n            _vanillaNpcTargetingAiStepper.SetCandidates(_npcTargetCandidates.AsSpan(0, candidateCount));\n            if (_worldClock is not null)\n            {\n                _vanillaNpcTargetingAiStepper.SetWorldConditions(\n                    _worldClock.DayTime,\n                    _worldClock.SlimeRainActive);\n            }\n        }\n\n        LastNpcAiTick = _npcAiExecutor.Tick(_npcAiStepper);\n        _worldClock?.Tick();\n        Updates++;\n""",
    "state tick",
)

host = Path("src/TerraRuntime/TerrariaServerHost.cs")
replace_once(
    host,
    """        var worldItems = new RuntimeWorldItemStore();\n        var runtimeConnections = new RuntimeConnectionRegistry(\n""",
    """        var worldItems = new RuntimeWorldItemStore();\n        var worldClock = RuntimeWorldClock.FromWorld(world.RuntimeMetadata, world.CreativePowers);\n        var runtimeConnections = new RuntimeConnectionRegistry(\n""",
    "host clock creation",
)
replace_once(
    host,
    """        var playerEvents = new RuntimePlayerEventFanout(playerNetworkEvents, npcReplication);\n        var state = new ServerRuntimeState(playerEvents, npcs: npcStore, worldTiles: world.Tiles);\n""",
    """        var playerEvents = new RuntimePlayerEventFanout(playerNetworkEvents, npcReplication);\n        var state = new ServerRuntimeState(\n            playerEvents,\n            npcs: npcStore,\n            worldTiles: world.Tiles,\n            worldClock: worldClock);\n""",
    "host state wiring",
)
