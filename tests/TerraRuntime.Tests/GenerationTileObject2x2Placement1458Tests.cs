using System.Reflection;
using System.Security.Cryptography;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;
using Xunit;
namespace TerraRuntime.Tests;
public sealed class GenerationTileObject2x2Placement1458Tests
{
    [Fact]
    public void Every_source_floor_identity_shape_and_descriptor_matches_actual_tile_object()
    {
        var w=Create();var random=new ObjectRandom();var framing=new GenerationTileFraming1458(w.TileStore,random,w,true);using var bytes=new MemoryStream();using var writer=new BinaryWriter(bytes);
        foreach(ushort family in new ushort[]{21,467,411})for(int type=0;type<754;type++)foreach(bool active in new[]{false,true})foreach(bool actuated in new[]{false,true})for(int shape=0;shape<10;shape++)
        {
            for(int x=40;x<42;x++)for(int y=40;y<43;y++)w.TileStore.Set(x,y,default);
            for(int x=40;x<42;x++){var t=new WorldTile{Type=(ushort)type,Flags=(active?WorldTileFlags.Active:0)|(actuated?WorldTileFlags.Inactive:0),FrameX=18};t.TrySetSourceShape(shape>=5,(byte)(shape%5));w.TileStore.Set(x,42,t);}
            bool accepted=GenerationTileObject2x2Placement1458.CanPlace(w.TileStore,w,40,41,family);if(accepted)GenerationTileObject2x2Placement1458.Place(w.TileStore,framing,40,41,family,0);
            writer.Write(accepted);for(int x=40;x<42;x++)for(int y=40;y<43;y++){var t=w.TileStore.Get(x,y);writer.Write(t.IsActive);writer.Write(t.Type);writer.Write(t.FrameX);writer.Write(t.FrameY);writer.Write(t.SourceSlope);writer.Write(t.IsHalfBrick);writer.Write(t.IsActuated);}
        }
        writer.Flush();Assert.Equal("1838467CF3C88944CF17C96217CEE920555F70DA4A2B41F0F656C1162D26BA82",Convert.ToHexString(SHA256.HashData(bytes.ToArray())));Assert.True(random.Source.HasSameState(new(1458)));
    }
    [Fact]
    public void Every_source_footprint_identity_preserves_headers_and_orders_kills_before_all_writes()
    {
        var w=Create();using var bytes=new MemoryStream();using var writer=new BinaryWriter(bytes);
        foreach(ushort family in new ushort[]{21,467,411})for(int type=0;type<754;type++)for(int member=0;member<4;member++)for(int header=0;header<7;header++)
        {
            for(int x=34;x<48;x++)for(int y=34;y<49;y++)w.TileStore.Set(x,y,default);
            for(int x=40;x<42;x++)w.TileStore.Set(x,42,new WorldTile{Type=1,Flags=WorldTileFlags.Active});
            for(int column=0;column<2;column++)for(int row=0;row<2;row++)
            {
                var t=new WorldTile{Type=(ushort)type,FrameX=(short)(header*36+column*18),FrameY=(short)(row*18),Wall=15,WallColor=9,TileColor=7,Flags=WorldTileFlagMasks.Wires|WorldTileFlags.Actuator|WorldTileFlagMasks.Visibility|WorldTileFlagMasks.Fullbright,LiquidAmount=(byte)(header==0?0:255),LiquidKind=(WorldLiquidKind)(header%4)};
                if(column*2+row==member)t.Flags|=WorldTileFlags.Active;if(header==3)t.Flags|=WorldTileFlags.Inactive;t.TrySetSourceShape(header>=2,(byte)(header>=2?4:0));w.TileStore.Set(40+column,40+row,t);
            }
            var random=new ObjectRandom();var framing=new GenerationTileFraming1458(w.TileStore,random,w,true);bool accepted=GenerationTileObject2x2Placement1458.CanPlace(w.TileStore,w,40,41,family);
            if(accepted)GenerationTileObject2x2Placement1458.Place(w.TileStore,framing,40,41,family,0);writer.Write(accepted);
            for(int x=0;x<80;x++)for(int y=0;y<80;y++)
            {
                var t=w.TileStore.Get(x,y);if(x>=38&&x<=43&&y>=38&&y<=44){writer.Write(t.Type);writer.Write(t.Wall);writer.Write(t.FrameX);writer.Write(t.FrameY);writer.Write(t.LiquidAmount);writer.Write((byte)t.LiquidKind);writer.Write(t.Shape);writer.Write(t.TileColor);writer.Write(t.WallColor);writer.Write((ushort)t.Flags);}else Assert.Equal(default(WorldTile),t);
            }
            writer.Write((uint)typeof(VanillaUnifiedRandom1458).GetField("inext",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(random.Source)!);foreach(int value in (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(random.Source)!)writer.Write(value);
        }
        writer.Flush();Assert.Equal("0DD3187EFBC76E2C8CC63CBE8A3DBDBA57F2765D2956672D469A97BF3BAE4D67",Convert.ToHexString(SHA256.HashData(bytes.ToArray())));
    }
    private static Workspace Create()
    {
        var w=new Workspace(80,80);w.InitializeVanillaGenerationSolidity();foreach(int type in new[]{481,482,483})w.SetVanillaGenerationSolid(new(type),false);Assert.True(w.TileStore.TryAttachTileEntities([],1000));return w;
    }
    private sealed class ObjectRandom:IWorldGenerationVanillaRandom
    {
        internal readonly VanillaUnifiedRandom1458 Source=new(1458);
        public int Next()=>Source.Next();public int Next(int max)=>Source.Next(max);public int Next(int min,int max)=>Source.Next(min,max);public double NextDouble()=>Source.NextDouble();public void NextBytes(byte[] bytes)=>Source.NextBytes(bytes);
    }
}
