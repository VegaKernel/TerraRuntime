from pathlib import Path


def replace_once(path: Path, old: str, new: str) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{path}: expected exactly one replacement target, found {count}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8")


state = Path("src/TerraRuntime/ServerRuntimeState.cs")

replace_once(
    state,
    """    private readonly RuntimeServerPlayerStateStore? _serverPlayerStates;\n    private readonly RuntimeServerPlayerCommandService? _serverPlayerCommands;\n    private readonly PlayerStateSnapshot[] _serverPlayerSnapshots =\n""",
    """    private readonly RuntimeServerPlayerStateStore? _serverPlayerStates;\n    private readonly RuntimeServerPlayerCommandService? _serverPlayerCommands;\n    private readonly VanillaServerPlayerDryPhysicsStepper? _serverPlayerDryPhysics;\n    private readonly PlayerStateSnapshot[] _serverPlayerSnapshots =\n""",
)

replace_once(
    state,
    """        _serverPlayerCommands = serverPlayerIdentities is not null && serverPlayerStates is not null\n            ? new RuntimeServerPlayerCommandService(serverPlayerIdentities, serverPlayerStates)\n            : null;\n        _npcActorControls = new RuntimeNpcActorControlRegistry(_npcs);\n""",
    """        _serverPlayerCommands = serverPlayerIdentities is not null && serverPlayerStates is not null\n            ? new RuntimeServerPlayerCommandService(serverPlayerIdentities, serverPlayerStates)\n            : null;\n        _serverPlayerDryPhysics = serverPlayerStates is not null && worldTiles is not null\n            ? new VanillaServerPlayerDryPhysicsStepper(worldTiles)\n            : null;\n        _npcActorControls = new RuntimeNpcActorControlRegistry(_npcs);\n""",
)

replace_once(
    state,
    """    public void Tick()\n    {\n        _npcActorCommands.CommitPending();\n\n        if (_vanillaNpcTargetingAiStepper is not null)\n""",
    """    public void Tick()\n    {\n        _npcActorCommands.CommitPending();\n        TickServerPlayerPhysics();\n\n        if (_vanillaNpcTargetingAiStepper is not null)\n""",
)

replace_once(
    state,
    """        _worldClock?.Tick();\n        Updates++;\n    }\n\n    private int CopyVanillaNpcTargetCandidates(Span<VanillaNpcTargetCandidate> destination)\n""",
    """        _worldClock?.Tick();\n        Updates++;\n    }\n\n    private void TickServerPlayerPhysics()\n    {\n        if (_serverPlayerStates is null || _serverPlayerDryPhysics is null)\n            return;\n\n        int count = _serverPlayerStates.CopySnapshots(_serverPlayerSnapshots);\n        for (int index = 0; index < count; index++)\n        {\n            PlayerStateSnapshot player = _serverPlayerSnapshots[index];\n            if (!_serverPlayerDryPhysics.TryStep(in player, out ServerPlayerDryPhysicsStepResult next))\n                continue;\n\n            if (next.PositionX == player.PositionX &&\n                next.PositionY == player.PositionY &&\n                next.VelocityX == player.VelocityX &&\n                next.VelocityY == player.VelocityY)\n            {\n                continue;\n            }\n\n            _serverPlayerStates.TrySetMotion(\n                player.Player,\n                next.PositionX,\n                next.PositionY,\n                next.VelocityX,\n                next.VelocityY,\n                out _);\n        }\n    }\n\n    private int CopyVanillaNpcTargetCandidates(Span<VanillaNpcTargetCandidate> destination)\n""",
)

print("G6 dry player physics wiring applied")
