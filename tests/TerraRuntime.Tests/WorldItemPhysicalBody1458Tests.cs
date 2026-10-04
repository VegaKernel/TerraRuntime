using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Tests;

public sealed class WorldItemPhysicalBody1458Tests
{
    // Independent original NPC imported loot + boss recovery probe, three live body sizes.
    // TerrariaServer.exe SHA256 4b87890ac53d40f61db5f928693a379acf4ccbd8ed3b47eb32fb096f145df034.
    // Captured WorldItem bodies are all 16x16; these facts check entity placement, not Item icon dimensions.
    [Theory]
    [InlineData(47, 1050, 1055, 1042, 1047, 12, -36)]
    [InlineData(56, 1050, 1055, 1042, 1047, 25, -29)]
    [InlineData(59, 1050, 1055, 1042, 1047, -30, -27)]
    [InlineData(28, 1050, 1055, 1042, 1047, 15, -17)]
    [InlineData(58, 1050, 1055, 1042, 1047, 22, -27)]
    [InlineData(188, 1040, 1051, 1032, 1043, -20, -18)]
    [InlineData(499, 1050, 1055, 1042, 1047, 15, -17)]
    [InlineData(1134, 1033, 1033, 1025, 1025, -7, -26)]
    [InlineData(3544, 1023, 1033, 1015, 1025, -7, -26)]
    [InlineData(47, 1050, 1060, 1042, 1052, 12, -36)]
    [InlineData(56, 1050, 1060, 1042, 1052, 25, -29)]
    [InlineData(59, 1050, 1060, 1042, 1052, -30, -27)]
    [InlineData(28, 1050, 1060, 1042, 1052, 15, -17)]
    [InlineData(58, 1050, 1060, 1042, 1052, 22, -27)]
    [InlineData(188, 1050, 1060, 1042, 1052, -20, -18)]
    [InlineData(499, 1050, 1060, 1042, 1052, 15, -17)]
    [InlineData(1134, 1050, 1060, 1042, 1052, -7, -26)]
    [InlineData(3544, 1050, 1060, 1042, 1052, -7, -26)]
    [InlineData(47, 1023, 1017, 1015, 1009, 12, -36)]
    [InlineData(56, 1023, 1017, 1015, 1009, 25, -29)]
    [InlineData(59, 1023, 1017, 1015, 1009, -30, -27)]
    [InlineData(28, 1023, 1017, 1015, 1009, 15, -17)]
    [InlineData(58, 1023, 1017, 1015, 1009, 22, -27)]
    [InlineData(188, 1023, 1017, 1015, 1009, -20, -18)]
    [InlineData(499, 1023, 1017, 1015, 1009, 15, -17)]
    [InlineData(1134, 1023, 1017, 1015, 1009, -7, -26)]
    [InlineData(3544, 1023, 1017, 1015, 1009, -7, -26)]
    public void Original_world_drop_positions_use_fixed_physical_body(
        int id, float centerX, float centerY, float x, float y, int vx, int vy)
    {
        var random = new LaunchRandom(vx, vy);
        Assert.True(VanillaNpcLootWorldItemMaterializer.Instance.TryMaterialize(
            new NpcLootWorldItemOrigin(centerX, centerY), new NpcLootDrop(new(id), 1),
            random, out WorldItemDropStateUpdate actual));
        Assert.Equal(x, actual.PositionX);
        Assert.Equal(y, actual.PositionY);
        Assert.Equal(vx * .1f, actual.VelocityX);
        Assert.Equal(vy * .1f, actual.VelocityY);
        Assert.Equal(2, random.LaunchDraws);
        Assert.Equal((short)id, actual.ItemNetId);
    }

    [Theory]
    [InlineData(23, 10, 12)]
    [InlineData(1309, 26, 28)]
    public void Physical_placement_does_not_replace_item_default_dimensions(int id, int width, int height)
    {
        Assert.True(VanillaDefinitionCatalog.TryGetRuntimeDefaults(new(id), out var defaults));
        Assert.Equal((width, height), (defaults.Width, defaults.Height));
        Assert.True(VanillaNpcLootWorldItemMaterializer.Instance.TryMaterialize(
            new(22, 29), new(new(id), 1), new LaunchRandom(0, -20), out var actual));
        Assert.Equal((14f, 21f), (actual.PositionX, actual.PositionY));
    }

    private sealed class LaunchRandom(int vx, int vy) : INpcLootRollSource
    {
        public int LaunchDraws { get; private set; }
        public int RollLuck(int denominator) => throw new InvalidOperationException();
        public int NextInt32(int min, int max)
        {
            if (min == 0 && max == 4) return 0;
            Assert.InRange(LaunchDraws, 0, 1);
            int result = LaunchDraws++ == 0 ? vx : vy;
            Assert.InRange(result, min, max - 1);
            return result;
        }
    }
}
