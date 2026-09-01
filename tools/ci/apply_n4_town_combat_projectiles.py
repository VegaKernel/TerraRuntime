#!/usr/bin/env python3
from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    p = Path(path)
    text = p.read_text(encoding="utf-8-sig")
    if old not in text:
        raise SystemExit(f"anchor missing in {path}: {old[:120]!r}")
    if text.count(old) != 1:
        raise SystemExit(f"anchor not unique in {path}: {old[:120]!r}")
    p.write_text(text.replace(old, new, 1), encoding="utf-8")


# Production ownership and composition.
server = "src/TerraRuntime/ServerRuntimeState.cs"
replace_once(
    server,
    "    private readonly RuntimeTownNpcSchedule1458? _townSchedule;\n    private readonly RuntimeTownNpcShimmerService1458? _townShimmer;",
    "    private readonly RuntimeTownNpcSchedule1458? _townSchedule;\n    private readonly RuntimeTownNpcCombat1458? _townCombat;\n    private readonly RuntimeTownNpcShimmerService1458? _townShimmer;")
replace_once(
    server,
    "        VanillaTownSpawnWorldFacts1458? townSpawnWorldFacts = null,\n        RuntimeTownCommerceWorldFacts1458? townCommerceWorldFacts = null,\n        bool townInitialRaining = false,",
    "        VanillaTownSpawnWorldFacts1458? townSpawnWorldFacts = null,\n        RuntimeTownCommerceWorldFacts1458? townCommerceWorldFacts = null,\n        RuntimeTownNpcCombatWorldFacts1458? townCombatWorldFacts = null,\n        bool townInitialRaining = false,")
replace_once(
    server,
    "        _townCommerce = worldTiles is not null && townCommerceWorldFacts is RuntimeTownCommerceWorldFacts1458 commerceFacts\n            ? new RuntimeTownCommerceResolver1458(worldTiles, townNpcs, _npcs, in commerceFacts)\n            : null;\n        _housingValidator = worldTiles is not null && townNpcs is not null",
    "        _townCommerce = worldTiles is not null && townCommerceWorldFacts is RuntimeTownCommerceWorldFacts1458 commerceFacts\n            ? new RuntimeTownCommerceResolver1458(worldTiles, townNpcs, _npcs, in commerceFacts)\n            : null;\n        _townCombat = worldTiles is not null &&\n            townNpcs is not null &&\n            _worldProgression is not null &&\n            townCombatWorldFacts is RuntimeTownNpcCombatWorldFacts1458 combatFacts\n                ? new RuntimeTownNpcCombat1458(\n                    townNpcs, _npcs, _projectiles, worldTiles, in combatFacts, _worldProgression, expertMode, masterMode)\n                : null;\n        _housingValidator = worldTiles is not null && townNpcs is not null")
replace_once(
    server,
    "        if (_townMoveIn is null && _townSchedule is null)\n            return;",
    "        if (_townMoveIn is null && _townSchedule is null && _townCombat is null)\n            return;")
replace_once(
    server,
    "            _townSchedule.Tick(in scheduleConditions, _townPlayerBounds.AsSpan(0, boundsCount));\n        }\n    }",
    "            _townSchedule.Tick(in scheduleConditions, _townPlayerBounds.AsSpan(0, boundsCount));\n        }\n\n        _townCombat?.Tick();\n    }")

host = "src/TerraRuntime/TerrariaServerHost.cs"
replace_once(
    host,
    "            townSpawnWorldFacts: RuntimeTownNpcWorldFactsProjection1458.FromMetadata(world.RuntimeMetadata),\n            townCommerceWorldFacts: RuntimeTownCommerceWorldFacts1458.FromMetadata(world.RuntimeMetadata),\n            townInitialRaining: world.RuntimeMetadata.Raining,",
    "            townSpawnWorldFacts: RuntimeTownNpcWorldFactsProjection1458.FromMetadata(world.RuntimeMetadata),\n            townCommerceWorldFacts: RuntimeTownCommerceWorldFacts1458.FromMetadata(world.RuntimeMetadata),\n            townCombatWorldFacts: RuntimeTownNpcCombatWorldFacts1458.FromMetadata(world.RuntimeMetadata),\n            townInitialRaining: world.RuntimeMetadata.Raining,")

# Tighten the admitted state-12 start to the exact source vertical-angle gate.
combat = "src/TerraRuntime/RuntimeTownNpcCombat1458.cs"
replace_once(
    combat,
    "            int chance = GetAttackChance(profile.AttackAverageChance);\n            if (random.Next(chance) != 0)\n                continue;",
    "            if (profile.Kind == VanillaTownNpcProjectileAttackKind1458.Straight &&\n                !HasStraightAttackAngle(in source, in target))\n            {\n                continue;\n            }\n\n            int chance = GetAttackChance(profile.AttackAverageChance);\n            if (random.Next(chance) != 0)\n                continue;")
replace_once(
    combat,
    "    private bool TrySelectTarget(\n",
    "    private static bool HasStraightAttackAngle(in NpcSnapshot source, in NpcSnapshot target)\n    {\n        if (!VanillaTownNpcDefinitionCatalogBridge.TryGetCenter(in source, out float sourceX, out float sourceY) ||\n            !VanillaTownNpcDefinitionCatalogBridge.TryGetCenter(in target, out float targetX, out float targetY))\n        {\n            return false;\n        }\n\n        float dx = targetX - sourceX;\n        float dy = targetY - sourceY;\n        float length = MathF.Sqrt(dx * dx + dy * dy);\n        if (!float.IsFinite(length) || length <= float.Epsilon)\n            return false;\n        float normalizedY = dy / length;\n        return normalizedY is >= -0.5f and <= 0.5f;\n    }\n\n    private bool TrySelectTarget(\n")
replace_once(
    combat,
    "    private bool IsComplete(VanillaWorldProgressionId milestone) =>",
    "    private static class VanillaTownNpcDefinitionCatalogBridge\n    {\n        public static bool TryGetCenter(\n            in NpcSnapshot snapshot,\n            out float centerX,\n            out float centerY)\n        {\n            if (!NpcTypeId.TryCreate(snapshot.Type, out NpcTypeId type) ||\n                !VanillaNpcDefinitionCatalog.TryGet(type, snapshot.NetIdentity, out VanillaNpcDefinition definition) ||\n                !definition.TryResolveHitbox(snapshot.Simulation.Scale, out VanillaNpcHitboxSize hitbox))\n            {\n                centerX = 0f;\n                centerY = 0f;\n                return false;\n            }\n\n            centerX = snapshot.PositionX + hitbox.Width * 0.5f;\n            centerY = snapshot.PositionY + hitbox.Height * 0.5f;\n            return true;\n        }\n    }\n\n    private bool IsComplete(VanillaWorldProgressionId milestone) =>")

# Fix fixture literals and assert the Expert hardmode Guide damage path.
tests = "tests/TerraRuntime.Tests/RuntimeTownNpcCombat1458Tests.cs"
replace_once(tests, "    [InlineData(true, 2, 25)]", "    [InlineData(true, 2, 37)]")
replace_once(
    tests,
    "                        Type = VanillaTileIds.Stone.Value,",
    "                        Type = checked((ushort)VanillaTileIds.Stone.Value),")

roadmap = "docs/roadmap/npc-ai-parity.md"
replace_once(
    roadmap,
    "- [ ] town AI, housing and schedules;\n- [ ] shops, happiness and progression-dependent inventory;",
    "- [ ] town AI, housing and schedules;\n  - source-backed AI_007 shelter/home/chair scheduling, shimmer state 25, and an authoritative projectile-combat slice for Merchant/Nurse/Arms Dealer/Guide are implemented; social/emote/melee/special town branches remain open;\n- [ ] shops, happiness and progression-dependent inventory;")

print("N4 Town NPC projectile-combat production integration applied")
