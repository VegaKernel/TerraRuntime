from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    file = Path(path)
    text = file.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{path}: expected one match, found {count}: {old[:160]!r}")
    file.write_text(text.replace(old, new, 1), encoding="utf-8")


replace_once(
    "src/TerraRuntime.Contracts/Gameplay/VanillaContentIds.cs",
    "    public static readonly ProjectileTypeId ThrowingKnife = new(48);\n    public static readonly ProjectileTypeId PoisonedKnife = new(54);\n    public static readonly ProjectileTypeId BoneDagger = new(599);",
    "    public static readonly ProjectileTypeId ThrowingKnife = new(48);\n"
    "    public static readonly ProjectileTypeId PoisonedKnife = new(54);\n"
    "    public static readonly ProjectileTypeId RottenEgg = new(318);\n"
    "    public static readonly ProjectileTypeId StarAnise = new(330);\n"
    "    public static readonly ProjectileTypeId NurseSyringeHurt = new(583);\n"
    "    public static readonly ProjectileTypeId SantaBombs = new(589);\n"
    "    public static readonly ProjectileTypeId BoneDagger = new(599);\n"
    "    public static readonly ProjectileTypeId Waffle = new(1012);\n"
    "    public static readonly ProjectileTypeId MeleeBone = new(1111);")

catalog = "src/TerraRuntime.Contracts/Gameplay/VanillaProjectileDefinitionCatalog.cs"
replace_once(
    catalog,
    "    private static readonly VanillaProjectileDefinition BoneDaggerDefinition = new(\n        Width: 22,",
    "    private static readonly VanillaProjectileDefinition RottenEggDefinition = new(\n"
    "        Width: 12,\n        Height: 14,\n        AiStyle: VanillaProjectileAiStyles.Thrown,\n"
    "        TileCollide: true,\n        IgnoreWater: false,\n        CanCutTiles: true,\n"
    "        CollisionWidth: 12,\n        CollisionHeight: 14);\n\n"
    "    private static readonly VanillaProjectileDefinition StarAniseDefinition = new(\n"
    "        Width: 22,\n        Height: 22,\n        AiStyle: VanillaProjectileAiStyles.Thrown,\n"
    "        TileCollide: true,\n        IgnoreWater: false,\n        CanCutTiles: true,\n"
    "        CollisionWidth: 22,\n        CollisionHeight: 22);\n\n"
    "    private static readonly VanillaProjectileDefinition NurseSyringeHurtDefinition = new(\n"
    "        Width: 10,\n        Height: 10,\n        AiStyle: VanillaProjectileAiStyles.Thrown,\n"
    "        TileCollide: true,\n        IgnoreWater: false,\n        CanCutTiles: true,\n"
    "        CollisionWidth: 10,\n        CollisionHeight: 10);\n\n"
    "    private static readonly VanillaProjectileDefinition SantaBombsDefinition = new(\n"
    "        Width: 10,\n        Height: 10,\n        AiStyle: VanillaProjectileAiStyles.Thrown,\n"
    "        TileCollide: true,\n        IgnoreWater: false,\n        CanCutTiles: true,\n"
    "        CollisionWidth: 10,\n        CollisionHeight: 10);\n\n"
    "    private static readonly VanillaProjectileDefinition WaffleDefinition = new(\n"
    "        Width: 18,\n        Height: 18,\n        AiStyle: VanillaProjectileAiStyles.Thrown,\n"
    "        TileCollide: true,\n        IgnoreWater: false,\n        CanCutTiles: true,\n"
    "        CollisionWidth: 18,\n        CollisionHeight: 18);\n\n"
    "    private static readonly VanillaProjectileDefinition MeleeBoneDefinition = new(\n"
    "        Width: 16,\n        Height: 16,\n        AiStyle: VanillaProjectileAiStyles.Thrown,\n"
    "        TileCollide: true,\n        IgnoreWater: false,\n        CanCutTiles: true,\n"
    "        CollisionWidth: 16,\n        CollisionHeight: 16);\n\n"
    "    private static readonly VanillaProjectileDefinition BoneDaggerDefinition = new(\n        Width: 22,")

replace_once(
    catalog,
    "        if (type == VanillaProjectileIds.BoneDagger)\n        {\n            definition = BoneDaggerDefinition;\n            return true;\n        }",
    "        if (type == VanillaProjectileIds.RottenEgg)\n        {\n            definition = RottenEggDefinition;\n            return true;\n        }\n\n"
    "        if (type == VanillaProjectileIds.StarAnise)\n        {\n            definition = StarAniseDefinition;\n            return true;\n        }\n\n"
    "        if (type == VanillaProjectileIds.NurseSyringeHurt)\n        {\n            definition = NurseSyringeHurtDefinition;\n            return true;\n        }\n\n"
    "        if (type == VanillaProjectileIds.SantaBombs)\n        {\n            definition = SantaBombsDefinition;\n            return true;\n        }\n\n"
    "        if (type == VanillaProjectileIds.BoneDagger)\n        {\n            definition = BoneDaggerDefinition;\n            return true;\n        }\n\n"
    "        if (type == VanillaProjectileIds.Waffle)\n        {\n            definition = WaffleDefinition;\n            return true;\n        }\n\n"
    "        if (type == VanillaProjectileIds.MeleeBone)\n        {\n            definition = MeleeBoneDefinition;\n            return true;\n        }")

replace_once(
    "src/TerraRuntime/VanillaProjectileWorldStateStepper.cs",
    "/// Poisoned Knife, Bone, and Bone Dagger (aiStyle 2).",
    "/// Poisoned Knife, Bone, Rotten Egg, Star Anise, Nurse Syringe, Santa Bombs, Bone Dagger, Waffle, and Melee Bone (aiStyle 2).")

replace_once(
    "tests/TerraRuntime.Tests/VanillaProjectileDefinitionCatalogTests.cs",
    "    [InlineData(54, 12, 12, 12, 12, 0f, 0f)]\n    [InlineData(599, 22, 22, 10, 10, 6f, 6f)]",
    "    [InlineData(54, 12, 12, 12, 12, 0f, 0f)]\n"
    "    [InlineData(318, 12, 14, 12, 14, 0f, 0f)]\n"
    "    [InlineData(330, 22, 22, 22, 22, 0f, 0f)]\n"
    "    [InlineData(583, 10, 10, 10, 10, 0f, 0f)]\n"
    "    [InlineData(589, 10, 10, 10, 10, 0f, 0f)]\n"
    "    [InlineData(599, 22, 22, 10, 10, 6f, 6f)]\n"
    "    [InlineData(1012, 18, 18, 18, 18, 0f, 0f)]\n"
    "    [InlineData(1111, 16, 16, 16, 16, 0f, 0f)]")

replace_once(
    "tests/TerraRuntime.Tests/VanillaProjectileWorldStateStepperTests.cs",
    "    [InlineData(54)]\n    [InlineData(599)]\n    public void Player_owned_thrown_family_uses_the_source_backed_ai_style_two_path",
    "    [InlineData(54)]\n"
    "    [InlineData(318)]\n    [InlineData(330)]\n    [InlineData(583)]\n    [InlineData(589)]\n"
    "    [InlineData(599)]\n    [InlineData(1012)]\n    [InlineData(1111)]\n"
    "    public void Player_owned_thrown_family_uses_the_source_backed_ai_style_two_path")

integration = Path("tests/TerraRuntime.Tests/ServerRuntimeVanillaProjectileSimulationTests.cs")
text = integration.read_text(encoding="utf-8")
marker = "    [InlineData(3)]\n    [InlineData(21)]\n    [InlineData(48)]\n    [InlineData(54)]\n    [InlineData(599)]"
if text.count(marker) != 3:
    raise SystemExit(f"{integration}: expected three thrown-family blocks, found {text.count(marker)}")
replacement = (
    "    [InlineData(3)]\n    [InlineData(21)]\n    [InlineData(48)]\n    [InlineData(54)]\n"
    "    [InlineData(318)]\n    [InlineData(330)]\n    [InlineData(583)]\n    [InlineData(589)]\n"
    "    [InlineData(599)]\n    [InlineData(1012)]\n    [InlineData(1111)]")
integration.write_text(text.replace(marker, replacement), encoding="utf-8")

probe = "tools/ci/probe_projectile_tile_cut.py"
replace_once(
    probe,
    "    projectile_update = extract_method(projectile_source, \"Update\")\n    handle_movement = extract_method(projectile_source, \"HandleMovement\")\n    projectile_kill = extract_method(projectile_source, \"Kill\")",
    "    projectile_update = extract_method(projectile_source, \"Update\")\n"
    "    projectile_ai = extract_method(projectile_source, \"AI\")\n"
    "    collision_params = extract_method(projectile_source, \"GetCollisionParams\")\n"
    "    handle_movement = extract_method(projectile_source, \"HandleMovement\")\n"
    "    projectile_kill = extract_method(projectile_source, \"Kill\")")

replace_once(
    probe,
    "    print(\"projectile_kill_type21_contexts=\" + all_type_comparison_contexts(projectile_kill, 21, radius=2600, limit=20))\n    print(\"projectile_ai001_ai0_increment=\"",
    "    print(\"projectile_kill_type21_contexts=\" + all_type_comparison_contexts(projectile_kill, 21, radius=2600, limit=20))\n"
    "    for simple_ai2_type in (318, 330, 583, 589, 1012, 1111):\n"
    "        print(f\"projectile_ai2_type{simple_ai2_type}_defaults=\" + around_optional(set_defaults, f\"type == {simple_ai2_type}\", radius=1600))\n"
    "        print(f\"projectile_ai2_type{simple_ai2_type}_ai_contexts=\" + all_type_comparison_contexts(projectile_ai, simple_ai2_type, radius=1800, limit=20))\n"
    "        print(f\"projectile_ai2_type{simple_ai2_type}_collision_contexts=\" + all_type_comparison_contexts(collision_params, simple_ai2_type, radius=1600, limit=20))\n"
    "        print(f\"projectile_ai2_type{simple_ai2_type}_movement_contexts=\" + all_type_comparison_contexts(handle_movement, simple_ai2_type, radius=1600, limit=20))\n"
    "        print(f\"projectile_ai2_type{simple_ai2_type}_kill_contexts=\" + all_type_comparison_contexts(projectile_kill, simple_ai2_type, radius=2200, limit=20))\n"
    "    print(\"projectile_moon_globe_type996_kill_contexts=\" + all_type_comparison_contexts(projectile_kill, 996, radius=2600, limit=20))\n"
    "    print(\"projectile_ai001_ai0_increment=\"")
