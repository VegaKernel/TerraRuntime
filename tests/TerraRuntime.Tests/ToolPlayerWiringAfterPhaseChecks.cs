using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;
using Xunit;

namespace TerraRuntime.Tests.ToolPlayer;

public sealed class OwnedWiringPostPhase1458Tests
{
    [Fact]
    public void Post_phase_wire_report_waits_for_its_paid_resource_tail_and_commits_without_a_second_phase() =>
        ToolPlayerBoundaryChecks.WiringAfterPhase();

    [Fact]
    public void Prior_raw_inventory_report_is_not_a_later_wiring_payment() => ToolPlayerBoundaryChecks.WiringInventoryBeforeSignal();

    [Fact]
    public void Missing_or_wrong_quantity_inventory_tail_cannot_spend_or_install() => ToolPlayerBoundaryChecks.WiringMissingAndWrongTail();

    [Fact]
    public void Reused_slot_rejects_old_connection_generation_and_its_inventory_tail() => ToolPlayerBoundaryChecks.WiringOldGeneration();

    [Fact]
    public void Wiring_remains_allowed_inside_the_owned_noBuilding_arena() => ToolPlayerBoundaryChecks.WiringNoBuilding();

    [Fact]
    public void Wrench_reach_includes_its_twenty_tile_boost_and_rejects_the_next_tile() => ToolPlayerBoundaryChecks.WiringReachBoundary();

    [Fact]
    public void Inventory_publication_observes_joint_adoption_and_reentry_cannot_spend_a_second_unit() =>
        ToolPlayerBoundaryChecks.WiringPublicationReentry();
}

internal static partial class ToolPlayerBoundaryChecks
{
    internal static void WiringAfterPhase()
    {
        using var f = WiringScene(509);
        WiringStock(f, 3, 530, 2);
        f.Tick();
        var before = f.CaptureInventory();
        using var transport = new PlacementTransport(f);
        transport.Post(Wiring17(5));
        Assert.Equal(WorldTileFlags.None, f.Tiles.Get(50, 50).Flags);
        f.AssertInventory(before);
        transport.Post(Wiring5(f, 3, 530, 1));
        Assert.Equal(WorldTileFlags.WireRed, f.Tiles.Get(50, 50).Flags);
        Assert.Equal(1UL, f.Tools.ActionSerial);
        Assert.Equal(5, f.Tools.Clocks.Selected.ItemTime);
        Assert.Equal((short)1, f.CaptureInventory().Items[3].Stack);
        Assert.Equal((short)1, f.CaptureInventory().Quantity[3]);
    }

    internal static void WiringInventoryBeforeSignal()
    {
        using var f = WiringScene(509);
        WiringStock(f, 3, 530, 2);
        f.Tick();
        using var transport = new PlacementTransport(f);
        transport.Post(Wiring5(f, 3, 530, 1));
        // An ordinary earlier inventory report may lower its physical projection. It cannot
        // be reused as a receipt for a later tile proposal or spend another unit of funding.
        var afterReport = f.CaptureInventory();
        transport.Post(Wiring17(5));
        f.Tick();
        Assert.Equal(WorldTileFlags.None, f.Tiles.Get(50, 50).Flags);
        Assert.Equal(0UL, f.Tools.ActionSerial);
        f.AssertInventory(afterReport);
    }

    internal static void WiringMissingAndWrongTail()
    {
        foreach (bool wrongTail in new[] { false, true })
        {
            using var f = WiringScene(509);
            WiringStock(f, 3, 530, 2);
            var before = f.CaptureInventory();
            using var transport = new PlacementTransport(f);
            transport.Post(Wiring17(5));
            if (wrongTail) transport.Post(Wiring5(f, 3, 530, 0));
            f.Tick();
            Assert.Equal(WorldTileFlags.None, f.Tiles.Get(50, 50).Flags);
            Assert.Equal(0UL, f.Tools.ActionSerial);
            f.AssertInventory(before);
        }
    }

    internal static void WiringOldGeneration()
    {
        using var f = WiringScene(509);
        var stale = f.Connection;
        f.Replace();
        Assert.Equal(stale.Player.Slot, f.Connection.Player.Slot);
        Assert.NotEqual(stale.Player.Generation, f.Connection.Player.Generation);
        WiringStock(f, 3, 530, 2);
        var before = f.CaptureInventory();
        f.Apply(new ClientTileManipulationRuntimeCommand(stale, new TerrariaTileManipulationState(5, 50, 50, 0, 0)));
        f.Apply(new PlayerEquipmentRuntimeCommand(stale, new(stale.Player.Slot, 3, 1, 0, 530, 0)));
        f.Tick();
        Assert.Equal(WorldTileFlags.None, f.Tiles.Get(50, 50).Flags);
        Assert.Equal(0UL, f.Tools.ActionSerial);
        f.AssertInventory(before);
    }

    internal static void WiringNoBuilding()
    {
        using var f = WiringScene(509);
        WiringStock(f, 3, 530, 2);
        var world = Composition(f.State).WorldTileAuthority;
        Assert.True(world.TryCaptureToolBuildingPermission(f.Member.PositionX + 10, f.Member.PositionY + 21, out var permission));
        Assert.NotNull(permission.Owner);
        Assert.True(permission.Owner!.TrySetSourceEvent(true, new System.Drawing.Rectangle(0, 0, 6400, 4800)));
        f.Tick();
        Assert.True(f.Tools.BuildingPermission?.NoBuilding == true);
        using var transport = new PlacementTransport(f);
        transport.Post(Wiring17(5));
        transport.Post(Wiring5(f, 3, 530, 1));
        Assert.Equal(WorldTileFlags.WireRed, f.Tiles.Get(50, 50).Flags);
        Assert.Equal(1UL, f.Tools.ActionSerial);
        Assert.Equal((short)1, f.CaptureInventory().Quantity[3]);
    }

    internal static void WiringReachBoundary()
    {
        // Source Simple rectangle at X736,width20 reaches floor-left46 and ceil-right47,
        // then adds the base five tiles and wrench boost twenty: right endpoint72 is inclusive.
        foreach (short target in new short[] { 72, 73 })
        {
            using var f = WiringScene(509);
            WiringStock(f, 3, 530, 2);
            var before = f.CaptureInventory();
            using var transport = new PlacementTransport(f);
            transport.Post(Wiring17(5, target, 50));
            transport.Post(Wiring5(f, 3, 530, 1));
            f.Tick();
            Assert.Equal(target == 72 ? WorldTileFlags.WireRed : WorldTileFlags.None, f.Tiles.Get(target, 50).Flags);
            Assert.Equal(target == 72 ? 1UL : 0UL, f.Tools.ActionSerial);
            if (target == 73) f.AssertInventory(before);
            else Assert.Equal((short)1, f.CaptureInventory().Quantity[3]);
        }
    }

    internal static void WiringPublicationReentry()
    {
        using var f = WiringScene(509);
        WiringStock(f, 3, 530, 2);
        f.Tick();
        using var transport = new PlacementTransport(f);
        bool observed = false;
        f.Events.OnPlacementEquipment = report =>
        {
            if (report.SlotId != 3) return;
            f.Events.OnPlacementEquipment = null;
            observed = true;
            Assert.Equal(WorldTileFlags.WireRed, f.Tiles.Get(50, 50).Flags);
            Assert.Equal(1UL, f.Tools.ActionSerial);
            Assert.Equal(5, f.Tools.Clocks.Selected.ItemTime);
            Assert.Equal((short)1, f.CaptureInventory().Quantity[3]);
            transport.Post(Wiring17(5, 51, 50));
            transport.Post(Wiring5(f, 3, 530, 0));
        };
        transport.Post(Wiring17(5));
        transport.Post(Wiring5(f, 3, 530, 1));
        Assert.True(observed);
        Assert.Equal(WorldTileFlags.None, f.Tiles.Get(51, 50).Flags);
        Assert.Equal(1UL, f.Tools.ActionSerial);
        Assert.Equal((short)1, f.CaptureInventory().Items[3].Stack);
        Assert.Equal((short)1, f.CaptureInventory().Quantity[3]);
    }
}
