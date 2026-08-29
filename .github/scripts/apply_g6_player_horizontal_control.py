from pathlib import Path

path = Path("src/TerraRuntime/ServerRuntimeState.cs")
text = path.read_text(encoding="utf-8")
old = """            PlayerStateSnapshot player = _serverPlayerSnapshots[index];
            if (!_serverPlayerDryPhysics.TryStep(in player, out ServerPlayerDryPhysicsStepResult next))
                continue;
"""
new = """            PlayerStateSnapshot player = _serverPlayerSnapshots[index];
            ServerPlayerHorizontalIntent horizontalIntent =
                _serverPlayerCommands?.GetHorizontalIntent(player.Player) ?? ServerPlayerHorizontalIntent.Stop;
            if (!_serverPlayerDryPhysics.TryStep(
                    in player,
                    horizontalIntent,
                    out ServerPlayerDryPhysicsStepResult next))
            {
                continue;
            }
"""
count = text.count(old)
if count != 1:
    raise SystemExit(f"horizontal physics anchor: expected exactly one match, found {count}")
path.write_text(text.replace(old, new, 1), encoding="utf-8")
print("Applied guarded G6-D horizontal-control wiring.")
