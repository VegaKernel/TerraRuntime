from pathlib import Path

path = Path("src/TerraRuntime/ServerRuntimeState.cs")
text = path.read_text()

replacements = [
    (
        "    private readonly VanillaNpcTargetingAiStepper? _vanillaNpcTargetingAiStepper;\n    private readonly RuntimeProjectileStore _projectiles;",
        "    private readonly VanillaNpcTargetingAiStepper? _vanillaNpcTargetingAiStepper;\n    private readonly VanillaNpcCheckActiveAiStepper? _vanillaNpcCheckActiveAiStepper;\n    private readonly RuntimeProjectileStore _projectiles;",
    ),
    (
        "            _vanillaNpcTargetingAiStepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());\n            _npcAiStepper = worldTiles is null\n                ? _vanillaNpcTargetingAiStepper\n                : new VanillaNpcWorldMotionAiStepper(_vanillaNpcTargetingAiStepper, worldTiles);",
        "            _vanillaNpcTargetingAiStepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());\n            if (worldTiles is null)\n            {\n                _npcAiStepper = _vanillaNpcTargetingAiStepper;\n            }\n            else\n            {\n                var worldMotion = new VanillaNpcWorldMotionAiStepper(_vanillaNpcTargetingAiStepper, worldTiles);\n                _vanillaNpcCheckActiveAiStepper = new VanillaNpcCheckActiveAiStepper(worldMotion);\n                _npcAiStepper = _vanillaNpcCheckActiveAiStepper;\n            }",
    ),
    (
        "            int candidateCount = CopyVanillaNpcTargetCandidates(_npcTargetCandidates);\n            _vanillaNpcTargetingAiStepper.SetCandidates(_npcTargetCandidates.AsSpan(0, candidateCount));",
        "            int candidateCount = CopyVanillaNpcTargetCandidates(_npcTargetCandidates);\n            ReadOnlySpan<VanillaNpcTargetCandidate> candidates = _npcTargetCandidates.AsSpan(0, candidateCount);\n            _vanillaNpcTargetingAiStepper.SetCandidates(candidates);\n            _vanillaNpcCheckActiveAiStepper?.SetCandidates(candidates);",
    ),
    (
        "        LastNpcAiTick = _npcAiExecutor.Tick(_npcAiStepper);\n        if (_projectileStepper is not null)",
        "        LastNpcAiTick = _npcAiExecutor.Tick(_npcAiStepper);\n        AppliedNpcDespawns += _npcs.DespawnExpired();\n        if (_projectileStepper is not null)",
    ),
]

for old, new in replacements:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"expected exactly one guarded match, found {count}: {old[:100]!r}")
    text = text.replace(old, new, 1)

path.write_text(text)
