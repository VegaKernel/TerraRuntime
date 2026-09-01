using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaQueenBeeLootTests
{
    [Fact]
    public void Classic_source_order_includes_three_quarter_beenades_and_guaranteed_wax()
    {
        var random = new ScriptedRolls();
        var sink = new RecordingSink();
        var context = new VanillaQueenBeeLootContext(false, false);
        Assert.True(VanillaQueenBeeLootEvaluator.TryExecute(in context, new NpcLootWorldItemOrigin(100, 100), [], random, sink, out QueenBeeLootExecutionResult result));
        Assert.Contains(sink.WorldDrops, d => d.ItemType == VanillaQueenBeeItemIds.BeeWax && d.Stack is >= 17 and <= 30);
        Assert.Contains(sink.WorldDrops, d => d.ItemType == VanillaQueenBeeItemIds.Beenade && d.Stack is >= 10 and <= 30);
        Assert.True(result.WorldItemCount >= 3);
    }

    [Theory]
    [InlineData(842,28,20)] [InlineData(843,18,14)] [InlineData(844,18,14)] [InlineData(1121,50,18)]
    [InlineData(1123,40,40)] [InlineData(1129,8,10)] [InlineData(1130,10,10)] [InlineData(1132,22,22)]
    [InlineData(1170,16,30)] [InlineData(1364,30,30)] [InlineData(2108,28,20)] [InlineData(2431,18,16)]
    [InlineData(2502,16,30)] [InlineData(2888,12,28)] [InlineData(3322,24,24)] [InlineData(4802,16,30)]
    [InlineData(4928,14,14)] [InlineData(5483,30,30)]
    public void Queen_bee_loot_items_have_materializable_source_dimensions(int raw, int width, int height)
    {
        var type = new ItemTypeId(raw);
        Assert.True(VanillaItemDefinitionCatalog.TryGetRuntimeDefaults(type, out VanillaItemRuntimeDefaults defaults));
        Assert.Equal((width, height), (defaults.Width, defaults.Height));
        Assert.True(VanillaItemDefinitionCatalog.TryGetWorldDrop(type, out _));
    }

    private sealed class ScriptedRolls : INpcLootRollSource
    {
        public int RollLuck(int chanceDenominator) => 0;
        public int NextInt32(int inclusiveMin, int exclusiveMax) => inclusiveMin;
    }

    private sealed class RecordingSink : IQueenBeeLootDeliverySink
    {
        public List<NpcLootDrop> WorldDrops { get; } = [];
        public bool CanDeliverInstanced(ItemTypeId itemType) => true;
        public bool CanDeliverWorldItem(ItemTypeId itemType) => true;
        public bool TryDeliverInstanced(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop, ReadOnlySpan<VanillaQueenBeeLootPlayer> recipients, int slotLeaseTicks, INpcLootRollSource random) => true;
        public bool TryDeliverWorldItem(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop, INpcLootRollSource random) { WorldDrops.Add(drop); return true; }
    }
}
