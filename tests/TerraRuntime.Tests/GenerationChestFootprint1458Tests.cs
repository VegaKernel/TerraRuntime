using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationChestFootprint1458Tests
{
    [Theory]
    [InlineData(324, "D76EB1AB51CAA53CD9F60FAD1CAF7A42F638BBC3BF0F04421F863B9C4AC8EC5A", "64323EF6FECB3C36DDEEDF7A982EB5EF9A656293EC8031845C32C1BF39F531AE", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData(186, "7BD905E0A06C8DEB0CCD738EEB160A4CC446FE17E0EF092E1DB24873BAD63B2F", "64323EF6FECB3C36DDEEDF7A982EB5EF9A656293EC8031845C32C1BF39F531AE", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData(187, "287C68F29FA88F663E1BD2153077AF30D0E59EE92FB2B115D5D2F5D4A7D5A068", "64323EF6FECB3C36DDEEDF7A982EB5EF9A656293EC8031845C32C1BF39F531AE", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData(185, "42828FAF28A58AA8164FAD3D67D2758FCA798C84B404A539EAD830EF83E3FCE6", "64323EF6FECB3C36DDEEDF7A982EB5EF9A656293EC8031845C32C1BF39F531AE", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData(165, "87CAFBAF9D73F74FEB1B6302F659ED29156CA5770F904A8930E896A4C34F24CC", "64323EF6FECB3C36DDEEDF7A982EB5EF9A656293EC8031845C32C1BF39F531AE", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData(530, "B2DD8E4C6AC77F34BBE40DE504EA2AE909E080B3D90C6DBBFEFF12C3AB4D4B2E", "64323EF6FECB3C36DDEEDF7A982EB5EF9A656293EC8031845C32C1BF39F531AE", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData(233, "9D96B00B42A16423995B467BECDBAC206E4F0B9B76214443BC56620D3AE7B69F", "64323EF6FECB3C36DDEEDF7A982EB5EF9A656293EC8031845C32C1BF39F531AE", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData(227, "5525E94FF5862688674156ADF9315420D246FF3594FFC8CC0051F4CEF2C14F5C", "64323EF6FECB3C36DDEEDF7A982EB5EF9A656293EC8031845C32C1BF39F531AE", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData(485, "9193CB13B8F4EE46C1F0AD1C1A79675D9C9A582F88C612B13EE30D2E38AC065B", "64323EF6FECB3C36DDEEDF7A982EB5EF9A656293EC8031845C32C1BF39F531AE", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData(81, "9FEAB3CEA77364BEE1EEAB67CE882004F30AF37FB69588CAEED38869613ACF42", "64323EF6FECB3C36DDEEDF7A982EB5EF9A656293EC8031845C32C1BF39F531AE", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData(624, "087FE4435C93F9B1BA5955E94399072AE04E8D03DD006E63FF507424C487C64A", "64323EF6FECB3C36DDEEDF7A982EB5EF9A656293EC8031845C32C1BF39F531AE", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData(51, "D8AA71D13307701CDDD59277F090E211CDAF0127FE8C66E506957F8CB4C80B21", "64323EF6FECB3C36DDEEDF7A982EB5EF9A656293EC8031845C32C1BF39F531AE", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData(-1, "B0FAC308BD6105B483903574908F3F791AF47B56E3857038FFC8C53F9BFCB42B", "7E5E03F83D734439FAD4B368D19234C3C594E9FE45AFF52C67A7E1D93CBACBC9", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    public void Chest_placement_matches_original_removal_and_preserves_inactive_source_headers(
        int obstacle, string before, string after, string rng, int next)
    {
        // Actual untouched WorldGen.PlaceChest, fresh Main metadata1+2, netMode0/generatingWorld.
        // The reference hashes all21 fields in all100x100 cells and all56 physical RNG slots.
        var workspace = new Workspace(100, 100);
        for (int x = 45; x <= 55; x++)
        {
            var floor = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x, 60, in floor);
        }
        var tile = new WorldTile { Type = (ushort)(obstacle == -1 ? 17 : obstacle), Wall = 1,
            FrameX = 18, FrameY = 18, TileColor = 5, WallColor = 7,
            Flags = (obstacle == -1 ? WorldTileFlags.None : WorldTileFlags.Active) |
                WorldTileFlags.WireRed | WorldTileFlags.WireBlue | WorldTileFlags.WireGreen |
                WorldTileFlags.WireYellow | WorldTileFlags.Actuator | WorldTileFlags.InvisibleBlock |
                WorldTileFlags.FullbrightBlock | WorldTileFlags.InvisibleWall | WorldTileFlags.FullbrightWall };
        Assert.True(tile.TrySetSourceShape(true, 3));
        workspace.TileStore.Set(49, 58, in tile);
        Assert.Equal(before, FullState(workspace));
        var random = new RandomAdapter(1458);
        var state = new BuriedChestContext1458 { Height = 100, WorldSurface = 30, RockLayer = 50,
            LavaLine = 80, CopperBar = 20, IronBar = 22, SilverBar = 21, GoldBar = 19,
            TungstenIsSilverTier = false, DesertHiveLow = 0, DesertHiveHigh = 0, HellChestItem = [] };
        var placer = new BuriedChest1458(workspace.TileStore, random, state, workspace: workspace);
        Assert.True(placer.TryPlaceChest(49, 59, 21, false, 0));
        Assert.Equal(after, FullState(workspace));
        Assert.Equal(rng, random.Fingerprint());
        Assert.Equal(next, random.Next());
    }

    private static string FullState(Workspace workspace)
    {
        var b = new StringBuilder();
        for (int x = 0; x < workspace.WidthTiles; x++)
        for (int y = 0; y < workspace.HeightTiles; y++)
        {
            WorldTile t = workspace.TileStore.Get(x, y);
            b.Append(t.IsActive ? 1 : 0).Append(',').Append(t.Type).Append(',').Append(t.Wall).Append(',')
                .Append(t.FrameX).Append(',').Append(t.FrameY).Append(',').Append(t.LiquidAmount).Append(',')
                .Append((int)t.LiquidKind).Append(',').Append(t.IsHalfBrick ? 1 : 0).Append(',').Append(t.SourceSlope).Append(',')
                .Append(t.TileColor).Append(',').Append(t.WallColor);
            WorldTileFlags[] flags = [WorldTileFlags.WireRed, WorldTileFlags.WireBlue, WorldTileFlags.WireGreen,
                WorldTileFlags.WireYellow, WorldTileFlags.Actuator, WorldTileFlags.Inactive, WorldTileFlags.InvisibleBlock,
                WorldTileFlags.FullbrightBlock, WorldTileFlags.InvisibleWall, WorldTileFlags.FullbrightWall];
            foreach (WorldTileFlags flag in flags) b.Append(',').Append((t.Flags & flag) != 0 ? 1 : 0);
            b.Append(';');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(b.ToString())));
    }

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public string Fingerprint()
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var cursor = typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(random);
            var slots = (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(random)!;
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cursor + ";" + string.Join(",", slots))));
        }
        public int Next() => random.Next();
        public int Next(int maxValue) => random.Next(maxValue);
        public int Next(int minValue, int maxValue) => random.Next(minValue, maxValue);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] buffer) => random.NextBytes(buffer);
    }
}
