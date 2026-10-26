using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Gameplay.Tiles;
using Xunit;

namespace TerraRuntime.Tests.Tools1458;

internal static partial class AllLocalToolSourceChecks
{
    private static void CheckDefinitions(string platform, string repeat)
    {
        using var defaults = Read("ProjectileMetadata", platform, repeat);
        using var capsule = Read("Capsule", platform, repeat);
        using var directions = Read("Directions", platform, repeat);
        using var predicates = Read("Predicates", platform, repeat);
        using var wallFrames = Read("WallFrames", platform, repeat);
        using var sets = Read("TileSets", platform, repeat);
        var source = defaults.RootElement.EnumerateArray().ToDictionary(row => I(row, "input"));
        Assert.Equal(1136, source.Count);
        int collisions = 0;
        foreach (var row in capsule.RootElement.EnumerateArray())
        {
            if (row.GetProperty("kind").GetString() != "collision") continue;
            int type = I(row, "type");
            context = $"definition/{type}";
            Assert.True(VanillaToolProjectileDefinition1458.TryGet(new(type), out var definition));
            Assert.Equal(I(row, "width"), definition.Width);
            Assert.Equal(I(row, "height"), definition.Height);
            Assert.Equal(I(row, "collisionWidth"), definition.CollisionWidth);
            Assert.Equal(I(row, "collisionHeight"), definition.CollisionHeight);
            var facts = source[type];
            Assert.Equal(I(facts, "aiStyle"), definition.AiStyle.Value);
            Assert.Equal(B(facts, "tileCollide"), definition.TileCollide);
            Assert.Equal(B(facts, "ignoreWater"), definition.IgnoreWater);
            Assert.Equal(B(facts, "CanCutTiles"), definition.CanCutTiles);
            Assert.True(VanillaDefinitionCatalog.TryGet(new(type), out var hooked));
            Assert.Equal(definition, hooked);
            collisions++;
        }
        Assert.Equal(24, collisions);
        Assert.Equal(4544, directions.RootElement.GetArrayLength());
        foreach (var row in directions.RootElement.EnumerateArray())
        {
            int type = I(row, "id");
            context = $"direction/{type}/{F(row, "ai0")}";
            foreach (float vx in new[] { -1f, 0f, 1f })
            {
                bool found = VanillaProjectileSourceDirection1458.TryResolveAfterMotion(new(type),
                    new ProjectileAiState(F(row, "ai0"), 0f, 0f), vx, out int actual);
                Assert.Equal(type > 0 && I(source[type], "width") > 0 && B(row, "automatic"), found);
                if (found) Assert.Equal(vx < 0f ? -1 : 1, actual);
            }
        }
        Assert.Equal(754, predicates.RootElement.GetArrayLength());
        foreach (var row in predicates.RootElement.EnumerateArray())
        {
            int type = I(row, "type");
            context = $"target-role/{type}";
            Assert.Equal(B(row, "hammer"), VanillaToolDamage1458.IsAHammerTarget(new(type)));
            Assert.Equal(B(row, "pound"), VanillaToolDamage1458.IsTilePoundable(new(type)));
            Assert.True(VanillaToolTileCatalog1458.TryGet(new(type), out var facts));
            Assert.Equal(sets.RootElement.GetProperty("TileID.Sets.FramesOnKillWall")[type].GetBoolean(),
                facts.FramesOnKillWall);
            Assert.Equal(sets.RootElement.GetProperty("Main.tileCut")[type].GetBoolean(), facts.IsCuttable);
            Assert.Equal(sets.RootElement.GetProperty("TileID.Sets.BreakableWhenPlacing")[type].GetBoolean(), facts.BreakableWhenPlacing);
            Assert.Equal(sets.RootElement.GetProperty("Main.tileNoAttach")[type].GetBoolean(), facts.NoAttach);
        }
        byte[] modes = Convert.FromBase64String(wallFrames.RootElement.GetString()!);
        Assert.Equal(367, modes.Length);
        for (int type = 0; type < modes.Length; type++)
        {
            Assert.True(VanillaToolTileCatalog1458.TryGetWallFrameMode(new(type), out byte mode));
            Assert.Equal(modes[type], mode);
        }
        Assert.False(VanillaToolTileCatalog1458.TryGetWallFrameMode(new(367), out _));
    }

    private static void CheckDust(string platform, string repeat)
    {
        using var tiles = Read("Dust", platform, repeat);
        using var walls = Read("WallDust", platform, repeat);
        Assert.Equal(6032, tiles.RootElement.GetArrayLength());
        foreach (var row in tiles.RootElement.EnumerateArray())
        {
            context = $"dust/{I(row, "type")}/{I(row, "seed")}/{B(row, "clientOffscreen")}";
            var facts = new ToolDustTile1458(new(I(row, "type")), (short)I(row, "frameX"),
                (short)I(row, "frameY"));
            var random = Random(row.GetProperty("beforeRandom"));
            Assert.True(VanillaToolDust1458.TryGetAmount(facts, true, out int fail));
            Assert.True(VanillaToolDust1458.TryGetAmount(facts, false, out int success));
            Assert.Equal(I(row, "failAmount"), fail);
            Assert.Equal(I(row, "successAmount"), success);
            AssertRandom(row.GetProperty("afterAmountRandom"), random);
            Assert.True(VanillaToolDust1458.TryAdvanceSuppressedParticle(facts, random.Next));
            AssertRandom(row.GetProperty("afterRandom"), random);
            Assert.Equal(I(row, "afterNext"), random.Next());
        }
        Assert.Equal(2202, walls.RootElement.GetArrayLength());
        foreach (var row in walls.RootElement.EnumerateArray())
        {
            context = $"wall-dust/{I(row, "type")}/{I(row, "seed")}/{B(row, "clientOffscreen")}";
            var random = Random(row.GetProperty("before"));
            Assert.True(VanillaToolDust1458.TryAdvanceSuppressedWallParticle(new(I(row, "type")), random.Next));
            AssertRandom(row.GetProperty("after"), random);
            Assert.Equal(I(row, "afterNext"), random.Next());
        }
    }
}
