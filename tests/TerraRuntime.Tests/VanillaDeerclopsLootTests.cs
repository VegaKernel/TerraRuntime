using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Tests;

public sealed class VanillaDeerclopsLootTests
{
    [Fact]
    public void Classic_source_order_delivers_five_conditionals_one_guaranteed_weapon_and_trophy()
    {
        var rolls = new AlwaysSuccessRolls();
        var sink = new RecordingSink();
        var context = new VanillaDeerclopsLootContext(IsExpertMode: false, IsMasterMode: false);

        Assert.True(VanillaDeerclopsLootEvaluator.TryExecute(
            in context,
            new NpcLootWorldItemOrigin(100f, 200f),
            [],
            rolls,
            sink,
            out DeerclopsLootExecutionResult result));

        ItemTypeId[] expected =
        [
            VanillaDeerclopsItemIds.DeerclopsMask,
            VanillaDeerclopsItemIds.ChesterPetItem,
            VanillaDeerclopsItemIds.Eyebrella,
            VanillaDeerclopsItemIds.DontStarveShaderItem,
            VanillaDeerclopsItemIds.DizzyHat,
            VanillaDeerclopsItemIds.PewMaticHorn,
            VanillaDeerclopsItemIds.DeerclopsTrophy
        ];

        Assert.Equal(expected, sink.WorldDrops.Select(static drop => drop.ItemType));
        Assert.Equal(expected.Length, result.WorldItemCount);
        Assert.Equal(0, result.InstancedItemCount);
        Assert.Equal(0, result.MasterPetDropCount);
    }

    [Fact]
    public void Master_delivers_bag_relic_per_player_pet_and_trophy_without_classic_drops()
    {
        var rolls = new AlwaysSuccessRolls();
        var sink = new RecordingSink();
        var context = new VanillaDeerclopsLootContext(IsExpertMode: true, IsMasterMode: true);
        VanillaDeerclopsLootPlayer[] players =
        [
            new(new PlayerSlotId(1), 10f, 20f),
            new(new PlayerSlotId(4), 30f, 40f)
        ];

        Assert.True(VanillaDeerclopsLootEvaluator.TryExecute(
            in context,
            new NpcLootWorldItemOrigin(100f, 200f),
            players,
            rolls,
            sink,
            out DeerclopsLootExecutionResult result));

        Assert.Equal(VanillaDeerclopsItemIds.DeerclopsBossBag, Assert.Single(sink.InstancedDrops).ItemType);
        Assert.Equal(
            [
                VanillaDeerclopsItemIds.DeerclopsMasterTrophy,
                VanillaDeerclopsItemIds.DeerclopsPetItem,
                VanillaDeerclopsItemIds.DeerclopsPetItem,
                VanillaDeerclopsItemIds.DeerclopsTrophy
            ],
            sink.WorldDrops.Select(static drop => drop.ItemType));
        Assert.Equal(4, result.WorldItemCount);
        Assert.Equal(1, result.InstancedItemCount);
        Assert.Equal(2, result.InstancedRecipientCount);
        Assert.Equal(2, result.MasterPetDropCount);
        Assert.Equal([new NpcLootWorldItemOrigin(10f, 20f), new NpcLootWorldItemOrigin(30f, 40f)], sink.PetOrigins);
    }

    [Theory]
    [InlineData(5090, 16, 30)]
    [InlineData(5095, 24, 28)]
    [InlineData(5098, 16, 30)]
    [InlineData(5101, 28, 20)]
    [InlineData(5108, 30, 30)]
    [InlineData(5109, 18, 18)]
    [InlineData(5110, 14, 14)]
    [InlineData(5111, 24, 24)]
    [InlineData(5113, 26, 30)]
    [InlineData(5117, 24, 24)]
    [InlineData(5118, 24, 24)]
    [InlineData(5119, 18, 20)]
    [InlineData(5385, 28, 20)]
    public void Deerclops_loot_items_have_source_backed_world_drop_dimensions(int rawType, int width, int height)
    {
        var type = new ItemTypeId(rawType);
        Assert.True(VanillaDefinitionCatalog.TryGetRuntimeDefaults(type, out VanillaItemRuntimeDefaults defaults));
        Assert.Equal((width, height), (defaults.Width, defaults.Height));
        Assert.True(VanillaDefinitionCatalog.TryGetWorldDrop(type, out VanillaItemWorldDropDefinition worldDrop));
        Assert.Equal((width, height), (worldDrop.Width, worldDrop.Height));
    }

    [Fact]
    public void Player_list_must_be_strictly_slot_ordered()
    {
        var rolls = new AlwaysSuccessRolls();
        var sink = new RecordingSink();
        var context = new VanillaDeerclopsLootContext(IsExpertMode: true, IsMasterMode: true);
        VanillaDeerclopsLootPlayer[] players =
        [
            new(new PlayerSlotId(4), 10f, 20f),
            new(new PlayerSlotId(1), 30f, 40f)
        ];

        Assert.False(VanillaDeerclopsLootEvaluator.TryExecute(
            in context,
            new NpcLootWorldItemOrigin(100f, 200f),
            players,
            rolls,
            sink,
            out _));
        Assert.Empty(sink.WorldDrops);
        Assert.Empty(sink.InstancedDrops);
    }

    private sealed class AlwaysSuccessRolls : INpcLootRollSource
    {
        public int RollLuck(int chanceDenominator) => 0;
        public int NextInt32(int inclusiveMin, int exclusiveMax) => inclusiveMin;
    }

    private sealed class RecordingSink : IDeerclopsLootDeliverySink
    {
        public List<NpcLootDrop> WorldDrops { get; } = [];
        public List<NpcLootDrop> InstancedDrops { get; } = [];
        public List<NpcLootWorldItemOrigin> PetOrigins { get; } = [];

        public bool CanDeliverInstanced(ItemTypeId itemType) => true;
        public bool CanDeliverWorldItem(ItemTypeId itemType) => true;

        public bool TryDeliverInstanced(
            in NpcLootWorldItemOrigin origin,
            in NpcLootDrop drop,
            ReadOnlySpan<VanillaDeerclopsLootPlayer> recipients,
            int slotLeaseTicks,
            INpcLootRollSource random)
        {
            InstancedDrops.Add(drop);
            return true;
        }

        public bool TryDeliverWorldItem(
            in NpcLootWorldItemOrigin origin,
            in NpcLootDrop drop,
            INpcLootRollSource random)
        {
            WorldDrops.Add(drop);
            if (drop.ItemType == VanillaDeerclopsItemIds.DeerclopsPetItem)
                PetOrigins.Add(origin);
            return true;
        }
    }
}
