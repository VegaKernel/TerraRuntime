using TerraRuntime.Contracts.Gameplay;

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
}
