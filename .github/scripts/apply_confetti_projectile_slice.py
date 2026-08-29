#!/usr/bin/env python3
from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    target = Path(path)
    text = target.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{path}: expected exactly one marker, got {count}: {old[:160]!r}")
    target.write_text(text.replace(old, new, 1), encoding="utf-8")


replace_once(
    "src/TerraRuntime.Contracts/Gameplay/VanillaContentIds.cs",
    "    public static readonly ProjectileTypeId PoisonedKnife = new(54);\n    public static readonly ProjectileTypeId RottenEgg = new(318);",
    "    public static readonly ProjectileTypeId PoisonedKnife = new(54);\n"
    "    public static readonly ProjectileTypeId ConfettiGun = new(178);\n"
    "    public static readonly ProjectileTypeId ConfettiMelee = new(289);\n"
    "    public static readonly ProjectileTypeId RottenEgg = new(318);",
)

replace_once(
    "src/TerraRuntime.Contracts/Gameplay/VanillaProjectileDefinitionCatalog.cs",
    "    private static readonly VanillaProjectileDefinition RottenEggDefinition = new(\n",
    "    private static readonly VanillaProjectileDefinition ConfettiDefinition = new(\n"
    "        Width: 10,\n"
    "        Height: 10,\n"
    "        AiStyle: VanillaProjectileAiStyles.Arrow,\n"
    "        TileCollide: true,\n"
    "        IgnoreWater: false,\n"
    "        CanCutTiles: true,\n"
    "        CollisionWidth: 10,\n"
    "        CollisionHeight: 10);\n\n"
    "    private static readonly VanillaProjectileDefinition RottenEggDefinition = new(\n",
)
replace_once(
    "src/TerraRuntime.Contracts/Gameplay/VanillaProjectileDefinitionCatalog.cs",
    "        if (type == VanillaProjectileIds.RottenEgg)\n        {\n            definition = RottenEggDefinition;\n            return true;\n        }",
    "        if (type == VanillaProjectileIds.ConfettiGun ||\n"
    "            type == VanillaProjectileIds.ConfettiMelee)\n"
    "        {\n"
    "            definition = ConfettiDefinition;\n"
    "            return true;\n"
    "        }\n\n"
    "        if (type == VanillaProjectileIds.RottenEgg)\n"
    "        {\n"
    "            definition = RottenEggDefinition;\n"
    "            return true;\n"
    "        }",
)

replace_once(
    "src/TerraRuntime/VanillaProjectileWorldStateStepper.cs",
    "/// Fire, Unholy, and Jester's Arrows, Bullet, Seed, Bone Arrow, Sound Gun, Bone Shard, and player-owned Green Laser (aiStyle 1), plus Shuriken, Throwing Knife,\n",
    "/// Fire, Unholy, and Jester's Arrows, Bullet, Seed, Confetti Gun/Melee, Bone Arrow, Sound Gun, Bone Shard, and player-owned Green Laser (aiStyle 1), plus Shuriken, Throwing Knife,\n",
)
replace_once(
    "src/TerraRuntime/VanillaProjectileWorldStateStepper.cs",
    "             current.Type == VanillaProjectileIds.Seed ||\n             current.Type == VanillaProjectileIds.BoneArrowFromMerchant ||",
    "             current.Type == VanillaProjectileIds.Seed ||\n"
    "             current.Type == VanillaProjectileIds.ConfettiGun ||\n"
    "             current.Type == VanillaProjectileIds.ConfettiMelee ||\n"
    "             current.Type == VanillaProjectileIds.BoneArrowFromMerchant ||",
)
replace_once(
    "src/TerraRuntime/VanillaProjectileWorldStateStepper.cs",
    "// source-backed Wooden/Fire/Unholy/Jester/Bullet/Seed/BoneArrow/SoundGun/BoneShard/player-owned-GreenLaser path has ai[2] == 0; non-default\n",
    "// source-backed Wooden/Fire/Unholy/Jester/Bullet/Seed/Confetti/BoneArrow/SoundGun/BoneShard/player-owned-GreenLaser path has ai[2] == 0; non-default\n",
)

replace_once(
    "tests/TerraRuntime.Tests/VanillaProjectileDefinitionCatalogTests.cs",
    "    [InlineData(51, 8, 8)]\n    [InlineData(1124, 6, 6)]",
    "    [InlineData(51, 8, 8)]\n"
    "    [InlineData(178, 10, 10)]\n"
    "    [InlineData(289, 10, 10)]\n"
    "    [InlineData(1124, 6, 6)]",
)

replace_once(
    "tests/TerraRuntime.Tests/VanillaProjectileWorldStateStepperTests.cs",
    "    [InlineData(51, 3600)]\n    [InlineData(474, 1200)]",
    "    [InlineData(51, 3600)]\n"
    "    [InlineData(178, 2)]\n"
    "    [InlineData(289, 2)]\n"
    "    [InlineData(474, 1200)]",
)

replace_once(
    "tests/TerraRuntime.Tests/ServerRuntimeVanillaProjectileSimulationTests.cs",
    "    [InlineData(51, 104f, 1f, 3599)]\n    [InlineData(474, 104f, 1f, 1199)]",
    "    [InlineData(51, 104f, 1f, 3599)]\n"
    "    [InlineData(178, 104f, 1f, 1)]\n"
    "    [InlineData(289, 104f, 1f, 1)]\n"
    "    [InlineData(474, 104f, 1f, 1199)]",
)
replace_once(
    "tests/TerraRuntime.Tests/ServerRuntimeVanillaProjectileSimulationTests.cs",
    "    [Fact]\n    public async Task Authoritative_tick_runs_source_backed_player_owned_wooden_arrow_free_flight_by_default()\n",
    "    [Theory]\n"
    "    [InlineData(178)]\n"
    "    [InlineData(289)]\n"
    "    public async Task Authoritative_tick_expires_confetti_projectile_after_second_world_tick(int type)\n"
    "    {\n"
    "        var tiles = new WorldTileStore(new WorldDimensions(100, 100));\n"
    "        var projectiles = new RuntimeProjectileStore(capacity: 4);\n"
    "        var state = new ServerRuntimeState(worldTiles: tiles, projectiles: projectiles);\n"
    "        ProjectileStateUpdate projectile = CreateProjectile(type, spawner: 3);\n"
    "        var completion = new TaskCompletionSource<ProjectileSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);\n"
    "        state.Apply(new ProjectileSpawnRuntimeCommand(0, projectile, completion));\n"
    "        ProjectileSnapshot spawned = Assert.IsType<ProjectileSnapshot>(await completion.Task);\n\n"
    "        state.Tick();\n\n"
    "        Assert.True(state.TryCaptureProjectileSnapshot(spawned.Handle, out ProjectileSnapshot first));\n"
    "        Assert.Equal(new ProjectileRevision(2), first.Revision);\n"
    "        Assert.True(projectiles.TryGetLifecycle(spawned.Handle, out ProjectileLifecycleState firstLifecycle));\n"
    "        Assert.Equal(1, firstLifecycle.TimeLeft);\n\n"
    "        state.Tick();\n\n"
    "        Assert.Equal(new ProjectileStateTickSummary(1, 1, 1, 0), state.LastProjectileTick);\n"
    "        Assert.False(state.TryCaptureProjectileSnapshot(spawned.Handle, out _));\n"
    "    }\n\n"
    "    [Fact]\n"
    "    public async Task Authoritative_tick_runs_source_backed_player_owned_wooden_arrow_free_flight_by_default()\n",
)
replace_once(
    "tests/TerraRuntime.Tests/ServerRuntimeVanillaProjectileSimulationTests.cs",
    "    [InlineData(51)]\n    [InlineData(54)]",
    "    [InlineData(51)]\n"
    "    [InlineData(54)]\n"
    "    [InlineData(178)]\n"
    "    [InlineData(289)]",
)
replace_once(
    "tests/TerraRuntime.Tests/ServerRuntimeVanillaProjectileSimulationTests.cs",
    "    [InlineData(51)]\n    [InlineData(474)]\n    [InlineData(1099)]\n    public async Task Server_owned_single_subupdate_ai_style_one_simulates_when_tile_cut_effect_is_empty(int type)",
    "    [InlineData(51)]\n"
    "    [InlineData(178)]\n"
    "    [InlineData(289)]\n"
    "    [InlineData(474)]\n"
    "    [InlineData(1099)]\n"
    "    public async Task Server_owned_single_subupdate_ai_style_one_simulates_when_tile_cut_effect_is_empty(int type)",
)

# Permanent pinned-source proof.
replace_once(
    "tools/ci/probe_projectile_tile_cut.py",
    "    bone_arrow_defaults = compact(extract_type_if_block(set_defaults, 474))\n    sound_gun_defaults = compact(extract_type_if_block(set_defaults, 1099))\n",
    "    confetti_gun_defaults = compact(extract_type_if_block(set_defaults, 178))\n"
    "    confetti_melee_defaults = compact(extract_type_if_block(set_defaults, 289))\n"
    "    bone_arrow_defaults = compact(extract_type_if_block(set_defaults, 474))\n"
    "    sound_gun_defaults = compact(extract_type_if_block(set_defaults, 1099))\n",
)
replace_once(
    "tools/ci/probe_projectile_tile_cut.py",
    '        "Seed": 51,\n        "BoneArrowFromMerchant": 474,\n',
    '        "Seed": 51,\n'
    '        "ConfettiGun": 178,\n'
    '        "ConfettiMelee": 289,\n'
    '        "BoneArrowFromMerchant": 474,\n',
)

marker = '''    for source_name, source_text in (\n        ("AI_001", arrow_ai),\n        ("AI", projectile_ai),\n        ("Update", projectile_update),\n        ("HandleMovement", handle_movement),\n        ("GetCollisionParams", collision_params),\n        ("Kill", projectile_kill),\n        ("CanCutTiles", can_cut_tiles),\n    ):\n        if count_type_comparisons(source_text, 1099) != 0:\n            raise SystemExit(f"type 1099 unexpectedly special in {source_name}")\n\n'''
addition = marker + '''    for raw_type, name, defaults in (\n        (178, "ConfettiGun", confetti_gun_defaults),\n        (289, "ConfettiMelee", confetti_melee_defaults),\n    ):\n        for token in (\n            "width = 10;",\n            "height = 10;",\n            "aiStyle = 1;",\n            "alpha = 255;",\n            "penetrate = -1;",\n            "timeLeft = 2;",\n        ):\n            if token not in defaults:\n                raise SystemExit(f"{name} default missing: {token}")\n        for forbidden in ("tileCollide = false;", "ignoreWater = true;", "extraUpdates ="):\n            if forbidden in defaults:\n                raise SystemExit(f"{name} unexpected default: {forbidden}")\n\n        for source_name, source_text in (\n            ("AI_001", arrow_ai),\n            ("AI", projectile_ai),\n            ("Update", projectile_update),\n            ("HandleMovement", handle_movement),\n            ("GetCollisionParams", collision_params),\n            ("CanCutTiles", can_cut_tiles),\n        ):\n            if count_type_comparisons(source_text, raw_type) != 0:\n                raise SystemExit(f"{name} unexpectedly special in {source_name}")\n\n        if count_type_comparisons(projectile_kill, raw_type) != 1:\n            raise SystemExit(f"{name} Kill branch count changed")\n        kill_block = compact(extract_type_if_block(projectile_kill, raw_type))\n        for token in ("Dust.NewDust", "Gore.NewGore"):\n            if token not in kill_block:\n                raise SystemExit(f"{name} visual Kill token missing: {token}")\n        for token in ("NewProjectile(", "NewItem(", "KillTile(", "RequestNewItem("):\n            if token in kill_block:\n                raise SystemExit(f"{name} Kill gained authoritative side effect: {token}")\n\n'''
replace_once("tools/ci/probe_projectile_tile_cut.py", marker, addition)
replace_once(
    "tools/ci/probe_projectile_tile_cut.py",
    '    print("projectile_bone_defaults=" + bone_defaults)\n',
    '    print("projectile_bone_defaults=" + bone_defaults)\n'
    '    print("projectile_confetti_gun_defaults=" + confetti_gun_defaults)\n'
    '    print("projectile_confetti_melee_defaults=" + confetti_melee_defaults)\n',
)

print("confetti projectile runtime + permanent source-proof patch applied")
