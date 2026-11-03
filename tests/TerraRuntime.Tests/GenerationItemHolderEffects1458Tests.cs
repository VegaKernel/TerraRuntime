using System.Reflection;
using System.Security.Cryptography;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class GenerationItemHolderEffects1458Tests
{
    // Actual original whole KillTile; 1152 paired raw member/frame contexts. Jar lower18 is
    // an explicitly configured raw frame, not a claim about TileObjectData placement styles.
    public static IEnumerable<object[]> OriginalRows()
    {
        yield return new object[] { 395, 8, 0, "99E2FF2FEFDDC090A869773E3A873EB9D532B9CB82DCC78CAC5C4D5FBE9638FD" };
        yield return new object[] { 395, 8, 1, "99E2FF2FEFDDC090A869773E3A873EB9D532B9CB82DCC78CAC5C4D5FBE9638FD" };
        yield return new object[] { 395, 8, 2, "99E2FF2FEFDDC090A869773E3A873EB9D532B9CB82DCC78CAC5C4D5FBE9638FD" };
        yield return new object[] { 395, 8, 3, "99E2FF2FEFDDC090A869773E3A873EB9D532B9CB82DCC78CAC5C4D5FBE9638FD" };
        yield return new object[] { 395, 353, 0, "99E2FF2FEFDDC090A869773E3A873EB9D532B9CB82DCC78CAC5C4D5FBE9638FD" };
        yield return new object[] { 395, 353, 1, "99E2FF2FEFDDC090A869773E3A873EB9D532B9CB82DCC78CAC5C4D5FBE9638FD" };
        yield return new object[] { 395, 353, 2, "99E2FF2FEFDDC090A869773E3A873EB9D532B9CB82DCC78CAC5C4D5FBE9638FD" };
        yield return new object[] { 395, 353, 3, "99E2FF2FEFDDC090A869773E3A873EB9D532B9CB82DCC78CAC5C4D5FBE9638FD" };
        yield return new object[] { 471, 8, 0, "C4F96EC51698A2929BE7FC6607322186387A4521D30C6B0D775CC7F5D4C11172" };
        yield return new object[] { 471, 8, 1, "C4F96EC51698A2929BE7FC6607322186387A4521D30C6B0D775CC7F5D4C11172" };
        yield return new object[] { 471, 8, 2, "C4F96EC51698A2929BE7FC6607322186387A4521D30C6B0D775CC7F5D4C11172" };
        yield return new object[] { 471, 8, 3, "C4F96EC51698A2929BE7FC6607322186387A4521D30C6B0D775CC7F5D4C11172" };
        yield return new object[] { 471, 8, 4, "C4F96EC51698A2929BE7FC6607322186387A4521D30C6B0D775CC7F5D4C11172" };
        yield return new object[] { 471, 8, 5, "C4F96EC51698A2929BE7FC6607322186387A4521D30C6B0D775CC7F5D4C11172" };
        yield return new object[] { 471, 8, 6, "C4F96EC51698A2929BE7FC6607322186387A4521D30C6B0D775CC7F5D4C11172" };
        yield return new object[] { 471, 8, 7, "C4F96EC51698A2929BE7FC6607322186387A4521D30C6B0D775CC7F5D4C11172" };
        yield return new object[] { 471, 8, 8, "C4F96EC51698A2929BE7FC6607322186387A4521D30C6B0D775CC7F5D4C11172" };
        yield return new object[] { 471, 353, 0, "C4F96EC51698A2929BE7FC6607322186387A4521D30C6B0D775CC7F5D4C11172" };
        yield return new object[] { 471, 353, 1, "C4F96EC51698A2929BE7FC6607322186387A4521D30C6B0D775CC7F5D4C11172" };
        yield return new object[] { 471, 353, 2, "C4F96EC51698A2929BE7FC6607322186387A4521D30C6B0D775CC7F5D4C11172" };
        yield return new object[] { 471, 353, 3, "C4F96EC51698A2929BE7FC6607322186387A4521D30C6B0D775CC7F5D4C11172" };
        yield return new object[] { 471, 353, 4, "C4F96EC51698A2929BE7FC6607322186387A4521D30C6B0D775CC7F5D4C11172" };
        yield return new object[] { 471, 353, 5, "C4F96EC51698A2929BE7FC6607322186387A4521D30C6B0D775CC7F5D4C11172" };
        yield return new object[] { 471, 353, 6, "C4F96EC51698A2929BE7FC6607322186387A4521D30C6B0D775CC7F5D4C11172" };
        yield return new object[] { 471, 353, 7, "C4F96EC51698A2929BE7FC6607322186387A4521D30C6B0D775CC7F5D4C11172" };
        yield return new object[] { 471, 353, 8, "C4F96EC51698A2929BE7FC6607322186387A4521D30C6B0D775CC7F5D4C11172" };
        yield return new object[] { 520, 8, 0, "CD111572CAB3A9C12937350A05009977B563EF8A0046BA4249246836308EF5D8" };
        yield return new object[] { 520, 353, 0, "CD111572CAB3A9C12937350A05009977B563EF8A0046BA4249246836308EF5D8" };
        yield return new object[] { 698, 8, 0, "D09CAA5E98E555EA28FB1AB2B1B28E9254D2611EFFF70AC8EC1DE172F1F410FA" };
        yield return new object[] { 698, 8, 1, "D09CAA5E98E555EA28FB1AB2B1B28E9254D2611EFFF70AC8EC1DE172F1F410FA" };
        yield return new object[] { 698, 353, 0, "D09CAA5E98E555EA28FB1AB2B1B28E9254D2611EFFF70AC8EC1DE172F1F410FA" };
        yield return new object[] { 698, 353, 1, "D09CAA5E98E555EA28FB1AB2B1B28E9254D2611EFFF70AC8EC1DE172F1F410FA" };
    }
    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Source_item_reset_plate_removal_and_next_entity_id_match(int type, int itemType, int member, string expectedMetadata)
    {
        int kind = type == 395 ? 1 : type == 471 ? 4 : type == 520 ? 6 : 8;
        int columns = type == 395 ? 2 : type == 471 ? 3 : 1;
        int dx = member % columns, dy = member / columns;
        int stride = type == 395 ? 36 : type == 471 ? 54 : 18;
        var tiles = new WorldTileStore(new(80, 80));
        Assert.True(tiles.TryAttachTileEntities([
            new(5, 40, 40, (WorldTileEntityKind)kind, new WorldItemTileEntityPayload(new((short)itemType, 0, 3))),
            new(9, 44, 40, WorldTileEntityKind.ItemFrame, new WorldItemTileEntityPayload(new(8, 0, 3))),
            new(13, 44, 44, WorldTileEntityKind.FoodPlatter, new WorldItemTileEntityPayload(new(353, 0, 3)))], 1000));
        var initial = new WorldTile
        {
            Type = (ushort)type,
            Flags = WorldTileFlags.Active | WorldTileFlags.WireRed |
            WorldTileFlags.WireBlue | WorldTileFlags.WireGreen | WorldTileFlags.WireYellow | WorldTileFlags.Actuator |
            WorldTileFlags.InvisibleBlock | WorldTileFlags.InvisibleWall | WorldTileFlags.FullbrightBlock | WorldTileFlags.FullbrightWall,
            FrameX = (short)(17 * stride + dx * 18),
            FrameY = (short)(dy * 18),
            Wall = 7,
            LiquidAmount = 123,
            LiquidKind = WorldLiquidKind.Honey,
            TileColor = 5,
            WallColor = 7
        };
        Assert.True(initial.TrySetSourceShape(true, 4)); tiles.Set(40 + dx, 40 + dy, in initial);
        var random = new NoDrawRandom(); var framing = new GenerationTileFraming1458(tiles, random);
        framing.KillTile(40 + dx, 40 + dy);
        Assert.Equal(initial, tiles.Get(40 + dx, 40 + dy));
        var owner = tiles.RuntimeTileEntities!;
        Assert.Equal(new[] { 5, 9, 13 }, owner.CaptureSnapshot().Select(e => e.PersistedId));
        var held = Assert.IsType<WorldItemTileEntityPayload>(owner.CaptureSnapshot()[0].Payload);
        Assert.Equal(default, held.Item);
        if (type == 520)
        {
            framing.KillTile(40, 40); Assert.False(tiles.Get(40, 40).IsActive);
            Assert.DoesNotContain(owner.CaptureSnapshot(), e => e.PersistedId == 5);
            Assert.Equal((byte)123, tiles.Get(40, 40).LiquidAmount); Assert.Equal(WorldLiquidKind.Honey, tiles.Get(40, 40).LiquidKind);
        }
        Assert.True(owner.TryPrepare(1, out var prepared)); Assert.NotNull(prepared);
        Assert.True(prepared.TryStageEmptyPlacement(60, 60, WorldTileEntityKind.DisplayDoll, out int id)); Assert.Equal(14, id); Assert.True(prepared.TrySeal());
        WorldSectionId[] sections = [TerrariaSectionGeometry.FromTile(tiles.Dimensions, 60, 60)];
        tiles.BeginMetadataMutation(sections); try { Assert.True(prepared.TryAdoptUnpublished()); } finally { tiles.EndMetadataMutation(sections); }
        using var stream = new MemoryStream(); Assert.Equal(WorldFileTileEntityEncodeResult.Encoded,
            WorldFileTileEntityEncoder.TryEncode(owner.CaptureSnapshot(), new(80, 80), 1000, stream, out _));
        Assert.Equal(expectedMetadata, Convert.ToHexString(SHA256.HashData(stream.ToArray())));
        Assert.Equal(0, random.Draws);
    }

    [Fact]
    public void Unknown_item_holder_metadata_is_not_assumed_empty()
    {
        var tiles = new WorldTileStore(new(80, 80)); tiles.Set(40, 40, new WorldTile { Type = 520, Flags = WorldTileFlags.Active });
        Assert.Throws<NotSupportedException>(() => new GenerationTileFraming1458(tiles, new NoDrawRandom()).KillTile(40, 40)); Assert.True(tiles.Get(40, 40).IsActive);
    }
    private sealed class NoDrawRandom : IWorldGenerationVanillaRandom
    {
        public int Draws { get; private set; }
        private int Unexpected() { Draws++; throw new InvalidOperationException("Original item reset/plate removal/creation control spent zero RNG."); }
        public int Next() => Unexpected(); public int Next(int maxValue) => Unexpected(); public int Next(int minValue, int maxValue) => Unexpected(); public double NextDouble() => Unexpected(); public void NextBytes(byte[] bytes) => Unexpected();
    }
}
