using System.Buffers.Binary;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;
using Xunit;

namespace TerraRuntime.Tests.ToolPlayer;

public sealed class OwnedWiringTools1458Tests
{
    [Fact]
    public void Four_wrenches_use_real_frame_ingress_paid_first_resource_and_independent_clocks() =>
        ToolPlayerBoundaryChecks.WiringInstallations();

    [Fact]
    public void Actuator_installation_in_air_spends_selected_stock_and_preserves_exhaustion_clock() =>
        ToolPlayerBoundaryChecks.WiringActuatorInstallation();

    [Fact]
    public void Raw_unfunded_stale_and_wrong_resource_reports_cannot_authorize_wiring() =>
        ToolPlayerBoundaryChecks.WiringRefusals();

    [Fact]
    public void Cutter_reports_remain_closed_without_an_owned_client_visual_random_projection() =>
        ToolPlayerBoundaryChecks.WiringCutter();

    [Fact]
    public void Paid_wire_connects_the_live_circuit_and_changes_the_same_replicated_world() =>
        ToolPlayerBoundaryChecks.WiringConnectedPulse();

    [Fact]
    public void Partially_funded_stock_spends_the_owned_unit_and_cannot_fund_its_reported_remainder() =>
        ToolPlayerBoundaryChecks.WiringPartialFunding();
}

internal static partial class ToolPlayerBoundaryChecks
{
    private static Fixture WiringScene(short item, short stock = 1, bool known = true)
    {
        var f = new Fixture(item, initialStack: stock, playerSeed: 7, worldSeed: 19, knownLocalContext: known);
        // Same ordinary movement/health/physics composition as the existing paid
        // placement fixture, now selecting a real wiring definition.
        f.Apply(new PlayerMovementRuntimeCommand(f.Connection, new(f.Connection.Player.Slot,
            96, 16, 0, 64, 0, 736, 806, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        f.Tiles.Set(50, 50, default);
        return f;
    }

    private static void WiringStock(Fixture f, short slot, short item, short stack, short? funded = null)
    {
        f.Apply(new PlayerEquipmentRuntimeCommand(f.Connection,
            new(f.Connection.Player.Slot, slot, stack, 0, item, 0)));
        PlacementFunding(Inventory(f.Players))[f.Connection.Player.Slot.Value * 59 + slot] = funded ?? stack;
    }

    private static byte[] Wiring17(byte action, short x = 50, short y = 50)
    {
        // Independent NetMessage17 layout: len,message,action,x,y,data,style.
        byte[] bytes = [11, 0, 17, action, 0, 0, 0, 0, 0, 0, 0];
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(4), x);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(6), y);
        return bytes;
    }

    private static byte[] Wiring5(Fixture f, short slot, short item, short stack)
    {
        byte[] bytes = [12, 0, 5, f.Connection.Player.Slot.Value, 0, 0, 0, 0, 0, 0, 0, 0];
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(4), slot);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(6), stack);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(9), stack == 0 ? (short)0 : item);
        return bytes;
    }

    internal static void WiringInstallations()
    {
        foreach (var (item, action, flag) in new (short, byte, WorldTileFlags)[] {
            (509,5,WorldTileFlags.WireRed), (850,10,WorldTileFlags.WireBlue),
            (851,12,WorldTileFlags.WireGreen), (3612,16,WorldTileFlags.WireYellow) })
        {
            using var f = WiringScene(item);
            WiringStock(f, 3, 530, 2); WiringStock(f, 4, 530, 7);
            var before = f.CaptureInventory();
            using var transport = new PlacementTransport(f);
            transport.Post(Wiring17(action));
            Assert.Equal(WorldTileFlags.None, f.Tiles.Get(50,50).Flags);
            transport.Post(Wiring5(f, 3, 530, 1));
            f.AssertInventory(before);
            f.Tick();
            Assert.Equal(flag, f.Tiles.Get(50,50).Flags);
            var after = f.CaptureInventory();
            Assert.Equal((short)1, after.Items[3].Stack);
            Assert.Equal((short)1, after.Quantity[3]);
            Assert.Equal(before.Items[4], after.Items[4]); Assert.Equal(before.Quantity[4], after.Quantity[4]);
            Assert.Equal(before.Items[0], after.Items[0]);
            Assert.Equal(1UL, f.Tools.ActionSerial);
            Assert.Equal(5, f.Tools.Clocks.Selected.ItemTime); Assert.Equal(5, f.Tools.Clocks.ToolTime);
            // A late exact inventory echo acknowledges the existing receipt,
            // rather than debit again or manufacture resource funding.
            transport.Post(Wiring5(f, 3, 530, 1));
            Assert.Equal((short)1, f.CaptureInventory().Quantity[3]);
            transport.Post(Wiring17(action));
            Assert.Equal(1UL, f.Tools.ActionSerial);
        }
    }

    internal static void WiringActuatorInstallation()
    {
        foreach (short stock in new short[] { 1, 2 })
        {
            using var f = WiringScene(849, stock);
            PlacementFunding(Inventory(f.Players))[f.Connection.Player.Slot.Value * 59] = stock;
            using var transport = new PlacementTransport(f);
            transport.Post(Wiring17(8)); transport.Post(Wiring5(f, 0, 849, (short)(stock - 1)));
            f.Tick();
            Assert.True(f.Tiles.Get(50,50).HasActuator); Assert.False(f.Tiles.Get(50,50).IsActive);
            Assert.Equal((short)(stock - 1), f.CaptureInventory().Quantity[0]);
            Assert.Equal((short)(stock - 1), f.CaptureInventory().Items[0].Stack);
            Assert.Equal(stock == 1 ? f.Tools.Clocks.Selected.Animation : 10, f.Tools.Clocks.Selected.ItemTime);
            Assert.Equal(10, f.Tools.Clocks.Selected.ItemTimeMax); Assert.Equal(10, f.Tools.Clocks.ToolTime);
            Assert.Equal(1UL, f.Tools.ActionSerial);
        }
    }

    internal static void WiringRefusals()
    {
        foreach (int refusal in Enumerable.Range(0, 6))
        {
            using var f = WiringScene(509, known: refusal != 5);
            WiringStock(f, 3, 530, 2, refusal == 0 ? (short)0 : (short)2);
            WiringStock(f, 4, 530, 3);
            using var transport = new PlacementTransport(f);
            var before = f.CaptureInventory();
            transport.Post(Wiring17(refusal == 1 ? (byte)10 : (byte)5));
            if (refusal == 2) f.Tiles.Set(50,50,new WorldTile { Wall = 1 });
            if (refusal != 3) transport.Post(Wiring5(f, refusal == 4 ? (short)4 : (short)3, 530,
                refusal == 4 ? (short)2 : (short)1));
            f.Tick();
            Assert.Equal(0UL, f.Member.ItemPhase?.ToolUse?.ActionSerial ?? 0UL);
            Assert.Equal(WorldTileFlags.None, f.Tiles.Get(50,50).Flags);
            if (refusal is 4 or 5)
            {
                // An unrelated ordinary inventory report keeps its established
                // receiver policy, but cannot pay for this pending wire.
                if (refusal == 4)
                {
                    Assert.Equal(before.Items[3], f.CaptureInventory().Items[3]);
                    Assert.Equal(before.Quantity[3], f.CaptureInventory().Quantity[3]);
                }
                Assert.Equal(before.Items[0], f.CaptureInventory().Items[0]);
            }
            else f.AssertInventory(before);
        }
    }

    internal static void WiringCutter()
    {
        using var f = WiringScene(510);
        var original = new WorldTile { Type = 1, Flags = WorldTileFlags.Active | WorldTileFlags.Inactive |
            WorldTileFlags.Actuator | WorldTileFlags.WireRed | WorldTileFlags.WireBlue |
            WorldTileFlags.WireGreen | WorldTileFlags.WireYellow, Wall = 4, LiquidAmount = 31,
            LiquidKind = WorldLiquidKind.Honey, TileColor = 5, FrameX = 36 };
        f.Tiles.Set(50,50,original);
        using var transport = new PlacementTransport(f);
        var before = f.CaptureInventory();
        transport.Post(Wiring17(17)); f.Tick(); // Wrong first removal cannot skip an actuator.
        Assert.Equal(original, f.Tiles.Get(50,50)); Assert.Equal(0UL, f.Member.ItemPhase?.ToolUse?.ActionSerial ?? 0UL);
        transport.Post(Wiring17(9)); f.Tick();
        Assert.Equal(original, f.Tiles.Get(50,50));
        Assert.Equal(0UL, f.Member.ItemPhase?.ToolUse?.ActionSerial ?? 0UL); f.AssertInventory(before);
        Assert.Equal(0, Composition(f.State).WorldTileAuthority.AppliedWorldItemAllocations);
    }

    internal static void WiringConnectedPulse()
    {
        using var f = WiringScene(509);
        WiringStock(f, 3, 530, 2);
        f.Tiles.Set(50,50,new WorldTile { Type = 136, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
        f.Tiles.Set(52,50,new WorldTile { Type = 4, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
        f.Apply(new ClientHitSwitchRuntimeCommand1458(f.Connection, new(50,50)));
        Assert.Equal((short)0, f.Tiles.Get(52,50).FrameX);
        using var transport = new PlacementTransport(f);
        transport.Post(Wiring17(5,51,50)); transport.Post(Wiring5(f,3,530,1)); f.Tick();
        Assert.Equal(WorldTileFlags.WireRed, f.Tiles.Get(51,50).Flags);
        f.Apply(new ClientHitSwitchRuntimeCommand1458(f.Connection, new(50,50)));
        Assert.Equal((short)66, f.Tiles.Get(52,50).FrameX);
        Assert.Equal((short)1, f.CaptureInventory().Quantity[3]);
        Assert.Equal(1UL, f.Tools.ActionSerial);
    }

    internal static void WiringPartialFunding()
    {
        using var f = WiringScene(509);
        WiringStock(f, 3, 530, 2, funded: 1);
        using var transport = new PlacementTransport(f);
        transport.Post(Wiring17(5)); transport.Post(Wiring5(f,3,530,1)); f.Tick();
        Assert.Equal(WorldTileFlags.WireRed, f.Tiles.Get(50,50).Flags);
        Assert.Equal((short)1, f.CaptureInventory().Items[3].Stack);
        Assert.Equal((short)0, f.CaptureInventory().Quantity[3]);
        for (int tick = 0; tick < 5; tick++) f.Tick();
        Assert.Equal(0, f.Tools.Clocks.Selected.ItemTime);
        transport.Post(Wiring17(5,51,50)); transport.Post(Wiring5(f,3,530,0));
        Assert.Equal(WorldTileFlags.None, f.Tiles.Get(51,50).Flags);
        Assert.Equal((short)1, f.CaptureInventory().Items[3].Stack);
        Assert.Equal((short)0, f.CaptureInventory().Quantity[3]);
        Assert.Equal(1UL, f.Tools.ActionSerial);
    }
}
