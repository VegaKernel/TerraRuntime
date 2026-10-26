using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.World;
using Xunit;

namespace TerraRuntime.Tests.ToolPlayer;
internal static partial class ToolPlayerBoundaryChecks
{
    internal static void RunSingleCellFrames()
    {
        using (var metadata = typeof(ToolPlayerBoundaryChecks).Assembly.GetManifestResourceStream("ToolSingleCellTables1458")!)
        using (var unzip = new GZipStream(metadata, CompressionMode.Decompress))
        using (var tableDoc = JsonDocument.Parse(unzip))
        {
            var tables = tableDoc.RootElement.EnumerateArray().Select(x => x.GetInt32()).ToHashSet();
            var slots = new ToolSingleCellFrameTile1458[9];
            for (int type = 0; type < 754; type++)
            {
                // Actuated support excludes AnchorValid but the source's raw
                // tileTable alternative still uses active, independently of nactive.
                slots[7] = new(new(type),0,true,true,false,0);
                Assert.True(VanillaToolSingleCellFrame1458.TryPlan(new(49),0,0,slots,out _,out bool kill));
                Assert.Equal(!tables.Contains(type),kill);
            }
        }
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using var stream = typeof(ToolPlayerBoundaryChecks).Assembly.GetManifestResourceStream("ToolSingleCell" + platform + "1458")!;
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var doc = JsonDocument.Parse(gzip); Assert.Equal(160, doc.RootElement.GetArrayLength());
            foreach (var row in doc.RootElement.EnumerateArray())
            {
                using var f = new Fixture(3509); var world = Composition(f.State).WorldTileAuthority;
                foreach (var cell in row.GetProperty("before").EnumerateArray())
                    f.Tiles.Set(cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32(), SingleCellTile(cell));
                var target = f.Tiles.Get(50, 50);
                var window = new ToolSingleCellFrameTile1458[9];
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                { var t = f.Tiles.Get(50 + dx, 50 + dy); window[(dy + 1) * 3 + dx + 1] = new(t.TileType,t.FrameX,t.IsActive,t.IsActuated,t.IsHalfBrick,t.SourceSlope); }
                Assert.True(VanillaToolSingleCellFrame1458.TryPlan(target.TileType,target.FrameX,target.Wall,window,out short frame,out bool kill));
                var sourceTarget = row.GetProperty("after").EnumerateArray().Single(x => x.GetProperty("x").GetInt32()==50 && x.GetProperty("y").GetInt32()==50);
                var expected = SingleCellTile(sourceTarget);
                Assert.Equal(!kill, expected.IsActive);
                if (!kill) Assert.Equal(expected.FrameX, frame);
                if (!row.GetProperty("fail").GetBoolean()) continue;
                var random = ReadLucyRandom(row.GetProperty("beforeRandom"));
                var image = new ToolWorldTileOverlay1458(f.Tiles);
                Assert.True(world.TryCaptureToolTreeWorld(out var context)); Assert.NotNull(context);
                Assert.True(f.Players.TryCaptureToolTreeActors(out var actors)); Assert.NotNull(actors);
                bool client = row.GetProperty("role").GetInt32()==1;
                Assert.True(world.TryCreateToolMutationPlan(image,out var plan)); Assert.NotNull(plan);
                using (plan!)
                {
                    if (!client) { plan!.SourceRandom.CopyStateFrom(random); random=plan.SourceRandom; }
                    Assert.True(world.TryCreateToolSourcePass(image,random,client,false,context!.Facts,actors!.Actors,client?null:plan,out var pass));
                    Assert.True(pass!.TryKillTile(50,50,true,false,false,out _), $"{platform}/{row.GetProperty("type")}/{row.GetProperty("mode")}/role{row.GetProperty("role")}");
                    Assert.True(random.HasSameState(ReadLucyRandom(row.GetProperty("afterRandom"))), $"RNG {platform}/{row.GetProperty("type")}/{row.GetProperty("mode")}/role{row.GetProperty("role")}");
                    foreach (var cell in row.GetProperty("after").EnumerateArray())
                    {
                        Assert.True(image.TryRead(cell.GetProperty("x").GetInt32(),cell.GetProperty("y").GetInt32(),out var actual));
                        var after=SingleCellTile(cell); Assert.Equal(after.IsActive,actual.IsActive);
                        if (after.IsActive) { Assert.Equal(after.Type,actual.Type); if (after.Type is 49 or 136) Assert.Equal(after.FrameX,actual.FrameX); }
                        Assert.Equal(after.Flags,actual.Flags); Assert.Equal(after.SourceSlope,actual.SourceSlope); Assert.Equal(after.Wall,actual.Wall);
                    }
                }
                Assert.True(f.Tiles.Get(50,50).Equals(target));
            }
        }
    }
    private static WorldTile SingleCellTile(JsonElement cell)
    {
        int h=cell.GetProperty("header").GetInt32(),b1=cell.GetProperty("b1").GetInt32(),b3=cell.GetProperty("b3").GetInt32();
        var t=new WorldTile { Type=checked((ushort)cell.GetProperty("type").GetInt32()),Wall=checked((ushort)cell.GetProperty("wall").GetInt32()),
            FrameX=checked((short)cell.GetProperty("frameX").GetInt32()),FrameY=checked((short)cell.GetProperty("frameY").GetInt32()),
            LiquidAmount=checked((byte)cell.GetProperty("liquid").GetInt32()),TileColor=(byte)(h&31),WallColor=(byte)(b1&31),LiquidKind=(WorldLiquidKind)((b1&96)>>5) };
        if((h&32)!=0)t.Flags|=WorldTileFlags.Active;
        if((h&64)!=0)t.Flags|=WorldTileFlags.Inactive;
        if((h&128)!=0)t.Flags|=WorldTileFlags.WireRed;
        if((h&256)!=0)t.Flags|=WorldTileFlags.WireBlue;
        if((h&512)!=0)t.Flags|=WorldTileFlags.WireGreen;
        if((h&2048)!=0)t.Flags|=WorldTileFlags.Actuator;
        if((h&32768)!=0)t.Flags|=WorldTileFlags.FullbrightWall;
        if((b1&128)!=0)t.Flags|=WorldTileFlags.WireYellow;
        if((b3&32)!=0)t.Flags|=WorldTileFlags.InvisibleBlock;
        if((b3&64)!=0)t.Flags|=WorldTileFlags.InvisibleWall;
        if((b3&128)!=0)t.Flags|=WorldTileFlags.FullbrightBlock;
        Assert.True(t.TrySetSourceShape(cell.GetProperty("half").GetBoolean(),cell.GetProperty("slope").GetByte()));return t;
    }
}
