from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    file = Path(path)
    text = file.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{path}: expected one match, found {count}: {old[:140]!r}")
    file.write_text(text.replace(old, new, 1), encoding="utf-8")


replace_once(
    "src/TerraRuntime.Contracts/Gameplay/VanillaContentIds.cs",
    "    public static readonly ProjectileTypeId GreenLaser = new(20);\n    public static readonly ProjectileTypeId ThrowingKnife = new(48);",
    "    public static readonly ProjectileTypeId GreenLaser = new(20);\n    public static readonly ProjectileTypeId Bone = new(21);\n    public static readonly ProjectileTypeId ThrowingKnife = new(48);")

replace_once(
    "src/TerraRuntime.Contracts/Gameplay/VanillaProjectileDefinitionCatalog.cs",
    "    private static readonly VanillaProjectileDefinition ShurikenDefinition = new(\n        Width: 22,",
    "    private static readonly VanillaProjectileDefinition BoneDefinition = new(\n        Width: 16,\n        Height: 16,\n        AiStyle: VanillaProjectileAiStyles.Thrown,\n        TileCollide: true,\n        IgnoreWater: false,\n        CanCutTiles: true,\n        CollisionWidth: 16,\n        CollisionHeight: 16);\n\n    private static readonly VanillaProjectileDefinition ShurikenDefinition = new(\n        Width: 22,")

replace_once(
    "src/TerraRuntime.Contracts/Gameplay/VanillaProjectileDefinitionCatalog.cs",
    "        if (type == VanillaProjectileIds.Shuriken)\n        {\n            definition = ShurikenDefinition;\n            return true;\n        }",
    "        if (type == VanillaProjectileIds.Bone)\n        {\n            definition = BoneDefinition;\n            return true;\n        }\n\n        if (type == VanillaProjectileIds.Shuriken)\n        {\n            definition = ShurikenDefinition;\n            return true;\n        }")

replace_once(
    "src/TerraRuntime/VanillaProjectileWorldStateStepper.cs",
    "/// Poisoned Knife, and Bone Dagger (aiStyle 2).",
    "/// Poisoned Knife, Bone, and Bone Dagger (aiStyle 2).")

replace_once(
    "tests/TerraRuntime.Tests/VanillaProjectileDefinitionCatalogTests.cs",
    "    [Theory]\n    [InlineData(48, 12, 12, 12, 12, 0f, 0f)]",
    "    [Theory]\n    [InlineData(21, 16, 16, 16, 16, 0f, 0f)]\n    [InlineData(48, 12, 12, 12, 12, 0f, 0f)]")

replace_once(
    "tests/TerraRuntime.Tests/VanillaProjectileWorldStateStepperTests.cs",
    "    [Theory]\n    [InlineData(48)]\n    [InlineData(54)]\n    [InlineData(599)]\n    public void Player_owned_thrown_family_uses_the_source_backed_ai_style_two_path",
    "    [Theory]\n    [InlineData(21)]\n    [InlineData(48)]\n    [InlineData(54)]\n    [InlineData(599)]\n    public void Player_owned_thrown_family_uses_the_source_backed_ai_style_two_path")

integration = Path("tests/TerraRuntime.Tests/ServerRuntimeVanillaProjectileSimulationTests.cs")
text = integration.read_text(encoding="utf-8")
marker = "    [InlineData(3)]\n    [InlineData(48)]\n    [InlineData(54)]\n    [InlineData(599)]"
count = text.count(marker)
if count != 3:
    raise SystemExit(f"{integration}: expected three thrown-family theory blocks, found {count}")
integration.write_text(
    text.replace(
        marker,
        "    [InlineData(3)]\n    [InlineData(21)]\n    [InlineData(48)]\n    [InlineData(54)]\n    [InlineData(599)]"),
    encoding="utf-8")

replace_once(
    "tools/ci/probe_projectile_tile_cut.py",
    '    green_laser_defaults = around_optional(set_defaults, "type == 20", radius=1800)\n    arrow_ai = extract_method(projectile_source, "AI_001")',
    '    green_laser_defaults = around_optional(set_defaults, "type == 20", radius=1800)\n    bone_defaults = around_optional(set_defaults, "type == 21", radius=1800)\n    arrow_ai = extract_method(projectile_source, "AI_001")')

replace_once(
    "tools/ci/probe_projectile_tile_cut.py",
    '    print("projectile_green_laser_defaults=" + green_laser_defaults)\n    print("projectile_ai001_ai0_increment="',
    '    print("projectile_green_laser_defaults=" + green_laser_defaults)\n'
    '    print("projectile_bone_defaults=" + bone_defaults)\n'
    '    print("projectile_ai_type21_contexts=" + all_type_comparison_contexts(extract_method(projectile_source, "AI"), 21, radius=2600, limit=20))\n'
    '    print("projectile_collision_params_type21_contexts=" + all_type_comparison_contexts(extract_method(projectile_source, "GetCollisionParams"), 21, radius=1800, limit=20))\n'
    '    print("projectile_handle_movement_type21_contexts=" + all_type_comparison_contexts(handle_movement, 21, radius=2200, limit=20))\n'
    '    print("projectile_kill_type21_contexts=" + all_type_comparison_contexts(projectile_kill, 21, radius=2600, limit=20))\n'
    '    print("projectile_ai001_ai0_increment="')
