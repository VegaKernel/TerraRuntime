using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Tests;

public sealed class VanillaEaterOfWorldsItemDefinitionTests
{
    public static TheoryData<ItemTypeId, int, int> Drops => new()
    {
        { VanillaEaterOfWorldsItemIds.DemoniteOre, 12, 12 },
        { VanillaEaterOfWorldsItemIds.ShadowScale, 14, 18 },
        { VanillaEaterOfWorldsItemIds.EatersBone, 16, 30 },
        { VanillaEaterOfWorldsItemIds.EaterOfWorldsTrophy, 30, 30 },
        { VanillaEaterOfWorldsItemIds.EaterMask, 28, 20 },
        { VanillaEaterOfWorldsItemIds.EaterOfWorldsBossBag, 24, 24 },
        { VanillaEaterOfWorldsItemIds.EaterOfWorldsPetItem, 16, 30 },
        { VanillaEaterOfWorldsItemIds.EaterOfWorldsMasterTrophy, 14, 14 }
    };

    [Theory]
    [MemberData(nameof(Drops))]
    public void Eater_drop_defaults_are_source_backed(ItemTypeId type, int width, int height)
    {
        Assert.True(VanillaItemDefinitionCatalog.TryGetWorldDrop(type, out VanillaItemWorldDropDefinition drop));
        Assert.Equal(width, drop.Width);
        Assert.Equal(height, drop.Height);
        Assert.False(drop.NoGravity);
        Assert.Equal(VanillaItemPrefixFamily.None, drop.PrefixFamily);
    }
}
