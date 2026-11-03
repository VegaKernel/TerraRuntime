using System.Security.Cryptography;
using System.Text;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;
using Xunit;
namespace TerraRuntime.Tests;
public sealed class GenerationStructureMap1458Tests
{
    [Fact]
    public void Every_source_identity_override_reservation_and_order_matches_original()
    {
        var tiles=new WorldTileStore(new(80,80));using var bytes=new MemoryStream();using var writer=new BinaryWriter(bytes,Encoding.UTF8,true);
        foreach(int type in Enumerable.Range(0,754).Concat(new[]{754,65535}))foreach(bool active in new[]{false,true})
        foreach(bool overrideTiles in new[]{false,true})foreach(int padding in new[]{0,1})foreach(int reservation in new[]{0,1,2})foreach(bool disjoint in new[]{false,true})
        {
            tiles.Set(40,40,new WorldTile{Type=(ushort)type,Flags=active?WorldTileFlags.Active:0});var map=new VanillaStructureMap1458(tiles);int rx=disjoint?45:40;
            if(reservation==1)map.AddStructure(new(rx,40,1,1));if(reservation==2)map.AddProtectedStructure(new(rx,40,1,1));
            writer.Write(Outcome(()=>map.CanPlace(new(40,40,1,1),padding,overrideTiles?static t=>VanillaStructureMap1458.IsGeneralPlacementTile(t)||t is 21 or 467 or 138 or 664 or 712 or 713 or 714 or 715:null)));
            foreach(var rows in new[]{map.CaptureStructures(),map.CaptureProtectedStructures()})
            {writer.Write(rows.Length);foreach(var row in rows){writer.Write(row.X);writer.Write(row.Y);writer.Write(row.Width);writer.Write(row.Height);}}
        }
        writer.Flush();Assert.Equal("7CF1C2941F20F1CF99F6B7FBF4C2339CF48EB2D0596DCACC33BEFFD2661AEE29",Convert.ToHexString(SHA256.HashData(bytes.ToArray())));
    }
    public static IEnumerable<object[]> OriginalBoundaries()
    {
        yield return new object[]{-1,0,false,0};
        yield return new object[]{-1,0,true,0};
        yield return new object[]{-1,1,false,0};
        yield return new object[]{-1,1,true,0};
        yield return new object[]{0,0,false,1};
        yield return new object[]{0,0,true,0};
        yield return new object[]{0,1,false,2};
        yield return new object[]{0,1,true,0};
        yield return new object[]{1,0,false,1};
        yield return new object[]{1,0,true,1};
        yield return new object[]{1,1,false,1};
        yield return new object[]{1,1,true,0};
        yield return new object[]{77,0,false,1};
        yield return new object[]{77,0,true,1};
        yield return new object[]{77,1,false,1};
        yield return new object[]{77,1,true,1};
        yield return new object[]{78,0,false,1};
        yield return new object[]{78,0,true,1};
        yield return new object[]{78,1,false,1};
        yield return new object[]{78,1,true,1};
        yield return new object[]{79,0,false,0};
        yield return new object[]{79,0,true,0};
        yield return new object[]{79,1,false,0};
        yield return new object[]{79,1,true,0};
        yield return new object[]{80,0,false,0};
        yield return new object[]{80,0,true,0};
        yield return new object[]{80,1,false,0};
        yield return new object[]{80,1,true,0};
    }
    [Theory]
    [MemberData(nameof(OriginalBoundaries))]
    public void Padded_array_fault_is_reached_only_after_original_protection_check(int x,int padding,bool protect,int expected)
    {
        var map=new VanillaStructureMap1458(new WorldTileStore(new(80,80)));if(protect)map.AddProtectedStructure(new(0,40,1,1));
        Assert.Equal((byte)expected,Outcome(()=>map.CanPlace(new(x,40,1,1),padding)));
    }
    private static byte Outcome(Func<bool> action){try{return(byte)(action()?1:0);}catch(IndexOutOfRangeException){return 2;}}
}
