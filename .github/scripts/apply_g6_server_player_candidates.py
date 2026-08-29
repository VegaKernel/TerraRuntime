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
    """    private readonly RuntimeNpcActorControlRegistry _npcActorControls;\n    private readonly RuntimeNpcActorControlCommandService _npcActorCommands;\n    private readonly RuntimeServerPlayerStateStore? _serverPlayerStates;\n    private readonly INpcAiStateStepper _npcAiStepper;\n""",
    """    private readonly RuntimeNpcActorControlRegistry _npcActorControls;\n    private readonly RuntimeNpcActorControlCommandService _npcActorCommands;\n    private readonly RuntimeServerPlayerStateStore? _serverPlayerStates;\n    private readonly PlayerStateSnapshot[] _serverPlayerSnapshots =\n        new PlayerStateSnapshot[VanillaNpcTargetingAiStepper.MaximumPlayerCandidates];\n    private readonly INpcAiStateStepper _npcAiStepper;\n""",
)

replace_once(
    state,
    """    private int CopyVanillaNpcTargetCandidates(Span<VanillaNpcTargetCandidate> destination)\n    {\n        int written = 0;\n        for (int slot = 0; slot < VanillaNpcTargetingAiStepper.MaximumPlayerCandidates; slot++)\n        {\n            if (!_players.TryGetValue(checked((byte)slot), out RuntimePlayerState? player))\n                continue;\n            if (player.MountType != 0)\n                continue;\n\n            destination[written++] = new VanillaNpcTargetCandidate(\n                Slot: checked((byte)slot),\n                CenterX: player.PositionX + VanillaBasePlayerWidth * 0.5f,\n                CenterY: player.PositionY + VanillaBasePlayerHeight * 0.5f,\n                Aggro: 0,\n                Active: true,\n                Dead: player.IsDead,\n                Ghost: false,\n                NoAggro: false);\n        }\n\n        return written;\n    }\n""",
    """    private int CopyVanillaNpcTargetCandidates(Span<VanillaNpcTargetCandidate> destination)\n    {\n        int serverPlayerCount = _serverPlayerStates?.CopySnapshots(_serverPlayerSnapshots) ?? 0;\n        int serverPlayerIndex = 0;\n        int written = 0;\n\n        for (int slot = 0; slot < VanillaNpcTargetingAiStepper.MaximumPlayerCandidates; slot++)\n        {\n            if (_players.TryGetValue(checked((byte)slot), out RuntimePlayerState? player))\n            {\n                if (player.MountType != 0)\n                    continue;\n\n                destination[written++] = new VanillaNpcTargetCandidate(\n                    Slot: checked((byte)slot),\n                    CenterX: player.PositionX + VanillaBasePlayerWidth * 0.5f,\n                    CenterY: player.PositionY + VanillaBasePlayerHeight * 0.5f,\n                    Aggro: 0,\n                    Active: true,\n                    Dead: player.IsDead,\n                    Ghost: false,\n                    NoAggro: false);\n                continue;\n            }\n\n            while (serverPlayerIndex < serverPlayerCount &&\n                   _serverPlayerSnapshots[serverPlayerIndex].Player.Slot.Value < slot)\n            {\n                serverPlayerIndex++;\n            }\n\n            if (serverPlayerIndex >= serverPlayerCount ||\n                _serverPlayerSnapshots[serverPlayerIndex].Player.Slot.Value != slot)\n            {\n                continue;\n            }\n\n            PlayerStateSnapshot serverPlayer = _serverPlayerSnapshots[serverPlayerIndex++];\n            if (serverPlayer.MountType != 0)\n                continue;\n\n            destination[written++] = new VanillaNpcTargetCandidate(\n                Slot: checked((byte)slot),\n                CenterX: serverPlayer.PositionX + VanillaBasePlayerWidth * 0.5f,\n                CenterY: serverPlayer.PositionY + VanillaBasePlayerHeight * 0.5f,\n                Aggro: 0,\n                Active: true,\n                Dead: serverPlayer.IsDead,\n                Ghost: false,\n                NoAggro: false);\n        }\n\n        return written;\n    }\n""",
)

print("G6 server-player targeting candidates applied")
