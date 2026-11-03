using System.Reflection;
using System.Security.Cryptography;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;
using Xunit;
namespace TerraRuntime.Tests;
public sealed class GenerationLegacyTwoByTwoPlacement1458Tests
{
    [Fact]
    public void Actual_original_legacy_placements_preserve_callback_order_independent_half_and_return_before_final_framing()
    {
        var w=new Workspace(80,80);w.InitializeVanillaGenerationSolidity();foreach(int type in new[]{481,482,483})w.SetVanillaGenerationSolid(new(type),false);Assert.True(w.TileStore.TryAttachTileEntities([],1000));
        using var bytes=new MemoryStream();using var writer=new BinaryWriter(bytes);
        foreach(ushort family in new ushort[]{94,411})foreach(int floor in new[]{1,14,19,88,427,435,481,659})for(int shape=0;shape<10;shape++)foreach(bool actuated in new[]{false,true})for(int header=0;header<5;header++)
        {
            for(int x=34;x<48;x++)for(int y=34;y<49;y++)w.TileStore.Set(x,y,default);
            for(int column=0;column<2;column++)
            {
                var t=new WorldTile{Type=(ushort)floor,Flags=WorldTileFlags.Active|(actuated?WorldTileFlags.Inactive:0),FrameX=18};t.TrySetSourceShape(shape>=5,(byte)(shape%5));w.TileStore.Set(40+column,42,t);
                for(int row=0;row<2;row++)
                {
                    t=new(){Type=(ushort)(header==1?1:header==2?3:header==3?488:0),Flags=WorldTileFlags.WireRed|WorldTileFlags.WireBlue|WorldTileFlags.Actuator|WorldTileFlagMasks.Visibility|WorldTileFlagMasks.Fullbright,Wall=15,TileColor=7,WallColor=9,FrameX=18,FrameY=36,LiquidAmount=(byte)(header==4?255:0),LiquidKind=WorldLiquidKind.Lava};
                    if(header is 1 or 2 or 3&&column==1&&row==1)t.Flags|=WorldTileFlags.Active;t.TrySetSourceShape(true,4);w.TileStore.Set(40+column,40+row,t);
                }
            }
            var random=new LegacyRandom();writer.Write(CaveHousePlacement1458.PlaceHouseObject(w,random,41,41,family,forced:true));
            for(int x=0;x<80;x++)for(int y=0;y<80;y++)
            {
                var t=w.TileStore.Get(x,y);if(x>=38&&x<=43&&y>=38&&y<=44){writer.Write(t.Type);writer.Write(t.Wall);writer.Write(t.FrameX);writer.Write(t.FrameY);writer.Write(t.LiquidAmount);writer.Write((byte)t.LiquidKind);writer.Write(t.Shape);writer.Write(t.TileColor);writer.Write(t.WallColor);writer.Write((ushort)t.Flags);}else Assert.Equal(default(WorldTile),t);
            }
            writer.Write((uint)typeof(VanillaUnifiedRandom1458).GetField("inext",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(random.Source)!);foreach(int value in(int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(random.Source)!)writer.Write(value);
        }
        writer.Flush();Assert.Equal("F760E1D0A43F712FAA2945F18BA11C753D6E4512328C6BB149261A32B797B430",Convert.ToHexString(SHA256.HashData(bytes.ToArray())));
    }
    private sealed class LegacyRandom:IWorldGenerationVanillaRandom
    {
        internal readonly VanillaUnifiedRandom1458 Source=new(1458);
        public int Next()=>Source.Next();public int Next(int max)=>Source.Next(max);public int Next(int min,int max)=>Source.Next(min,max);public double NextDouble()=>Source.NextDouble();public void NextBytes(byte[] bytes)=>Source.NextBytes(bytes);
    }
}
