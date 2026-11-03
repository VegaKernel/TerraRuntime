using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class GenerationMicroCompletion1458Tests
{
    public static IEnumerable<object[]> OriginalCases()
    {
        using var data=Resource("Cases.json.gz");using var json=JsonDocument.Parse(data);
        foreach(var group in json.RootElement.EnumerateObject())foreach(var row in group.Value.EnumerateArray())
            yield return [group.Name,row.GetProperty("seed").GetInt32(),row.GetProperty("profile").GetInt32(),row.GetRawText()];
    }

    [Theory]
    [MemberData(nameof(OriginalCases))]
    public void Actual_original_micro_biomes_match_whole_tiles_rng_structures_and_chest_items(string family,int seed,int profile,string expectedJson)
    {
        using var expectedDocument=JsonDocument.Parse(expectedJson);var expected=expectedDocument.RootElement;
        int width=family=="sword"?220:family=="mahogany"?300:900,height=family=="sword"?320:600;
        var world=new Workspace(width,height);world.InitializeVanillaStructureMap();world.InitializeVanillaGenerationSolidity();
        foreach(int type in new[]{481,482,483})world.SetVanillaGenerationSolid(new(type),false);
        Assert.True(world.TileStore.TryAttachTileEntities([],1000));
        using(var input=Resource($"{family}-{profile}.bin.gz"))using(var reader=new BinaryReader(input))
            foreach(ref var tile in world.TileStore.Tiles)tile=ReadTile(reader);
        if(family=="sword"&&profile is 5 or 6)world.VanillaStructureMap.AddProtectedStructure(profile==5?new(90,140,40,40):new(110,125,1,1));
        if(family=="deadman"&&profile==23)world.VanillaStructureMap.AddProtectedStructure(new(449,299,1,1));
        if(family=="mahogany")world.SetVanillaBuriedChestContext(new(){Height=600,WorldSurface=100,RockLayer=180,LavaLine=550,
            CopperBar=20,IronBar=22,SilverBar=21,GoldBar=19,TungstenIsSilverTier=false,DesertHiveLow=0,DesertHiveHigh=0,
            HellChestItem=[218,274,3019,220,112],JungleItemCount=seed%12});
        if(family=="deadman")
        {
            // The original fixture retains its chest record when its four cells are later made inactive.
            var saved=new WorldTile[4];for(int dx=0;dx<2;dx++)for(int dy=0;dy<2;dy++)
            {ref var t=ref world.TileStore.Tiles[(450+dx)*height+300+dy];saved[dx*2+dy]=t;t.Flags|=WorldTileFlags.Active;}
            var items=new WorldGenerationChestItem[40];foreach(int slot in new[]{0,2,5,38,39})items[slot]=new(slot+1,new(8));
            Assert.True(world.TryAddChest(450,300,"fixture",items));
            for(int dx=0;dx<2;dx++)for(int dy=0;dy<2;dy++)world.TileStore.Set(450+dx,300+dy,saved[dx*2+dy]);
        }
        var random=new SharedRandom(seed);bool returned;
        if(family=="sword")returned=MicroEnchantedSword1458.Place(world,random,110,profile==2?54:140,
            expected.GetProperty("entranceChance").GetDouble(),expected.GetProperty("realChance").GetDouble());
        else if(family=="mahogany")
        {
            returned=MicroMahoganyTree1458.Place(world,random,150,270);
            Assert.Equal(expected.GetProperty("jungleCount").GetInt32(),world.VanillaBuriedChestContext!.JungleItemCount);
        }
        else
        {
            var biome=new MicroDeadMansChest1458(world,random,180);
            Assert.Equal(expected.GetProperty("possible").EnumerateArray().Select(p=>(p[0].GetInt32(),p[1].GetInt32())),biome.GetPossibleChests());
            returned=biome.Place(450,300);
        }
        Assert.Equal(expected.GetProperty("returned").GetBoolean(),returned);
        Assert.Equal(expected.GetProperty("sourceGrid").GetString(),GridHash(world));
        Assert.Equal(expected.GetProperty("sourceRng").GetString(),random.Hash());
        Assert.Equal(expected.GetProperty("Draws").GetInt32(),random.Draws);
        Assert.Equal(Regions(expected.GetProperty("sourceStructures")),world.VanillaStructureMap.CaptureStructures());
        Assert.Equal(Regions(expected.GetProperty("sourceProtected")),world.VanillaStructureMap.CaptureProtectedStructures());
        if(expected.TryGetProperty("sourceChests",out var chests))
        {
            var actual=world.CaptureGeneratedChests();Assert.Equal(chests.GetArrayLength(),actual.Length);int index=0;
            foreach(var chest in chests.EnumerateArray())
            {
                var retained=actual[index++];Assert.Equal(chest.GetProperty("x").GetInt32(),retained.X);Assert.Equal(chest.GetProperty("y").GetInt32(),retained.Y);
                Assert.Equal(chest.GetProperty("name").GetString(),retained.Name);int slot=0;
                foreach(var item in chest.GetProperty("items").EnumerateArray())
                {Assert.Equal(new WorldChestItem(item.GetProperty("stack").GetInt32(),item.GetProperty("netID").GetInt32(),item.GetProperty("prefix").GetByte()),retained.Items[slot++]);}
                Assert.Equal(40,slot);
            }
        }
        Assert.Empty(world.CaptureGeneratedTileEntities());
    }

    private static WorldTileRegion[] Regions(JsonElement value)=>value.EnumerateArray().Select(r=>new WorldTileRegion(r[0].GetInt32(),r[1].GetInt32(),r[2].GetInt32(),r[3].GetInt32())).ToArray();
    private static Stream Resource(string name)
    {
        using var zip=new GZipStream(typeof(GenerationMicroCompletion1458Tests).Assembly.GetManifestResourceStream("GenerationMicroCompletion1458Tests."+name)??throw new InvalidOperationException("Missing source fixture."),CompressionMode.Decompress);
        var memory=new MemoryStream();zip.CopyTo(memory);memory.Position=0;return memory;
    }
    private static WorldTile ReadTile(BinaryReader reader)=>new(){Type=reader.ReadUInt16(),Wall=reader.ReadUInt16(),FrameX=reader.ReadInt16(),FrameY=reader.ReadInt16(),LiquidAmount=reader.ReadByte(),LiquidKind=(WorldLiquidKind)reader.ReadByte(),Shape=reader.ReadByte(),TileColor=reader.ReadByte(),WallColor=reader.ReadByte(),Flags=(WorldTileFlags)reader.ReadUInt16()};
    private static string GridHash(Workspace world)
    {
        using var memory=new MemoryStream();using(var writer=new BinaryWriter(memory))foreach(var t in world.TileStore.Tiles)
        {writer.Write(t.Type);writer.Write(t.Wall);writer.Write(t.FrameX);writer.Write(t.FrameY);writer.Write(t.LiquidAmount);writer.Write((byte)t.LiquidKind);writer.Write(t.Shape);writer.Write(t.TileColor);writer.Write(t.WallColor);writer.Write((ushort)t.Flags);}
        return Convert.ToHexString(SHA256.HashData(memory.ToArray()));
    }
    private sealed class SharedRandom(int seed):IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 source=new(seed);internal int Draws;
        public int Next(){Draws++;return source.Next();}public int Next(int max){Draws++;return source.Next(max);}public int Next(int min,int max){Draws++;return source.Next(min,max);}
        public double NextDouble(){Draws++;return source.NextDouble();}public void NextBytes(byte[] bytes){Draws++;source.NextBytes(bytes);}
        internal string Hash()
        {
            const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;using var memory=new MemoryStream();using(var writer=new BinaryWriter(memory))
            {writer.Write((uint)typeof(VanillaUnifiedRandom1458).GetField("inext",flags)!.GetValue(source)!);foreach(int value in(int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray",flags)!.GetValue(source)!)writer.Write(value);}
            return Convert.ToHexString(SHA256.HashData(memory.ToArray()));
        }
    }
}
