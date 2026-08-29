#!/usr/bin/env python3
from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{path}: expected one marker, got {count}: {old[:120]!r}")
    p.write_text(text.replace(old, new, 1), encoding="utf-8")


# Named source-backed identity.
replace_once(
    "src/TerraRuntime.Contracts/Gameplay/VanillaContentIds.cs",
    "    public static readonly ProjectileTypeId StarAnise = new(330);\n"
    "    public static readonly ProjectileTypeId NurseSyringeHurt = new(583);",
    "    public static readonly ProjectileTypeId StarAnise = new(330);\n"
    "    public static readonly ProjectileTypeId BoneArrowFromMerchant = new(474);\n"
    "    public static readonly ProjectileTypeId NurseSyringeHurt = new(583);")

# Catalog shape: SetDefaults leaves tileCollide=true, ignoreWater=false and ordinary 10x10 collision inherited.
catalog = "src/TerraRuntime.Contracts/Gameplay/VanillaProjectileDefinitionCatalog.cs"
replace_once(
    catalog,
    "    private static readonly VanillaProjectileDefinition NurseSyringeHurtDefinition = new(\n",
    "    private static readonly VanillaProjectileDefinition BoneArrowFromMerchantDefinition = new(\n"
    "        Width: 10,\n"
    "        Height: 10,\n"
    "        AiStyle: VanillaProjectileAiStyles.Arrow,\n"
    "        TileCollide: true,\n"
    "        IgnoreWater: false,\n"
    "        CanCutTiles: true,\n"
    "        CollisionWidth: 10,\n"
    "        CollisionHeight: 10);\n\n"
    "    private static readonly VanillaProjectileDefinition NurseSyringeHurtDefinition = new(\n")
replace_once(
    catalog,
    "        if (type == VanillaProjectileIds.NurseSyringeHurt)\n"
    "        {\n"
    "            definition = NurseSyringeHurtDefinition;\n"
    "            return true;\n"
    "        }",
    "        if (type == VanillaProjectileIds.BoneArrowFromMerchant)\n"
    "        {\n"
    "            definition = BoneArrowFromMerchantDefinition;\n"
    "            return true;\n"
    "        }\n\n"
    "        if (type == VanillaProjectileIds.NurseSyringeHurt)\n"
    "        {\n"
    "            definition = NurseSyringeHurtDefinition;\n"
    "            return true;\n"
    "        }")

# Generic aiStyle-1 trajectory. Pinned source has no type-474 branch in AI_001/AI/Update/HandleMovement/GetCollisionParams.
stepper = "src/TerraRuntime/VanillaProjectileWorldStateStepper.cs"
replace_once(
    stepper,
    "/// Fire, Unholy, and Jester's Arrows, Bullet, Seed, Bone Shard, and player-owned Green Laser (aiStyle 1), plus Shuriken, Throwing Knife,",
    "/// Fire, Unholy, and Jester's Arrows, Bullet, Seed, Bone Arrow, Bone Shard, and player-owned Green Laser (aiStyle 1), plus Shuriken, Throwing Knife,")
replace_once(
    stepper,
    "             current.Type == VanillaProjectileIds.Seed ||\n"
    "             current.Type == VanillaProjectileIds.BoneShard ||",
    "             current.Type == VanillaProjectileIds.Seed ||\n"
    "             current.Type == VanillaProjectileIds.BoneArrowFromMerchant ||\n"
    "             current.Type == VanillaProjectileIds.BoneShard ||")
replace_once(
    stepper,
    "// source-backed Wooden/Fire/Unholy/Jester/Bullet/Seed/BoneShard/player-owned-GreenLaser path has ai[2] == 0; non-default",
    "// source-backed Wooden/Fire/Unholy/Jester/Bullet/Seed/BoneArrow/BoneShard/player-owned-GreenLaser path has ai[2] == 0; non-default")

# Catalog contract shares the ordinary 10x10 arrow shape.
replace_once(
    "tests/TerraRuntime.Tests/VanillaProjectileDefinitionCatalogTests.cs",
    "    [InlineData(4)]\n"
    "    public void Terraria_1458_arrow_family_definitions_match_source(int type)",
    "    [InlineData(4)]\n"
    "    [InlineData(474)]\n"
    "    public void Terraria_1458_arrow_family_definitions_match_source(int type)")

# Direct stepper trajectory, liquid scaling and generic tile-impact kill path.
stepper_tests = "tests/TerraRuntime.Tests/VanillaProjectileWorldStateStepperTests.cs"
replace_once(
    stepper_tests,
    "    [InlineData(51, 3600)]\n"
    "    [InlineData(1124, 600)]",
    "    [InlineData(51, 3600)]\n"
    "    [InlineData(474, 1200)]\n"
    "    [InlineData(1124, 600)]")
replace_once(
    stepper_tests,
    "    [Fact]\n"
    "    public void Wooden_arrow_free_flight_matches_ai001_before_gravity()",
    "    [Fact]\n"
    "    public void Bone_arrow_from_merchant_water_contact_uses_generic_half_speed_liquid_motion()\n"
    "    {\n"
    "        var tiles = new WorldTileStore(new WorldDimensions(100, 100));\n"
    "        tiles.Set(6, 6, LiquidTile(WorldLiquidKind.Water));\n"
    "        var stepper = new VanillaProjectileWorldStateStepper(tiles);\n"
    "        ProjectileSnapshot arrow = CreateSnapshot(\n"
    "            positionX: 100f,\n"
    "            positionY: 100f,\n"
    "            velocityX: 4f,\n"
    "            velocityY: 2f) with\n"
    "        {\n"
    "            Type = VanillaProjectileIds.BoneArrowFromMerchant\n"
    "        };\n"
    "        ProjectileSimulationStepContext context = CreateContext(arrow, timeLeft: 1200);\n\n"
    "        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));\n\n"
    "        Assert.Equal(VanillaProjectileIds.BoneArrowFromMerchant, next.State.Type);\n"
    "        Assert.Equal(102f, next.State.PositionX, 5);\n"
    "        Assert.Equal(101f, next.State.PositionY, 5);\n"
    "        Assert.Equal(4f, next.State.VelocityX, 5);\n"
    "        Assert.Equal(2f, next.State.VelocityY, 5);\n"
    "        Assert.Equal(1199, next.TimeLeft);\n"
    "        Assert.True(next.Liquid.GetValueOrDefault().Wet);\n"
    "    }\n\n"
    "    [Fact]\n"
    "    public void Bone_arrow_from_merchant_tile_impact_uses_generic_collision_kill_path()\n"
    "    {\n"
    "        var tiles = new WorldTileStore(new WorldDimensions(100, 100));\n"
    "        tiles.Set(7, 10, SolidTile(1));\n"
    "        var stepper = new VanillaProjectileWorldStateStepper(tiles);\n"
    "        ProjectileSnapshot arrow = CreateSnapshot(\n"
    "            positionX: 100f,\n"
    "            positionY: 160f,\n"
    "            velocityX: 20f,\n"
    "            velocityY: 0f) with\n"
    "        {\n"
    "            Type = VanillaProjectileIds.BoneArrowFromMerchant\n"
    "        };\n"
    "        ProjectileSimulationStepContext context = CreateContext(arrow, timeLeft: 1200);\n\n"
    "        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));\n\n"
    "        Assert.Equal(2f, next.State.VelocityX, 5);\n"
    "        Assert.Equal(0f, next.State.VelocityY, 5);\n"
    "        Assert.Equal(104f, next.State.PositionX, 5);\n"
    "        Assert.Equal(160f, next.State.PositionY, 5);\n"
    "        Assert.Equal(0, next.TimeLeft);\n"
    "    }\n\n"
    "    [Fact]\n"
    "    public void Wooden_arrow_free_flight_matches_ai001_before_gravity()")

# Server integration: default extraUpdates=0, pinned timeLeft=1200, one commit/revision per tick.
server_tests = "tests/TerraRuntime.Tests/ServerRuntimeVanillaProjectileSimulationTests.cs"
replace_once(
    server_tests,
    "    [Theory]\n"
    "    [InlineData(51, 104f, 1f)]\n"
    "    [InlineData(1124, 108f, 2f)]\n"
    "    public async Task Authoritative_tick_runs_source_backed_simple_ai_style_one_family_by_default(\n"
    "        int type, float expectedPositionX, float expectedAi0)",
    "    [Theory]\n"
    "    [InlineData(51, 104f, 1f, 3599)]\n"
    "    [InlineData(474, 104f, 1f, 1199)]\n"
    "    [InlineData(1124, 108f, 2f, 598)]\n"
    "    public async Task Authoritative_tick_runs_source_backed_simple_ai_style_one_family_by_default(\n"
    "        int type, float expectedPositionX, float expectedAi0, int expectedTimeLeft)")
replace_once(
    server_tests,
    "        Assert.Equal(expectedPositionX, updated.PositionX, 5);\n"
    "        Assert.Equal(100f, updated.PositionY, 5);\n"
    "        Assert.Equal(expectedAi0, updated.Ai.Ai0, 5);\n"
    "    }\n\n"
    "    [Fact]\n"
    "    public async Task Authoritative_tick_runs_source_backed_player_owned_wooden_arrow_free_flight_by_default()",
    "        Assert.Equal(expectedPositionX, updated.PositionX, 5);\n"
    "        Assert.Equal(100f, updated.PositionY, 5);\n"
    "        Assert.Equal(expectedAi0, updated.Ai.Ai0, 5);\n"
    "        Assert.True(projectiles.TryGetLifecycle(spawned.Handle, out ProjectileLifecycleState lifecycle));\n"
    "        Assert.Equal(expectedTimeLeft, lifecycle.TimeLeft);\n"
    "    }\n\n"
    "    [Fact]\n"
    "    public async Task Authoritative_tick_runs_source_backed_player_owned_wooden_arrow_free_flight_by_default()")
replace_once(
    server_tests,
    "    [InlineData(330)]\n"
    "    [InlineData(583)]\n"
    "    [InlineData(589)]\n"
    "    [InlineData(599)]\n"
    "    [InlineData(1012)]\n"
    "    [InlineData(1111)]\n"
    "    [InlineData(1124)]\n"
    "    public async Task Server_owned_source_backed_projectile_remains_authoritative_when_tile_cut_effect_is_not_yet_modeled(int type)",
    "    [InlineData(330)]\n"
    "    [InlineData(474)]\n"
    "    [InlineData(583)]\n"
    "    [InlineData(589)]\n"
    "    [InlineData(599)]\n"
    "    [InlineData(1012)]\n"
    "    [InlineData(1111)]\n"
    "    [InlineData(1124)]\n"
    "    public async Task Server_owned_source_backed_projectile_remains_authoritative_when_tile_cut_effect_is_not_yet_modeled(int type)")
replace_once(
    server_tests,
    "    [InlineData(1)]\n"
    "    [InlineData(2)]\n"
    "    [InlineData(4)]\n"
    "    public async Task Server_owned_arrow_simulates_when_tile_cut_effect_is_empty(int type)",
    "    [InlineData(1)]\n"
    "    [InlineData(2)]\n"
    "    [InlineData(4)]\n"
    "    [InlineData(474)]\n"
    "    public async Task Server_owned_arrow_simulates_when_tile_cut_effect_is_empty(int type)")

# Permanent source contract: pin the real nullable wind override initializer and type-474 visual-only Kill branch.
probe = "tools/ci/probe_projectile_tile_cut.py"
replace_once(
    probe,
    "def relevant_drop_contexts(source: str) -> str:\n",
    "def extract_factory_initializer(source: str, field_name: str) -> str:\n"
    "    match = re.search(\n"
    "        rf\"{re.escape(field_name)}\\s*=\\s*Factory\\.CreateCustomSet<bool\\?>\\s*\\(\",\n"
    "        source)\n"
    "    if match is None:\n"
    "        raise SystemExit(f\"factory initializer not found: {field_name}\")\n\n"
    "    opening = source.find(\"(\", match.start())\n"
    "    depth = 0\n"
    "    in_string = False\n"
    "    escaped = False\n"
    "    for index in range(opening, len(source)):\n"
    "        char = source[index]\n"
    "        if escaped:\n"
    "            escaped = False\n"
    "            continue\n"
    "        if char == \\\"\\\\\\\" and in_string:\n"
    "            escaped = True\n"
    "            continue\n"
    "        if char == '\"':\n"
    "            in_string = not in_string\n"
    "            continue\n"
    "        if in_string:\n"
    "            continue\n"
    "        if char == \"(\":\n"
    "            depth += 1\n"
    "        elif char == \")\":\n"
    "            depth -= 1\n"
    "            if depth == 0:\n"
    "                return source[match.start() : index + 1]\n"
    "    raise SystemExit(f\"unterminated factory initializer: {field_name}\")\n\n\n"
    "def count_type_comparisons(source: str, raw_type: int) -> int:\n"
    "    normalized = compact(source)\n"
    "    pattern = re.compile(\n"
    "        rf\"(?<!\\d)type\\s*(?:==|!=)\\s*{raw_type}(?!\\d)|\\bcase\\s+{raw_type}\\s*:\")\n"
    "    return len(pattern.findall(normalized))\n\n\n"
    "def relevant_drop_contexts(source: str) -> str:\n")
replace_once(
    probe,
    "    bone_defaults = around_optional(set_defaults, \"type == 21\", radius=1800)\n",
    "    bone_defaults = around_optional(set_defaults, \"type == 21\", radius=1800)\n"
    "    bone_arrow_defaults = around_optional(set_defaults, \"type == 474\", radius=1800)\n")
replace_once(
    probe,
    "    wind_immunity = matching_lines(projectile_id_source, \"WindPhysicsImmunity\", limit=5)\n"
    "    if \"public const short Seed = 51;\" not in projectile_id_source:\n"
    "        raise SystemExit(\"ProjectileID.Seed != 51 in pinned source\")\n"
    "    if \"public const short BoneShard = 1124;\" not in projectile_id_source:\n"
    "        raise SystemExit(\"ProjectileID.BoneShard != 1124 in pinned source\")\n"
    "    for raw_type in (51, 1124):\n"
    "        if re.search(rf\"\\(short\\){raw_type}(?!\\d)\", wind_immunity):\n"
    "            raise SystemExit(f\"type {raw_type} unexpectedly overrides WindPhysicsImmunity\")\n",
    "    wind_immunity = compact(extract_factory_initializer(projectile_id_source, \"WindPhysicsImmunity\"))\n"
    "    if \"CreateCustomSet<bool?>(null\" not in wind_immunity:\n"
    "        raise SystemExit(\"unexpected WindPhysicsImmunity default semantics\")\n"
    "    if \"public const short Seed = 51;\" not in projectile_id_source:\n"
    "        raise SystemExit(\"ProjectileID.Seed != 51 in pinned source\")\n"
    "    if \"public const short BoneArrowFromMerchant = 474;\" not in projectile_id_source:\n"
    "        raise SystemExit(\"ProjectileID.BoneArrowFromMerchant != 474 in pinned source\")\n"
    "    if \"public const short BoneShard = 1124;\" not in projectile_id_source:\n"
    "        raise SystemExit(\"ProjectileID.BoneShard != 1124 in pinned source\")\n"
    "    for raw_type in (51, 474, 1124):\n"
    "        if re.search(rf\"(?<!\\d){raw_type}(?!\\d)\", wind_immunity):\n"
    "            raise SystemExit(f\"type {raw_type} unexpectedly overrides WindPhysicsImmunity\")\n\n"
    "    required_bone_arrow_defaults = (\n"
    "        \"arrow = true;\", \"width = 10;\", \"height = 10;\", \"aiStyle = 1;\",\n"
    "        \"friendly = true;\", \"ranged = true;\", \"timeLeft = 1200;\", \"penetrate = 2;\")\n"
    "    for token in required_bone_arrow_defaults:\n"
    "        if token not in bone_arrow_defaults:\n"
    "            raise SystemExit(f\"type 474 default missing: {token}\")\n"
    "    for source_name, source_text in (\n"
    "        (\"AI_001\", arrow_ai),\n"
    "        (\"AI\", projectile_ai),\n"
    "        (\"Update\", projectile_update),\n"
    "        (\"HandleMovement\", handle_movement),\n"
    "        (\"GetCollisionParams\", collision_params)):\n"
    "        if count_type_comparisons(source_text, 474) != 0:\n"
    "            raise SystemExit(f\"type 474 unexpectedly special in {source_name}\")\n"
    "    if count_type_comparisons(can_cut_tiles, 474) != 0:\n"
    "        raise SystemExit(\"type 474 unexpectedly special in CanCutTiles\")\n"
    "    if count_type_comparisons(projectile_kill, 474) != 1:\n"
    "        raise SystemExit(\"type 474 Kill branch count changed\")\n"
    "    bone_arrow_kill = all_type_comparison_contexts(projectile_kill, 474, radius=1300, limit=3)\n"
    "    for token in (\"SoundEngine.PlaySound\", \"Dust.NewDust\"):\n"
    "        if token not in bone_arrow_kill:\n"
    "            raise SystemExit(f\"type 474 visual Kill token missing: {token}\")\n"
    "    for token in (\"NewProjectile(\", \"NewItem(\", \"KillTile(\"):\n"
    "        if token in bone_arrow_kill:\n"
    "            raise SystemExit(f\"type 474 Kill gained authoritative side effect: {token}\")\n")
replace_once(
    probe,
    "    print(\"projectile_bone_defaults=\" + bone_defaults)\n"
    "    print(\"projectile_seed_defaults=\" + seed_defaults)\n",
    "    print(\"projectile_bone_defaults=\" + bone_defaults)\n"
    "    print(\"projectile_bone_arrow_from_merchant_defaults=\" + bone_arrow_defaults)\n"
    "    print(\"projectile_bone_arrow_from_merchant_kill=\" + bone_arrow_kill)\n"
    "    print(\"projectile_seed_defaults=\" + seed_defaults)\n")
