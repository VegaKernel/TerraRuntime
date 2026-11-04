using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;
using TerraRuntime.WorldGeneration.Runtime;
using Xunit;
namespace TerraRuntime.Tests;
public sealed class GenerationRemixChestRequestedStyle1458Tests
{
 public static IEnumerable<object[]> OriginalCases {
  get{foreach(string profile in new[]{"Remix","Everything"})foreach(int seed in new[]{1,42,1458,8675309})foreach(string fixture in new[]{"rawcavern7","rawcavern14"})yield return [profile,seed,fixture];}}
 [Theory][MemberData(nameof(OriginalCases))]
 public void Serial_whole_chest_calls_match_original_inventory_full_random_state_grid_and_once_flags(string profile,int seed,string fixture)
 {
  using Stream resource=typeof(GenerationRemixChestRequestedStyle1458Tests).Assembly.GetManifestResourceStream("RequestedChestStyle1458."+profile)!;using var gzip=new GZipStream(resource,CompressionMode.Decompress);using var source=JsonDocument.Parse(gzip);
  var expected=source.RootElement.GetProperty("rows").EnumerateArray().Where(r=>r.GetProperty("seed").GetInt32()==seed&&r.GetProperty("fixture").GetString()==fixture).ToArray();Assert.Equal(4,expected.Length);
  var workspace=new Workspace(1400,900);workspace.TileStore.TryAttachTileEntities([],1000);
  workspace.SetVanillaSeedProfile(new(profile switch{"Normal"=>VanillaSpecialWorldSeed1458.None,"Drunk"=>VanillaSpecialWorldSeed1458.DrunkWorld,"ForTheWorthy"=>VanillaSpecialWorldSeed1458.ForTheWorthy,"Anniversary"=>VanillaSpecialWorldSeed1458.CelebrationMk10,"DontStarve"=>VanillaSpecialWorldSeed1458.TheConstant,"NotTheBees"=>VanillaSpecialWorldSeed1458.NotTheBees,"Remix"=>VanillaSpecialWorldSeed1458.Remix,"NoTraps"=>VanillaSpecialWorldSeed1458.NoTraps,"Everything"=>(VanillaSpecialWorldSeed1458)255,"Skyblock"=>VanillaSpecialWorldSeed1458.Skyblock,_=>throw new ArgumentException(profile)},VanillaSecretWorldSeed1458.None));
  for(int x=0;x<1400;x++)for(int y=0;y<900;y++)Set(x,y,new WorldTile{FrameX=-1,FrameY=-1,Wall=2});
  var context=new BuriedChestContext1458{Height=900,WorldSurface=220,RockLayer=320,LavaLine=650,CopperBar=20,IronBar=22,SilverBar=fixture=="hellmutablesilver"?705:21,GoldBar=19,TungstenIsSilverTier=fixture=="hellsavedtungsten",Crimson=source.RootElement.GetProperty("live").GetProperty("crimson").GetBoolean(),DesertHiveHigh=350,DesertHiveLow=550,HellChestItem=[220,112,218,274,3019,112]};workspace.SetVanillaBuriedChestContext(context);
  var random=new RandomAdapter(seed);var placer=BuriedChest1458.CreateOwned(workspace,random);
  foreach(var row in expected){
   int x=row.GetProperty("x").GetInt32(),y=row.GetProperty("y").GetInt32();
   for(int c=x-1;c<=x;c++){var floor=workspace.TileStore.Get(c,y);floor.Flags|=WorldTileFlags.Active;floor.Type=1;if(fixture=="slope")Assert.True(floor.TrySetSourceShape(false,1));Set(c,y,floor);}
   if(fixture=="dungeon"||fixture.EndsWith("biome")||fixture is "goldlocked" or "wooden")for(int c=x-16;c<=x+16;c++)for(int r=y-16;r<=y+16;r++){var t=workspace.TileStore.Get(c,r);t.Wall=7;Set(c,r,t);}
   if(fixture=="living"){var t=workspace.TileStore.Get(x-1,y-1);t.Wall=244;Set(x-1,y-1,t);}
   if(fixture=="blocked"){var t=workspace.TileStore.Get(x,y-1);t.Flags|=WorldTileFlags.Active;t.Type=1;Set(x,y-1,t);}
   if(fixture=="lava"){var t=workspace.TileStore.Get(x,y-1);t.LiquidAmount=255;t.LiquidKind=WorldLiquidKind.Lava;Set(x,y-1,t);}
   if(fixture=="cuttable")for(int c=x-1;c<=x;c++)for(int r=y-2;r<y;r++){var t=workspace.TileStore.Get(c,r);t.Flags|=WorldTileFlags.Active;t.Type=3;t.FrameX=t.FrameY=0;Set(c,r,t);}
   if(fixture=="inactiveheader")for(int c=x-1;c<=x;c++)for(int r=y-2;r<y;r++){var t=workspace.TileStore.Get(c,r);Assert.True(t.TrySetSourceShape(true,3));t.Flags|=WorldTileFlags.WireRed|WorldTileFlags.WireBlue|WorldTileFlags.WireGreen|WorldTileFlags.WireYellow|WorldTileFlags.Actuator|WorldTileFlags.Inactive|WorldTileFlags.InvisibleBlock|WorldTileFlags.InvisibleWall|WorldTileFlags.FullbrightBlock|WorldTileFlags.FullbrightWall;t.TileColor=17;t.WallColor=3;Set(c,r,t);}
   Assert.Null(row.GetProperty("error").GetString());
   bool result=placer.TryAdd(x,y-3,out int left,out int top,row.GetProperty("primary").GetInt32(),false,row.GetProperty("style").GetInt32(),fixture=="slope",(ushort)(fixture=="desertbiome"?467:0));
   Assert.Equal(row.GetProperty("success").GetBoolean(),result);Assert.Equal(row.GetProperty("locationX").GetInt32(),left);Assert.Equal(row.GetProperty("locationY").GetInt32(),top);
   Assert.Equal(row.GetProperty("grid").GetString(),GridHash(workspace));Assert.Equal(row.GetProperty("cursor").GetInt32(),random.Cursor);Assert.Equal(row.GetProperty("slots").EnumerateArray().Select(n=>n.GetInt32()),random.Slots);Assert.Equal(row.GetProperty("next").GetInt32(),random.PeekNext());
   var actual=workspace.CaptureGeneratedChests();var gold=row.GetProperty("chests").EnumerateArray().ToArray();Assert.Equal(gold.Length,actual.Length);
   for(int c=0;c<actual.Length;c++){Assert.Equal(gold[c].GetProperty("slot").GetInt32(),actual[c].SlotId);Assert.Equal(gold[c].GetProperty("x").GetInt32(),actual[c].X);Assert.Equal(gold[c].GetProperty("y").GetInt32(),actual[c].Y);var items=gold[c].GetProperty("items").EnumerateArray().ToArray();Assert.Equal(40,items.Length);for(int n=0;n<40;n++){Assert.Equal(items[n].GetProperty("type").GetInt32(),actual[c].Items[n].ItemType);Assert.Equal(items[n].GetProperty("stack").GetInt32(),actual[c].Items[n].Stack);Assert.Equal(items[n].GetProperty("prefix").GetInt32(),actual[c].Items[n].Prefix);}}
   Assert.Equal(row.GetProperty("hellChest").GetInt32(),context.HellChest);Assert.Equal(row.GetProperty("generatedShadowKey").GetBoolean(),context.GeneratedShadowKey);Assert.Equal(row.GetProperty("generatedRamRune").GetBoolean(),context.GeneratedRamRune);Assert.Equal(row.GetProperty("gennedLivingMahoganyWands").GetBoolean(),context.GennedLivingMahoganyWands);
  }
  void Set(int x,int y,WorldTile t)=>workspace.TileStore.Set(x,y,in t);
 }
    private static string GridHash(Workspace workspace)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] column = new byte[workspace.HeightTiles * 17];
        WorldTileFlags[] masks = [WorldTileFlags.WireRed, WorldTileFlags.WireBlue, WorldTileFlags.WireGreen,
            WorldTileFlags.WireYellow, WorldTileFlags.Actuator, WorldTileFlags.Inactive,
            WorldTileFlags.InvisibleBlock, WorldTileFlags.FullbrightBlock,
            WorldTileFlags.InvisibleWall, WorldTileFlags.FullbrightWall];
        int unknown = 0, invalid = 0, reserved = 0;
        for (int x = 0; x < workspace.WidthTiles; x++)
        {
            for (int y = 0; y < workspace.HeightTiles; y++)
            {
                WorldTile t = workspace.TileStore.Get(x, y);
                Span<byte> b = column.AsSpan(y * 17, 17);
                b[0] = (byte)(t.IsActive ? 1 : 0);
                BinaryPrimitives.WriteUInt16LittleEndian(b[1..], t.Type);
                BinaryPrimitives.WriteUInt16LittleEndian(b[3..], t.Wall);
                BinaryPrimitives.WriteInt16LittleEndian(b[5..], t.FrameX);
                BinaryPrimitives.WriteInt16LittleEndian(b[7..], t.FrameY);
                b[9] = t.LiquidAmount; b[10] = (byte)t.LiquidKind; b[11] = (byte)(t.IsHalfBrick ? 1 : 0);
                b[12] = t.SourceSlope; b[13] = t.TileColor; b[14] = t.WallColor;
                ushort flags = 0;
                for (int bit = 0; bit < masks.Length; bit++) if ((t.Flags & masks[bit]) != 0) flags |= (ushort)(1 << bit);
                BinaryPrimitives.WriteUInt16LittleEndian(b[15..], flags);
                if ((t.Flags & ~WorldTileFlagMasks.Known) != 0) unknown++;
                if (!t.HasValidSourceShape) invalid++;
                if (t.Reserved != 0) reserved++;
            }
            hash.AppendData(column);
        }
        Assert.Equal(0, unknown); Assert.Equal(0, invalid); Assert.Equal(0, reserved);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 source = new(seed);
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        internal int Cursor => Convert.ToInt32(typeof(VanillaUnifiedRandom1458).GetField("inext", Hidden)!.GetValue(source));
        internal int[] Slots => ((int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", Hidden)!.GetValue(source)!).ToArray();
        public int PeekNext(){var clone=new VanillaUnifiedRandom1458(0);typeof(VanillaUnifiedRandom1458).GetField("seedArray",Hidden)!.SetValue(clone,Slots);typeof(VanillaUnifiedRandom1458).GetField("inext",Hidden)!.SetValue(clone,typeof(VanillaUnifiedRandom1458).GetField("inext",Hidden)!.GetValue(source));return clone.Next();}
        public int Next() => source.Next();
        public int Next(int maximum) => source.Next(maximum);
        public int Next(int minimum, int maximum) => source.Next(minimum, maximum);
        public double NextDouble() => source.NextDouble();
        public void NextBytes(byte[] buffer) => source.NextBytes(buffer);
    }
}
