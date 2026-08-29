#!/usr/bin/env python3
from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    target = Path(path)
    text = target.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{path}: expected exactly one marker, got {count}: {old[:120]!r}")
    target.write_text(text.replace(old, new, 1), encoding="utf-8")


replace_once(
    "src/TerraRuntime.Contracts/Gameplay/VanillaContentIds.cs",
    "    public static readonly ProjectileTypeId Waffle = new(1012);\n    public static readonly ProjectileTypeId MeleeBone = new(1111);",
    "    public static readonly ProjectileTypeId Waffle = new(1012);\n    public static readonly ProjectileTypeId SoundGun = new(1099);\n    public static readonly ProjectileTypeId MeleeBone = new(1111);",
)

replace_once(
    "src/TerraRuntime.Contracts/Gameplay/VanillaProjectileDefinitionCatalog.cs",
    "    private static readonly VanillaProjectileDefinition MeleeBoneDefinition = new(\n",
    "    private static readonly VanillaProjectileDefinition SoundGunDefinition = new(\n"
    "        Width: 66,\n"
    "        Height: 66,\n"
    "        AiStyle: VanillaProjectileAiStyles.Arrow,\n"
    "        TileCollide: false,\n"
    "        IgnoreWater: false,\n"
    "        CanCutTiles: true,\n"
    "        CollisionWidth: 66,\n"
    "        CollisionHeight: 66);\n\n"
    "    private static readonly VanillaProjectileDefinition MeleeBoneDefinition = new(\n",
)
replace_once(
    "src/TerraRuntime.Contracts/Gameplay/VanillaProjectileDefinitionCatalog.cs",
    "        if (type == VanillaProjectileIds.MeleeBone)\n        {\n            definition = MeleeBoneDefinition;\n            return true;\n        }",
    "        if (type == VanillaProjectileIds.SoundGun)\n"
    "        {\n"
    "            definition = SoundGunDefinition;\n"
    "            return true;\n"
    "        }\n\n"
    "        if (type == VanillaProjectileIds.MeleeBone)\n"
    "        {\n"
    "            definition = MeleeBoneDefinition;\n"
    "            return true;\n"
    "        }",
)

replace_once(
    "src/TerraRuntime/VanillaProjectileWorldStateStepper.cs",
    "/// Fire, Unholy, and Jester's Arrows, Bullet, Seed, Bone Arrow, Bone Shard, and player-owned Green Laser (aiStyle 1), plus Shuriken, Throwing Knife,\n",
    "/// Fire, Unholy, and Jester's Arrows, Bullet, Seed, Bone Arrow, Sound Gun, Bone Shard, and player-owned Green Laser (aiStyle 1), plus Shuriken, Throwing Knife,\n",
)
replace_once(
    "src/TerraRuntime/VanillaProjectileWorldStateStepper.cs",
    "             current.Type == VanillaProjectileIds.BoneArrowFromMerchant ||\n             current.Type == VanillaProjectileIds.BoneShard ||",
    "             current.Type == VanillaProjectileIds.BoneArrowFromMerchant ||\n             current.Type == VanillaProjectileIds.SoundGun ||\n             current.Type == VanillaProjectileIds.BoneShard ||",
)
replace_once(
    "src/TerraRuntime/VanillaProjectileWorldStateStepper.cs",
    "// source-backed Wooden/Fire/Unholy/Jester/Bullet/Seed/BoneArrow/BoneShard/player-owned-GreenLaser path has ai[2] == 0; non-default\n",
    "// source-backed Wooden/Fire/Unholy/Jester/Bullet/Seed/BoneArrow/SoundGun/BoneShard/player-owned-GreenLaser path has ai[2] == 0; non-default\n",
)

replace_once(
    "tests/TerraRuntime.Tests/VanillaProjectileDefinitionCatalogTests.cs",
    "    [Fact]\n    public void Terraria_1458_jesters_arrow_definition_matches_source()\n",
    "    [Fact]\n"
    "    public void Terraria_1458_sound_gun_definition_matches_source()\n"
    "    {\n"
    "        Assert.True(VanillaProjectileDefinitionCatalog.TryGet(\n"
    "            VanillaProjectileIds.SoundGun,\n"
    "            out VanillaProjectileDefinition definition));\n\n"
    "        Assert.Equal(66, definition.Width);\n"
    "        Assert.Equal(66, definition.Height);\n"
    "        Assert.Equal(VanillaProjectileAiStyles.Arrow, definition.AiStyle);\n"
    "        Assert.False(definition.TileCollide);\n"
    "        Assert.False(definition.IgnoreWater);\n"
    "        Assert.True(definition.CanCutTiles);\n"
    "        Assert.Equal(66, definition.CollisionWidth);\n"
    "        Assert.Equal(66, definition.CollisionHeight);\n"
    "        Assert.Equal(0f, definition.CollisionOffsetX);\n"
    "        Assert.Equal(0f, definition.CollisionOffsetY);\n"
    "    }\n\n"
    "    [Fact]\n"
    "    public void Terraria_1458_jesters_arrow_definition_matches_source()\n",
)

replace_once(
    "tests/TerraRuntime.Tests/VanillaProjectileWorldStateStepperTests.cs",
    "    [InlineData(474, 1200)]\n    [InlineData(1124, 600)]",
    "    [InlineData(474, 1200)]\n    [InlineData(1099, 600)]\n    [InlineData(1124, 600)]",
)
replace_once(
    "tests/TerraRuntime.Tests/VanillaProjectileWorldStateStepperTests.cs",
    "    [Fact]\n    public void Wooden_arrow_free_flight_matches_ai001_before_gravity()\n",
    "    [Fact]\n"
    "    public void Sound_gun_water_contact_uses_generic_half_speed_liquid_motion()\n"
    "    {\n"
    "        var tiles = new WorldTileStore(new WorldDimensions(100, 100));\n"
    "        tiles.Set(8, 8, LiquidTile(WorldLiquidKind.Water));\n"
    "        var stepper = new VanillaProjectileWorldStateStepper(tiles);\n"
    "        ProjectileSnapshot projectile = CreateSnapshot(\n"
    "            positionX: 100f,\n"
    "            positionY: 100f,\n"
    "            velocityX: 4f,\n"
    "            velocityY: 2f) with\n"
    "        {\n"
    "            Type = VanillaProjectileIds.SoundGun\n"
    "        };\n"
    "        ProjectileSimulationStepContext context = CreateContext(projectile, timeLeft: 600);\n\n"
    "        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));\n\n"
    "        Assert.Equal(VanillaProjectileIds.SoundGun, next.State.Type);\n"
    "        Assert.Equal(102f, next.State.PositionX, 5);\n"
    "        Assert.Equal(101f, next.State.PositionY, 5);\n"
    "        Assert.Equal(4f, next.State.VelocityX, 5);\n"
    "        Assert.Equal(2f, next.State.VelocityY, 5);\n"
    "        Assert.Equal(599, next.TimeLeft);\n"
    "        Assert.True(next.Liquid.GetValueOrDefault().Wet);\n"
    "    }\n\n"
    "    [Fact]\n"
    "    public void Sound_gun_ignores_solid_tile_collision_when_tile_collide_is_false()\n"
    "    {\n"
    "        var tiles = new WorldTileStore(new WorldDimensions(100, 100));\n"
    "        tiles.Set(7, 10, SolidTile(1));\n"
    "        var stepper = new VanillaProjectileWorldStateStepper(tiles);\n"
    "        ProjectileSnapshot projectile = CreateSnapshot(\n"
    "            positionX: 100f,\n"
    "            positionY: 160f,\n"
    "            velocityX: 20f,\n"
    "            velocityY: 0f) with\n"
    "        {\n"
    "            Type = VanillaProjectileIds.SoundGun\n"
    "        };\n"
    "        ProjectileSimulationStepContext context = CreateContext(projectile, timeLeft: 600);\n\n"
    "        Assert.True(stepper.TryStepState(in context, out ProjectileSimulationStepResult next));\n\n"
    "        Assert.Equal(120f, next.State.PositionX, 5);\n"
    "        Assert.Equal(160f, next.State.PositionY, 5);\n"
    "        Assert.Equal(20f, next.State.VelocityX, 5);\n"
    "        Assert.Equal(0f, next.State.VelocityY, 5);\n"
    "        Assert.Equal(599, next.TimeLeft);\n"
    "    }\n\n"
    "    [Fact]\n"
    "    public void Wooden_arrow_free_flight_matches_ai001_before_gravity()\n",
)

replace_once(
    "tests/TerraRuntime.Tests/ServerRuntimeVanillaProjectileSimulationTests.cs",
    "    [InlineData(474, 104f, 1f, 1199)]\n    [InlineData(1124, 108f, 2f, 598)]",
    "    [InlineData(474, 104f, 1f, 1199)]\n    [InlineData(1099, 104f, 1f, 599)]\n    [InlineData(1124, 108f, 2f, 598)]",
)
replace_once(
    "tests/TerraRuntime.Tests/ServerRuntimeVanillaProjectileSimulationTests.cs",
    "    [InlineData(474)]\n    [InlineData(583)]",
    "    [InlineData(474)]\n    [InlineData(1099)]\n    [InlineData(583)]",
)
replace_once(
    "tests/TerraRuntime.Tests/ServerRuntimeVanillaProjectileSimulationTests.cs",
    "    [InlineData(4)]\n    [InlineData(474)]\n    public async Task Server_owned_arrow_simulates_when_tile_cut_effect_is_empty(int type)",
    "    [InlineData(4)]\n    [InlineData(51)]\n    [InlineData(474)]\n    [InlineData(1099)]\n    public async Task Server_owned_single_subupdate_ai_style_one_simulates_when_tile_cut_effect_is_empty(int type)",
)

print("SoundGun 1099 runtime patch applied")
