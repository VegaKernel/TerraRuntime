using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Core;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests.ToolPlayer;

public sealed class OwnedMassWiring1458Tests
{
    public static IEnumerable<object[]> SourceRows()
    {
        foreach (var args in WiringMass1458Tests.OriginalRows())
        {
            using var json = JsonDocument.Parse((string)args[0]); var row = json.RootElement;
            if (row.GetProperty("stock").GetInt32() == 3 && row.GetProperty("scene").GetInt32() == 1) yield return args;
        }
    }

    [Theory, MemberData(nameof(SourceRows))]
    public void Authenticated_request_joint_inventory_terrain_cached_drops_rng_and_owner_payment_match_original(string row) =>
        ToolPlayerBoundaryChecks.MassWiringSource(row);

    [Fact]
    public void Funding_is_spent_across_slots_and_a_repeat_cannot_spend_or_duplicate_drops() => ToolPlayerBoundaryChecks.MassWiringFunding();

    [Fact]
    public void Stale_generation_cannot_cut_or_spend_and_over_budget_operation_refuses_whole() => ToolPlayerBoundaryChecks.MassWiringRefusal();
}

internal static partial class ToolPlayerBoundaryChecks
{
    internal static void MassWiringSource(string observation)
    {
        using var json = JsonDocument.Parse(observation); var row = json.RootElement;
        using var f = new Fixture(509, worldSeed: 1458);
        f.Member.PositionX = 400; f.Member.PositionY = 500;
        f.Member.ControlFlags = row.GetProperty("direction").GetInt32() == 1 ? (byte)64 : (byte)0;
        WiringStock(f, 3, 530, 3); WiringStock(f, 57, 849, 3);
        foreach (var cell in row.GetProperty("before").EnumerateArray())
        {
            var tile = WiringLogicGate1458Tests.Decode(cell.GetProperty("tile"));
            tile.Wall = cell.GetProperty("tile").GetProperty("wall").GetUInt16();
            f.Tiles.Set(cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32(), in tile);
        }
        var queue = new TerrariaConnectionOutboundQueue(new(8192, 4_194_304, 8192));
        Assert.True(f.TileReplication.TryRegister(f.Connection.Source, queue));
        f.TileReplication.PlayerSpawned(f.Connection, new(f.Connection.Player.Slot, 20, 20, 0, 0, 0, 0, 0));
        f.PlacementWorldRandom.CopyStateFrom(new VanillaUnifiedRandom1458(1458));
        using var transport = new PlacementTransport(f);
        transport.Post(TerrariaMassWiringCodec1458.Encode(new(row.GetProperty("sx").GetInt16(), row.GetProperty("sy").GetInt16(),
            row.GetProperty("ex").GetInt16(), row.GetProperty("ey").GetInt16(), row.GetProperty("mode").GetByte())));
        Assert.Equal(1, f.State.AppliedClientTileManipulations);
        var sourceFrames = row.GetProperty("frames").EnumerateArray().Select(v => Convert.FromHexString(v.GetString()!)).Where(v => v[2] is 17 or 110).ToArray();
        foreach (var frame in sourceFrames) if (frame[2] == 110) frame[7] = f.Connection.Player.Slot.Value;
        Assert.Equal(sourceFrames.Select(Convert.ToHexString), GemBunnyWire1458Tests.Drain(queue).Select(Convert.ToHexString));
        var payments = sourceFrames.Where(v => v[2] == 110).ToArray();
        int wirePaid = payments[0][5], actuatorPaid = payments[1][5];
        var inventory = f.CaptureInventory();
        Assert.Equal(3 - wirePaid, inventory.Items[3].Stack); Assert.Equal(3 - wirePaid, inventory.Quantity[3]);
        Assert.Equal(3 - actuatorPaid, inventory.Items[57].Stack); Assert.Equal(3 - actuatorPaid, inventory.Quantity[57]);
        var random = row.GetProperty("random");
        Assert.Equal(random.GetProperty("cursor").GetInt32(), Convert.ToInt32(typeof(VanillaUnifiedRandom1458).GetField("inext", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.PlacementWorldRandom)));
        Assert.Equal(random.GetProperty("state").EnumerateArray().Select(v => v.GetInt32()), (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.PlacementWorldRandom)!);
        var store = MassWiringItems(f);
        var drops = row.GetProperty("drops").EnumerateArray().ToArray(); Assert.Equal(drops.Length, store.ActiveCount);
        foreach (var drop in drops)
        {
            Assert.True(store.TryGetActive(drop.GetProperty("slot").GetInt16(), out var actual));
            Assert.Equal(drop.GetProperty("type").GetInt16(), actual.ItemNetId);
            Assert.Equal(drop.GetProperty("stack").GetInt16(), actual.Stack);
            Assert.Equal(drop.GetProperty("x").GetSingle(), actual.PositionX); Assert.Equal(drop.GetProperty("y").GetSingle(), actual.PositionY);
            Assert.Equal(drop.GetProperty("vx").GetSingle(), actual.VelocityX); Assert.Equal(drop.GetProperty("vy").GetSingle(), actual.VelocityY);
        }
    }

    internal static void MassWiringFunding()
    {
        using var f = WiringScene(509);
        WiringStock(f, 3, 530, 2); WiringStock(f, 4, 530, 2, funded: 1);
        var request = new TerrariaMassWiringState1458(50, 50, 53, 50, 1);
        f.Apply(new ClientMassWiringRuntimeCommand1458(f.Connection, request));
        Assert.Equal((short)0, f.CaptureInventory().Items[3].Stack);
        Assert.Equal((short)1, f.CaptureInventory().Items[4].Stack);
        Assert.Equal((short)0, f.CaptureInventory().Quantity[4]);
        for (int x = 50; x <= 52; x++) Assert.True((f.Tiles.Get(x, 50).Flags & WorldTileFlags.WireRed) != 0);
        Assert.Equal(WorldTileFlags.None, f.Tiles.Get(53, 50).Flags);
        var after = f.CaptureInventory();
        f.Apply(new ClientMassWiringRuntimeCommand1458(f.Connection, request)); f.AssertInventory(after);
        var cut = request with { Mode = 33 };
        f.Apply(new ClientMassWiringRuntimeCommand1458(f.Connection, cut));
        Assert.Equal(1, MassWiringItems(f).ActiveCount);
        Assert.True(MassWiringItems(f).TryGetActive(0, out var drop)); Assert.Equal((short)3, drop.Stack);
        f.Apply(new ClientMassWiringRuntimeCommand1458(f.Connection, cut));
        Assert.Equal(1, MassWiringItems(f).ActiveCount);
        Assert.True(MassWiringItems(f).TryGetActive(0, out var same)); Assert.Equal(drop, same);
    }

    internal static void MassWiringRefusal()
    {
        using var f = WiringScene(509);
        var stale = f.Connection; f.Replace(); WiringStock(f, 3, 530, 9999);
        var before = f.CaptureInventory(); var random = f.PlacementWorldRandom.Clone();
        f.Apply(new ClientMassWiringRuntimeCommand1458(stale, new(50, 50, 51, 50, 1)));
        f.Apply(new ClientMassWiringRuntimeCommand1458(f.Connection, new(short.MinValue, 50, short.MaxValue, 50, 1)));
        f.AssertInventory(before); Assert.True(f.PlacementWorldRandom.HasSameState(random));
        Assert.Equal(WorldTileFlags.None, f.Tiles.Get(50, 50).Flags);
        Assert.Equal(0, MassWiringItems(f).ActiveCount);
    }

    private static RuntimeWorldItemStore MassWiringItems(Fixture f) =>
        (RuntimeWorldItemStore)typeof(WorldItemAuthority).GetField("worldItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Composition(f.State).WorldItems)!;
}
