using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class GenerationHoneyPatch1458Tests
{
    public static IEnumerable<object[]> OriginalRows()
    {
        yield return [0, 1, true, "D633FB9B717CEFE3EB700C4EEBD686ED054534D30D4DF3864991528E83B13382", "417221D409E961C05B1681FC9F34D330FD5A9D925F2021810C2DE90000BF41FF", 1461296927, "92,114,16,16"];
        yield return [0, 42, true, "607BC66AAE9752E1E8AD6F0EA1C181874BB04EF20E8B47596BEFECD34489B4D6", "CD22146042D8CB6AA5614D97807132DD4255E6770438F8F7A2FCBB5C201C5ED1", 1524244921, "92,114,16,16"];
        yield return [0, 1458, true, "162040D2C4D73531B58C4E636C256ED23D9A54EDC07936200CD183A90C96F2C3", "07A985D6D7DE2C9E933FD910C114B98204A68667B881C4AABE1320ACCC98C4AB", 410097122, "92,114,16,16"];
        yield return [1, 1, false, "0AF34AA9E6B6E05D0CF8CDFA111AD3454D585C22E2EF56A8542380C3A85D13DC", "5D7D82B143E9B10758D611A749826C8AF2C5FFB242C5477A131A0DFF25D967FC", 534011718, ""];
        yield return [1, 42, false, "0AF34AA9E6B6E05D0CF8CDFA111AD3454D585C22E2EF56A8542380C3A85D13DC", "FA244F107DFF1CAAED5F36CB9F3249BD46439F31C309C40C53F3590C213928BA", 1434747710, ""];
        yield return [1, 1458, false, "0AF34AA9E6B6E05D0CF8CDFA111AD3454D585C22E2EF56A8542380C3A85D13DC", "5CCC3069036C6D54F3CA441FD8C36C6A4491F8CE77EAEB20E42DF6C870ED87C7", 906992634, ""];
        yield return [2, 1, false, "1E90894C414D07AAAC7C8A01377AF152CE23B9C6130C7D749EC7F400B0C0453A", "5D7D82B143E9B10758D611A749826C8AF2C5FFB242C5477A131A0DFF25D967FC", 534011718, ""];
        yield return [2, 42, false, "1E90894C414D07AAAC7C8A01377AF152CE23B9C6130C7D749EC7F400B0C0453A", "FA244F107DFF1CAAED5F36CB9F3249BD46439F31C309C40C53F3590C213928BA", 1434747710, ""];
        yield return [2, 1458, false, "1E90894C414D07AAAC7C8A01377AF152CE23B9C6130C7D749EC7F400B0C0453A", "5CCC3069036C6D54F3CA441FD8C36C6A4491F8CE77EAEB20E42DF6C870ED87C7", 906992634, ""];
        yield return [3, 1, false, "FBE684EE1ECBD5AC7A9E284C75E4C7A98A8F58E270A756E7C7ADAA757A50D601", "5D7D82B143E9B10758D611A749826C8AF2C5FFB242C5477A131A0DFF25D967FC", 534011718, ""];
        yield return [3, 42, false, "FBE684EE1ECBD5AC7A9E284C75E4C7A98A8F58E270A756E7C7ADAA757A50D601", "FA244F107DFF1CAAED5F36CB9F3249BD46439F31C309C40C53F3590C213928BA", 1434747710, ""];
        yield return [3, 1458, false, "FBE684EE1ECBD5AC7A9E284C75E4C7A98A8F58E270A756E7C7ADAA757A50D601", "5CCC3069036C6D54F3CA441FD8C36C6A4491F8CE77EAEB20E42DF6C870ED87C7", 906992634, ""];
        yield return [4, 1, false, "2E488E4D40DF93B8F287D4B7A70EA830A1D04ACF737512F2B4CFD5B7157C8389", "5D7D82B143E9B10758D611A749826C8AF2C5FFB242C5477A131A0DFF25D967FC", 534011718, ""];
        yield return [4, 42, false, "2E488E4D40DF93B8F287D4B7A70EA830A1D04ACF737512F2B4CFD5B7157C8389", "FA244F107DFF1CAAED5F36CB9F3249BD46439F31C309C40C53F3590C213928BA", 1434747710, ""];
        yield return [4, 1458, false, "2E488E4D40DF93B8F287D4B7A70EA830A1D04ACF737512F2B4CFD5B7157C8389", "5CCC3069036C6D54F3CA441FD8C36C6A4491F8CE77EAEB20E42DF6C870ED87C7", 906992634, ""];
        yield return [5, 1, false, "C8F5A66505F2E3E453B8CABBD18DE43A7DBD710328F084527268ABEB376B7CF9", "5D7D82B143E9B10758D611A749826C8AF2C5FFB242C5477A131A0DFF25D967FC", 534011718, ""];
        yield return [5, 42, false, "C8F5A66505F2E3E453B8CABBD18DE43A7DBD710328F084527268ABEB376B7CF9", "FA244F107DFF1CAAED5F36CB9F3249BD46439F31C309C40C53F3590C213928BA", 1434747710, ""];
        yield return [5, 1458, false, "C8F5A66505F2E3E453B8CABBD18DE43A7DBD710328F084527268ABEB376B7CF9", "5CCC3069036C6D54F3CA441FD8C36C6A4491F8CE77EAEB20E42DF6C870ED87C7", 906992634, ""];
        yield return [6, 1, false, "7EC639585B92365DA69138C21FCBF26D4E51BBFC453FCD7DDBB2EBBF455E41E1", "5D7D82B143E9B10758D611A749826C8AF2C5FFB242C5477A131A0DFF25D967FC", 534011718, "92,114,16,16"];
        yield return [6, 42, false, "7EC639585B92365DA69138C21FCBF26D4E51BBFC453FCD7DDBB2EBBF455E41E1", "FA244F107DFF1CAAED5F36CB9F3249BD46439F31C309C40C53F3590C213928BA", 1434747710, "92,114,16,16"];
        yield return [6, 1458, false, "7EC639585B92365DA69138C21FCBF26D4E51BBFC453FCD7DDBB2EBBF455E41E1", "5CCC3069036C6D54F3CA441FD8C36C6A4491F8CE77EAEB20E42DF6C870ED87C7", 906992634, "92,114,16,16"];
        yield return [7, 1, false, "C846FFC218550848386FDAFB11461B39B0840029B62071F41190E22689776DCB", "5D7D82B143E9B10758D611A749826C8AF2C5FFB242C5477A131A0DFF25D967FC", 534011718, ""];
        yield return [7, 42, false, "C846FFC218550848386FDAFB11461B39B0840029B62071F41190E22689776DCB", "FA244F107DFF1CAAED5F36CB9F3249BD46439F31C309C40C53F3590C213928BA", 1434747710, ""];
        yield return [7, 1458, false, "C846FFC218550848386FDAFB11461B39B0840029B62071F41190E22689776DCB", "5CCC3069036C6D54F3CA441FD8C36C6A4491F8CE77EAEB20E42DF6C870ED87C7", 906992634, ""];
        yield return [8, 1, false, "DD5CFFA5BEC1F7A6BF5E27A831711A6F4DAC59BC69337FB66B2D620D73F3453A", "5D7D82B143E9B10758D611A749826C8AF2C5FFB242C5477A131A0DFF25D967FC", 534011718, ""];
        yield return [8, 42, false, "DD5CFFA5BEC1F7A6BF5E27A831711A6F4DAC59BC69337FB66B2D620D73F3453A", "FA244F107DFF1CAAED5F36CB9F3249BD46439F31C309C40C53F3590C213928BA", 1434747710, ""];
        yield return [8, 1458, false, "DD5CFFA5BEC1F7A6BF5E27A831711A6F4DAC59BC69337FB66B2D620D73F3453A", "5CCC3069036C6D54F3CA441FD8C36C6A4491F8CE77EAEB20E42DF6C870ED87C7", 906992634, ""];
        yield return [9, 1, true, "EEA946427F5AADB99C2D2CCC1CD675494FB39ACA2270E5298DF7B4BC34BAC170", "417221D409E961C05B1681FC9F34D330FD5A9D925F2021810C2DE90000BF41FF", 1461296927, "92,114,16,16"];
        yield return [9, 42, true, "2C0822D00D8FAE27D0413781B64AE875D40DF189E0A1760F1A37963C1F6D1F8B", "CD22146042D8CB6AA5614D97807132DD4255E6770438F8F7A2FCBB5C201C5ED1", 1524244921, "92,114,16,16"];
        yield return [9, 1458, true, "393EDF1B5E88FEB2B0C3EE3F4AAAE048FBDB646844DF63B1DAA5A8C4B7E1E1D0", "07A985D6D7DE2C9E933FD910C114B98204A68667B881C4AABE1320ACCC98C4AB", 410097122, "92,114,16,16"];
        yield return [10, 1, true, "6E62BFE5C74DF834DED41596E1D7C982CEE38842C19619460928C85DBD2C0C54", "417221D409E961C05B1681FC9F34D330FD5A9D925F2021810C2DE90000BF41FF", 1461296927, "92,114,16,16"];
        yield return [10, 42, true, "FA4AC94B68A02056D2D5903F10E0B5960CB3456CCE3EE6C421D25BD5BDDCD81F", "CD22146042D8CB6AA5614D97807132DD4255E6770438F8F7A2FCBB5C201C5ED1", 1524244921, "92,114,16,16"];
        yield return [10, 1458, true, "176BF0778B1026FF7BC67B14599CA72DEE9CBE5317CC1D11F63E5561C930737D", "07A985D6D7DE2C9E933FD910C114B98204A68667B881C4AABE1320ACCC98C4AB", 410097122, "92,114,16,16"];
        yield return [11, 1, false, "F337763102160DA43663C8CFC519BD73460D0C9DE5E142DF5C64C5A436C1317E", "5D7D82B143E9B10758D611A749826C8AF2C5FFB242C5477A131A0DFF25D967FC", 534011718, ""];
        yield return [11, 42, false, "F337763102160DA43663C8CFC519BD73460D0C9DE5E142DF5C64C5A436C1317E", "FA244F107DFF1CAAED5F36CB9F3249BD46439F31C309C40C53F3590C213928BA", 1434747710, ""];
        yield return [11, 1458, false, "F337763102160DA43663C8CFC519BD73460D0C9DE5E142DF5C64C5A436C1317E", "5CCC3069036C6D54F3CA441FD8C36C6A4491F8CE77EAEB20E42DF6C870ED87C7", 906992634, ""];
        yield return [12, 1, false, "2A20D86A1C1EE325302B3EACCDFA5F40D5E5533477E00C2BC04CCF677D5144D2", "5D7D82B143E9B10758D611A749826C8AF2C5FFB242C5477A131A0DFF25D967FC", 534011718, ""];
        yield return [12, 42, false, "2A20D86A1C1EE325302B3EACCDFA5F40D5E5533477E00C2BC04CCF677D5144D2", "FA244F107DFF1CAAED5F36CB9F3249BD46439F31C309C40C53F3590C213928BA", 1434747710, ""];
        yield return [12, 1458, false, "2A20D86A1C1EE325302B3EACCDFA5F40D5E5533477E00C2BC04CCF677D5144D2", "5CCC3069036C6D54F3CA441FD8C36C6A4491F8CE77EAEB20E42DF6C870ED87C7", 906992634, ""];
        yield return [13, 1, false, "188DAA41E78F9C3F23C8EDB47672D60F378C7AED939219FCFFA7AB50FED26BB2", "5D7D82B143E9B10758D611A749826C8AF2C5FFB242C5477A131A0DFF25D967FC", 534011718, ""];
        yield return [13, 42, false, "188DAA41E78F9C3F23C8EDB47672D60F378C7AED939219FCFFA7AB50FED26BB2", "FA244F107DFF1CAAED5F36CB9F3249BD46439F31C309C40C53F3590C213928BA", 1434747710, ""];
        yield return [13, 1458, false, "188DAA41E78F9C3F23C8EDB47672D60F378C7AED939219FCFFA7AB50FED26BB2", "5CCC3069036C6D54F3CA441FD8C36C6A4491F8CE77EAEB20E42DF6C870ED87C7", 906992634, ""];
        yield return [14, 1, false, "FD1400846A63BE48D72C12601D7F9FC9C87E1BBAFCB9AD3A84183ACA362464A1", "5D7D82B143E9B10758D611A749826C8AF2C5FFB242C5477A131A0DFF25D967FC", 534011718, ""];
        yield return [14, 42, false, "FD1400846A63BE48D72C12601D7F9FC9C87E1BBAFCB9AD3A84183ACA362464A1", "FA244F107DFF1CAAED5F36CB9F3249BD46439F31C309C40C53F3590C213928BA", 1434747710, ""];
        yield return [14, 1458, false, "FD1400846A63BE48D72C12601D7F9FC9C87E1BBAFCB9AD3A84183ACA362464A1", "5CCC3069036C6D54F3CA441FD8C36C6A4491F8CE77EAEB20E42DF6C870ED87C7", 906992634, ""];
    }

    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Honey_patch_matches_original_shapes_rejections_and_all_cell_state(
        int profile, int seed, bool accepted, string gridHash, string randomHash, int next, string areaText)
    {
        var store = new WorldTileStore(new WorldDimensions(200, 400));
        for (int x = 0; x < 200; x++)
            for (int y = 0; y < 400; y++)
            {
                bool on = y >= 120;
                ushort type = y == 120 ? (ushort)60 : (ushort)59;
                if (profile == 2) on = false;
                if (profile == 3) type = 1;
                if (profile == 4 && y > 120) type = 1;
                if (profile == 5) on = y == 120;
                if (profile == 8) type = x == 100 && y == 120 ? (ushort)60 : (ushort)59;
                var tile = new WorldTile { Type = type, Flags = on ? WorldTileFlags.Active : 0 };
                if (profile is 9 or 10)
                {
                    tile.Wall = 64; tile.TileColor = 5; tile.WallColor = 7;
                    tile.Flags |= WorldTileFlagMasks.Wires | WorldTileFlags.Actuator | WorldTileFlags.InvisibleBlock |
                        WorldTileFlags.FullbrightBlock | WorldTileFlags.InvisibleWall | WorldTileFlags.FullbrightWall;
                    if ((x + y) % 9 == 0) tile.Flags |= WorldTileFlags.Inactive;
                    if (profile == 9) Assert.True(tile.TrySetSourceShape((x + y) % 3 == 0, (byte)((x + y) % 5)));
                    if (profile == 10 && on) { tile.LiquidAmount = 17; tile.LiquidKind = (WorldLiquidKind)((x + y) % 4); }
                }
                store.Set(x, y, in tile);
            }
        if (profile is 1 or 11 or 12 or 13)
        {
            var tile = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
            if (profile == 11) tile.Flags |= WorldTileFlags.Inactive;
            if (profile == 12) Assert.True(tile.TrySetSourceShape(true, 0));
            if (profile == 13) Assert.True(tile.TrySetSourceShape(false, 2));
            store.Set(100, 100, in tile);
        }
        if (profile == 7) store.Tiles[store.GetUncheckedIndex(100, 122)].Wall = 83;
        if (profile == 14)
        {
            for (int i = 0; i < store.Count; i++) store.Tiles[i].Flags &= ~WorldTileFlags.Active;
            var tile = new WorldTile { Type = 1, Flags = WorldTileFlags.Active | WorldTileFlags.Inactive };
            store.Set(100, 120, in tile);
        }
        var areas = new List<WorldTileRegion>();
        if (profile == 6) areas.Add(new(92, 114, 16, 16));
        bool CanPlace(int x, int y, int width, int height) =>
            !areas.Any(a => x < a.ExclusiveRight && a.X < x + width && y < a.ExclusiveBottom && a.Y < y + height);
        var random = new RandomAdapter(seed);
        var patch = new HoneyPatch1458(store, random, CanPlace, areas.Add);
        Assert.Equal(accepted, patch.TryPlace(100, 100));
        Assert.Equal(gridHash, Hash(store));
        Assert.Equal(randomHash, random.Hash());
        Assert.Equal(next, random.Next());
        Assert.Equal(areaText, string.Join(';', areas.Select(a => $"{a.X},{a.Y},{a.Width},{a.Height}")));
    }

    private static string Hash(WorldTileStore store)
    {
        var text = new StringBuilder();
        WorldTileFlags[] flags = [WorldTileFlags.WireRed, WorldTileFlags.WireBlue, WorldTileFlags.WireGreen,
            WorldTileFlags.WireYellow, WorldTileFlags.Actuator, WorldTileFlags.Inactive,
            WorldTileFlags.InvisibleBlock, WorldTileFlags.FullbrightBlock,
            WorldTileFlags.InvisibleWall, WorldTileFlags.FullbrightWall];
        for (int x = 0; x < 200; x++)
            for (int y = 0; y < 400; y++)
            {
                WorldTile t = store.Get(x, y);
                text.Append(t.IsActive ? 1 : 0).Append(',').Append(t.Type).Append(',').Append(t.Wall).Append(',')
                    .Append(t.FrameX).Append(',').Append(t.FrameY).Append(',').Append(t.LiquidAmount).Append(',')
                    .Append((byte)t.LiquidKind).Append(',').Append(t.IsHalfBrick ? 1 : 0).Append(',')
                    .Append(t.SourceSlope).Append(',').Append(t.TileColor).Append(',').Append(t.WallColor);
                foreach (var f in flags) text.Append(',').Append((t.Flags & f) != 0 ? 1 : 0);
                text.Append(';');
            }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Next() => random.Next(); public int Next(int max) => random.Next(max);
        public int Next(int min, int max) => random.Next(min, max); public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
        public string Hash()
        {
            const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic;
            using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes);
            writer.Write((uint)typeof(VanillaUnifiedRandom1458).GetField("inext", f)!.GetValue(random)!);
            foreach (int value in (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", f)!.GetValue(random)!) writer.Write(value);
            return Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
        }
    }
}
