from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    file = Path(path)
    text = file.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{path}: expected one match, found {count}: {old[:180]!r}")
    file.write_text(text.replace(old, new, 1), encoding="utf-8")


# Named projectile IDs.
replace_once(
    "src/TerraRuntime.Contracts/Gameplay/VanillaContentIds.cs",
    "    public static readonly ProjectileTypeId ThrowingKnife = new(48);\n    public static readonly ProjectileTypeId PoisonedKnife = new(54);",
    "    public static readonly ProjectileTypeId ThrowingKnife = new(48);\n"
    "    public static readonly ProjectileTypeId Seed = new(51);\n"
    "    public static readonly ProjectileTypeId PoisonedKnife = new(54);")
replace_once(
    "src/TerraRuntime.Contracts/Gameplay/VanillaContentIds.cs",
    "    public static readonly ProjectileTypeId MeleeBone = new(1111);",
    "    public static readonly ProjectileTypeId MeleeBone = new(1111);\n"
    "    public static readonly ProjectileTypeId BoneShard = new(1124);")

# Source-backed world-state definitions.
catalog = "src/TerraRuntime.Contracts/Gameplay/VanillaProjectileDefinitionCatalog.cs"
replace_once(
    catalog,
    "    private static readonly VanillaProjectileDefinition PoisonedKnifeDefinition = new(\n",
    "    private static readonly VanillaProjectileDefinition SeedDefinition = new(\n"
    "        Width: 8,\n"
    "        Height: 8,\n"
    "        AiStyle: VanillaProjectileAiStyles.Arrow,\n"
    "        TileCollide: true,\n"
    "        IgnoreWater: false,\n"
    "        CanCutTiles: true,\n"
    "        CollisionWidth: 8,\n"
    "        CollisionHeight: 8);\n\n"
    "    private static readonly VanillaProjectileDefinition PoisonedKnifeDefinition = new(\n")
replace_once(
    catalog,
    "    private static readonly VanillaProjectileDefinition BoneDaggerDefinition = new(\n",
    "    private static readonly VanillaProjectileDefinition BoneShardDefinition = new(\n"
    "        Width: 6,\n"
    "        Height: 6,\n"
    "        AiStyle: VanillaProjectileAiStyles.Arrow,\n"
    "        TileCollide: true,\n"
    "        IgnoreWater: false,\n"
    "        CanCutTiles: true,\n"
    "        CollisionWidth: 6,\n"
    "        CollisionHeight: 6);\n\n"
    "    private static readonly VanillaProjectileDefinition BoneDaggerDefinition = new(\n")
replace_once(
    catalog,
    "        if (type == VanillaProjectileIds.PoisonedKnife)\n        {\n            definition = PoisonedKnifeDefinition;\n            return true;\n        }",
    "        if (type == VanillaProjectileIds.Seed)\n"
    "        {\n            definition = SeedDefinition;\n            return true;\n        }\n\n"
    "        if (type == VanillaProjectileIds.PoisonedKnife)\n"
    "        {\n            definition = PoisonedKnifeDefinition;\n            return true;\n        }")
replace_once(
    catalog,
    "        if (type == VanillaProjectileIds.MeleeBone)\n        {\n            definition = MeleeBoneDefinition;\n            return true;\n        }",
    "        if (type == VanillaProjectileIds.MeleeBone)\n"
    "        {\n            definition = MeleeBoneDefinition;\n            return true;\n        }\n\n"
    "        if (type == VanillaProjectileIds.BoneShard)\n"
    "        {\n            definition = BoneShardDefinition;\n            return true;\n        }")

# Generic aiStyle-1 production path. BoneShard's only exact AI_001 mutations are visual/local frame/rotation state.
stepper = "src/TerraRuntime/VanillaProjectileWorldStateStepper.cs"
replace_once(
    stepper,
    "/// Fire, Unholy, and Jester's Arrows, Bullet, and player-owned Green Laser (aiStyle 1), plus Shuriken, Throwing Knife,",
    "/// Fire, Unholy, and Jester's Arrows, Bullet, Seed, Bone Shard, and player-owned Green Laser (aiStyle 1), plus Shuriken, Throwing Knife,")
replace_once(
    stepper,
    "             current.Type == VanillaProjectileIds.Bullet ||\n             current.Type == VanillaProjectileIds.GreenLaser);",
    "             current.Type == VanillaProjectileIds.Bullet ||\n"
    "             current.Type == VanillaProjectileIds.Seed ||\n"
    "             current.Type == VanillaProjectileIds.BoneShard ||\n"
    "             current.Type == VanillaProjectileIds.GreenLaser);")
replace_once(
    stepper,
    "// source-backed Wooden/Fire/Unholy/Jester/Bullet/player-owned-GreenLaser path has ai[2] == 0; non-default",
    "// source-backed Wooden/Fire/Unholy/Jester/Bullet/Seed/BoneShard/player-owned-GreenLaser path has ai[2] == 0; non-default")

# Definition tests pin exact SetDefaults dimensions consumed by collision/world simulation.
definition_tests = "tests/TerraRuntime.Tests/VanillaProjectileDefinitionCatalogTests.cs"
replace_once(
    definition_tests,
    "    [Fact]\n    public void Terraria_1458_jesters_arrow_definition_matches_source()",
    "    [Theory]\n"
    "    [InlineData(51, 8, 8)]\n"
    "    [InlineData(1124, 6, 6)]\n"
    "    public void Terraria_1458_simple_ai_style_one_definitions_match_source(int type, int width, int height)\n"
    "    {\n"
    "        Assert.True(VanillaProjectileDefinitionCatalog.TryGet(\n"
    "            new ProjectileTypeId(type),\n"
    "            out VanillaProjectileDefinition definition));\n\n"
    "        Assert.Equal(width, definition.Width);\n"
    "        Assert.Equal(height, definition.Height);\n"
    "        Assert.Equal(VanillaProjectileAiStyles.Arrow, definition.AiStyle);\n"
    "        Assert.True(definition.TileCollide);\n"
    "        Assert.False(definition.IgnoreWater);\n"
    "        Assert.True(definition.CanCutTiles);\n"
    "        Assert.Equal(width, definition.CollisionWidth);\n"
    "        Assert.Equal(height, definition.CollisionHeight);\n"
    "        Assert.Equal(0f, definition.CollisionOffsetX);\n"
    "        Assert.Equal(0f, definition.CollisionOffsetY);\n"
    "    }\n\n"
    "    [Fact]\n"
    "    public void Terraria_1458_jesters_arrow_definition_matches_source()")

# One subupdate proves generic AI_001 trajectory/lifetime before executor-specific extraUpdates handling.
stepper_tests = "tests/TerraRuntime.Tests/VanillaProjectileWorldStateStepperTests.cs"
replace_once(
    stepper_tests,
    "    [Fact]\n    public void Wooden_arrow_free_flight_matches_ai001_before_gravity()",
    "    [Theory]\n"
    "    [InlineData(51, 3600)]\n"
    "    [InlineData(1124, 600)]\n"
    "    public void Simple_ai_style_one_family_uses_source_backed_world_trajectory(int type, int timeLeft)\n"
    "    {\n"
    "        var tiles = new WorldTileStore(new WorldDimensions(100, 100));\n"
    "        var stepper = new VanillaProjectileWorldStateStepper(tiles);\n"
    "        ProjectileSnapshot projectile = CreateSnapshot(\n"
    "            positionX: 100f,\n"
    "            positionY: 100f,\n"
    "            velocityX: 4f,\n"
    "            velocityY: 0f) with\n"
    "        {\n"
    "            Type = new ProjectileTypeId(type)\n"
    "        };\n"
    "        ProjectileSimulationStepContext context = CreateContext(projectile, timeLeft);\n\n"
    "        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));\n\n"
    "        Assert.Equal(new ProjectileTypeId(type), next.State.Type);\n"
    "        Assert.Equal(1f, next.State.Ai.Ai0, 5);\n"
    "        Assert.Equal(4f, next.State.VelocityX, 5);\n"
    "        Assert.Equal(0f, next.State.VelocityY, 5);\n"
    "        Assert.Equal(104f, next.State.PositionX, 5);\n"
    "        Assert.Equal(100f, next.State.PositionY, 5);\n"
    "        Assert.Equal(timeLeft - 1, next.TimeLeft);\n"
    "    }\n\n"
    "    [Fact]\n"
    "    public void Wooden_arrow_free_flight_matches_ai001_before_gravity()")

# Server integration proves Seed gets one subupdate and BoneShard gets two, but one final revision/commit.
integration = "tests/TerraRuntime.Tests/ServerRuntimeVanillaProjectileSimulationTests.cs"
replace_once(
    integration,
    "    [Fact]\n    public async Task Authoritative_tick_runs_source_backed_player_owned_wooden_arrow_free_flight_by_default()",
    "    [Theory]\n"
    "    [InlineData(51, 104f, 1f)]\n"
    "    [InlineData(1124, 108f, 2f)]\n"
    "    public async Task Authoritative_tick_runs_source_backed_simple_ai_style_one_family_by_default(\n"
    "        int type, float expectedPositionX, float expectedAi0)\n"
    "    {\n"
    "        var tiles = new WorldTileStore(new WorldDimensions(100, 100));\n"
    "        var projectiles = new RuntimeProjectileStore(capacity: 4);\n"
    "        var state = new ServerRuntimeState(worldTiles: tiles, projectiles: projectiles);\n"
    "        ProjectileStateUpdate projectile = CreateProjectile(type, spawner: 3);\n"
    "        var completion = new TaskCompletionSource<ProjectileSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);\n"
    "        state.Apply(new ProjectileSpawnRuntimeCommand(0, projectile, completion));\n"
    "        ProjectileSnapshot spawned = Assert.IsType<ProjectileSnapshot>(await completion.Task);\n\n"
    "        state.Tick();\n\n"
    "        Assert.Equal(new ProjectileStateTickSummary(1, 1, 1, 0), state.LastProjectileTick);\n"
    "        Assert.True(state.TryCaptureProjectileSnapshot(spawned.Handle, out ProjectileSnapshot updated));\n"
    "        Assert.Equal(new ProjectileTypeId(type), updated.Type);\n"
    "        Assert.Equal(new ProjectileRevision(2), updated.Revision);\n"
    "        Assert.Equal(expectedPositionX, updated.PositionX, 5);\n"
    "        Assert.Equal(100f, updated.PositionY, 5);\n"
    "        Assert.Equal(expectedAi0, updated.Ai.Ai0, 5);\n"
    "    }\n\n"
    "    [Fact]\n"
    "    public async Task Authoritative_tick_runs_source_backed_player_owned_wooden_arrow_free_flight_by_default()")
replace_once(
    integration,
    "    [Theory]\n    [InlineData(3)]\n    [InlineData(21)]\n    [InlineData(48)]\n    [InlineData(54)]\n    [InlineData(318)]\n    [InlineData(330)]\n    [InlineData(583)]\n    [InlineData(589)]\n    [InlineData(599)]\n    [InlineData(1012)]\n    [InlineData(1111)]\n    public async Task Server_owned_thrown_projectile_remains_authoritative_when_tile_cut_effect_is_not_yet_modeled(int type)",
    "    [Theory]\n"
    "    [InlineData(3)]\n    [InlineData(21)]\n    [InlineData(48)]\n    [InlineData(51)]\n    [InlineData(54)]\n"
    "    [InlineData(318)]\n    [InlineData(330)]\n    [InlineData(583)]\n    [InlineData(589)]\n    [InlineData(599)]\n"
    "    [InlineData(1012)]\n    [InlineData(1111)]\n    [InlineData(1124)]\n"
    "    public async Task Server_owned_source_backed_projectile_remains_authoritative_when_tile_cut_effect_is_not_yet_modeled(int type)")

# Permanent source contract: verify names, generic AI/CanCutTiles and the absence of table-driven wind overrides.
probe = "tools/ci/probe_projectile_tile_cut.py"
replace_once(
    probe,
    "    parser.add_argument(\"--tile-id\", required=True, type=Path)\n    args = parser.parse_args()",
    "    parser.add_argument(\"--tile-id\", required=True, type=Path)\n"
    "    parser.add_argument(\"--projectile-id\", required=True, type=Path)\n"
    "    args = parser.parse_args()")
replace_once(
    probe,
    "    tile_id_source = args.tile_id.read_text(encoding=\"utf-8\")",
    "    tile_id_source = args.tile_id.read_text(encoding=\"utf-8\")\n"
    "    projectile_id_source = args.projectile_id.read_text(encoding=\"utf-8\")")
replace_once(
    probe,
    "    bone_defaults = around_optional(set_defaults, \"type == 21\", radius=1800)\n    arrow_ai = extract_method(projectile_source, \"AI_001\")",
    "    bone_defaults = around_optional(set_defaults, \"type == 21\", radius=1800)\n"
    "    seed_defaults = around_optional(set_defaults, \"type == 51\", radius=1400)\n"
    "    bone_shard_defaults = around_optional(set_defaults, \"type == 1124\", radius=1800)\n"
    "    arrow_ai = extract_method(projectile_source, \"AI_001\")")
replace_once(
    probe,
    "    print(\"projectile_bone_defaults=\" + bone_defaults)\n    print(\"projectile_ai_type21_contexts=\"",
    "    wind_immunity = matching_lines(projectile_id_source, \"WindPhysicsImmunity\", limit=5)\n"
    "    if \"public const short Seed = 51;\" not in projectile_id_source:\n"
    "        raise SystemExit(\"ProjectileID.Seed != 51 in pinned source\")\n"
    "    if \"public const short BoneShard = 1124;\" not in projectile_id_source:\n"
    "        raise SystemExit(\"ProjectileID.BoneShard != 1124 in pinned source\")\n"
    "    for raw_type in (51, 1124):\n"
    "        if re.search(rf\"\\(short\\){raw_type}(?!\\d)\", wind_immunity):\n"
    "            raise SystemExit(f\"type {raw_type} unexpectedly overrides WindPhysicsImmunity\")\n\n"
    "    print(\"projectile_bone_defaults=\" + bone_defaults)\n"
    "    print(\"projectile_seed_defaults=\" + seed_defaults)\n"
    "    print(\"projectile_bone_shard_defaults=\" + bone_shard_defaults)\n"
    "    print(\"projectile_seed_ai001_contexts=\" + all_type_comparison_contexts(arrow_ai, 51, radius=1800, limit=20))\n"
    "    print(\"projectile_bone_shard_ai001_contexts=\" + all_type_comparison_contexts(arrow_ai, 1124, radius=2600, limit=20))\n"
    "    print(\"projectile_seed_kill_contexts=\" + all_type_comparison_contexts(projectile_kill, 51, radius=1800, limit=20))\n"
    "    print(\"projectile_bone_shard_kill_contexts=\" + all_type_comparison_contexts(projectile_kill, 1124, radius=1800, limit=20))\n"
    "    print(\"projectile_seed_bone_shard_wind_immunity=\" + wind_immunity)\n"
    "    print(\"projectile_ai_type21_contexts=\"")

# Source workflow now supplies ProjectileID so table-backed wind behavior stays pinned forever.
workflow = ".github/workflows/terraria-source-contract.yml"
replace_once(
    workflow,
    "          .tools/ilspycmd --disable-updatecheck -r \"$assembly_dir\" -t Terraria.ID.TileID \"$assembly\" \\\n            > artifacts/source-contract/tile-id.cs\n\n          python3 tools/ci/probe_projectile_tile_cut.py",
    "          .tools/ilspycmd --disable-updatecheck -r \"$assembly_dir\" -t Terraria.ID.TileID \"$assembly\" \\\n"
    "            > artifacts/source-contract/tile-id.cs\n"
    "          .tools/ilspycmd --disable-updatecheck -r \"$assembly_dir\" -t Terraria.ID.ProjectileID \"$assembly\" \\\n"
    "            > artifacts/source-contract/projectile-id.cs\n\n"
    "          python3 tools/ci/probe_projectile_tile_cut.py")
replace_once(
    workflow,
    "            --main artifacts/source-contract/main.cs \\\n            --tile-id artifacts/source-contract/tile-id.cs",
    "            --main artifacts/source-contract/main.cs \\\n"
    "            --tile-id artifacts/source-contract/tile-id.cs \\\n"
    "            --projectile-id artifacts/source-contract/projectile-id.cs")

# Explain the newly live extra-update case in the pinned update-facts contract.
update_facts = "src/TerraRuntime.Contracts/Gameplay/VanillaProjectileUpdateFacts.cs"
replace_once(
    update_facts,
    "/// The source-backed simple aiStyle-2 family (types 318/330/583/589/1012/1111) uses the default zero extra updates.",
    "/// The source-backed simple aiStyle-2 family (types 318/330/583/589/1012/1111) uses the default zero extra updates.\n"
    "/// Bone Shard (type 1124) is a generic aiStyle-1 trajectory with extraUpdates=1, so it executes two subupdates per world tick.")
