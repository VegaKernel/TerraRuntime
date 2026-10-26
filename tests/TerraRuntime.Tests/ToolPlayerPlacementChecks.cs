using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.World;
using Xunit;

namespace TerraRuntime.Tests.ToolPlayer;
internal static partial class ToolPlayerBoundaryChecks
{
    internal static void PlacementUnfundedBeforeFirstTick()
    {
        using var f = new Fixture(2);
        for (int x=102;x<=104;x++) for(int y=101;y<=102;y++) f.Tiles.Set(x,y,default);
        f.Tiles.Set(103,102,new WorldTile { Type=1,Flags=WorldTileFlags.Active });
        f.Apply(new PlayerEquipmentRuntimeCommand(f.Connection,new(f.Connection.Player.Slot,0,3,0,2,0)));
        var before=f.CaptureInventory();
        Assert.True(f.Players.TryGetInventoryItem(f.Connection,0,out var stock)); Assert.Equal((short)3,stock.Stack);
        f.Move(0,true); // actual input command; no extra Player.Update is invented.
        f.Apply(new ClientTileManipulationRuntimeCommand(f.Connection,new(1,102,102,0,0)));
        // Raw17 is a bounded placement observation. No paid source use has been
        // adopted; even this source-valid nearby cell must remain untouched.
        Assert.False(f.Tiles.Get(102,102).IsActive);
        f.AssertInventory(before);
        f.Tick();
        Assert.False(f.Tiles.Get(102,102).IsActive);
        f.AssertInventory(before);
    }
}
