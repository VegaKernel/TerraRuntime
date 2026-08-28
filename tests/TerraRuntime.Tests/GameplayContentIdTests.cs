using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class GameplayContentIdTests
{
    [Fact]
    public void Npc_type_ids_reject_unassigned_raw_values()
    {
        Assert.False(NpcTypeId.TryCreate(0, out _));
        Assert.False(NpcTypeId.TryCreate(-1, out _));
        Assert.True(NpcTypeId.TryCreate(1, out NpcTypeId type));
        Assert.Equal(VanillaNpcIds.BlueSlime, type);
    }

    [Fact]
    public void Vanilla_item_ids_are_pinned_to_1458_count()
    {
        Assert.True(VanillaItemIds.TryCreate(0, out ItemTypeId none));
        Assert.True(none.IsNone);

        Assert.True(VanillaItemIds.TryCreate(VanillaItemIds.Count - 1, out ItemTypeId last));
        Assert.Equal(VanillaItemIds.Count - 1, last.Value);
        Assert.False(VanillaItemIds.TryCreate(VanillaItemIds.Count, out _));
    }

    [Fact]
    public void Initial_named_npc_catalog_keeps_type_and_ai_style_categories_separate()
    {
        Assert.Equal(1, VanillaNpcIds.BlueSlime.Value);
        Assert.Equal(2, VanillaNpcIds.DemonEye.Value);
        Assert.Equal(3, VanillaNpcIds.Zombie.Value);
        Assert.Equal(1, VanillaNpcAiStyles.Slime.Value);
        Assert.Equal(2, VanillaNpcAiStyles.DemonEye.Value);
        Assert.Equal(3, VanillaNpcAiStyles.Fighter.Value);
    }

    [Fact]
    public void Vanilla_tile_ids_are_pinned_to_1458_count()
    {
        Assert.Equal(VanillaTileIds.Count, VanillaTileCollisionCatalog.TileTypeCount);
        Assert.True(VanillaTileIds.TryCreate(0, out TileTypeId first));
        Assert.Equal(0, first.Value);
        Assert.True(VanillaTileIds.TryCreate(VanillaTileIds.Count - 1, out TileTypeId last));
        Assert.Equal(VanillaTileIds.Count - 1, last.Value);
        Assert.False(VanillaTileIds.TryCreate(VanillaTileIds.Count, out _));
    }

    [Fact]
    public void Vanilla_tile_behavior_families_are_named_and_centralized()
    {
        Assert.True(VanillaTileIds.IsPlatform(VanillaTileIds.Platforms));
        Assert.True(VanillaTileIds.IsPlatform(VanillaTileIds.TeamBlockWhitePlatform));
        Assert.False(VanillaTileIds.IsPlatform(VanillaTileIds.Containers));

        Assert.True(VanillaTileIds.IsChestAnchor(VanillaTileIds.Containers));
        Assert.True(VanillaTileIds.IsChestAnchor(VanillaTileIds.Containers2));
        Assert.True(VanillaTileIds.IsChestAnchor(VanillaTileIds.Dressers));
        Assert.False(VanillaTileIds.IsChestAnchor(VanillaTileIds.Signs));

        Assert.True(VanillaTileIds.CarriesSignText(VanillaTileIds.Signs));
        Assert.True(VanillaTileIds.CarriesSignText(VanillaTileIds.TatteredWoodSign));
        Assert.False(VanillaTileIds.CarriesSignText(VanillaTileIds.ItemFrame));
    }
}
