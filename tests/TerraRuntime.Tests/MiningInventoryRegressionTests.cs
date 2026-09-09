using System.Buffers;
using System.Buffers.Binary;
using System.Reflection;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class MiningInventoryRegressionTests
{
    [Fact]
    public void Shovel_target_set_matches_official_1458_and_has_no_ordinary_pick_power()
    {
        // Literal TileID.Sets.CanBeDugByShovel from the official decompile, not candidate enumeration.
        ushort[] admitted = [0,668,59,57,123,224,147,2,109,23,661,199,662,60,70,477,492,
            53,116,112,234,40,495,633,189,196,460,717,718,719];
        Assert.False(TerraRuntime.Gameplay.Items.VanillaPickToolCatalog1458.TryGetPickPower(new(4711), out _));
        for (int type = 0; type <= ushort.MaxValue; type++)
        {
            bool expected = Array.IndexOf(admitted, (ushort)type) >= 0;
            Assert.Equal(expected, TerraRuntime.Gameplay.Items.VanillaPickToolCatalog1458.TryGetTilePickPower(
                new(4711), new((ushort)type), out short power));
            Assert.Equal(expected ? 30 : 0, power);
        }
    }

    // Official 1.4.5.8 Item.SetDefaults results, not enumerated from the candidate catalog.
    private static readonly short[] Picks = [1,103,122,385,386,388,579,776,777,778,798,882,990,
        1188,1189,1195,1196,1202,1203,1230,1231,1294,1320,1506,1917,2176,2341,
        2774,2776,2779,2781,2784,2786,2798,3464,3466,3485,3491,3497,3503,3509,3515,3521,4059];

    [Fact]
    public void Shovel_client_three_by_three_burst_uses_normal_tile_and_drop_authority()
    {
        using var f = new Fixture(false);
        f.Equip(7, 4711, 1); // ItemCheck_UseMiningTools special tool, NOT an ordinary pick.
        f.Select(7);
        for (short x = 49; x <= 51; x++)
        for (short y = 51; y <= 53; y++)
        {
            Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(f.Tiles, x, y));
            f.Kill(x, y, 1); // Intermediate PickTile failure is relayed, not a completed break.
            Assert.True(f.Tiles.Get(x, y).IsActive);
            f.Kill(x, y);
            Assert.False(f.Tiles.Get(x, y).IsActive);
        }
        Assert.Equal(9, f.Items.ActiveCount);
        Assert.Equal(0, f.State.RejectedClientTileManipulations);
        Assert.Equal(18, f.State.AppliedClientTileManipulations);
    }

    [Theory]
    [InlineData(1)] [InlineData(41)] [InlineData(21)] [InlineData(65535)]
    public void Shovel_never_becomes_a_general_pick(ushort tile)
    {
        using var f = new Fixture(false);
        f.Equip(7, 4711, 1);
        f.Select(7);
        var before = new WorldTile { Type = tile, Flags = WorldTileFlags.Active };
        f.Tiles.Set(50, 52, before);
        f.Kill();
        Assert.Equal(before, f.Tiles.Get(50, 52));
        Assert.Equal(0, f.Items.ActiveCount);
        Assert.Equal(1, f.State.RejectedClientTileManipulations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void More_than_two_world_item_pools_of_mining_switching_tools_and_literal_pickups_keep_working(bool fullInventory)
    {
        using var f = new Fixture(fullInventory);
        for (int i = 0; i < 900; i++)
        {
            // Include cursor-held tools, repeated replacement of the same slot, hotbar switches and prefixes.
            byte selected = i % 3 == 0 ? (byte)58 : (byte)7;
            f.Equip(selected, Picks[i % Picks.Length], 1, (byte)(i % 2));
            f.Select(selected);
            Assert.True(f.State.TryCapturePlayerSnapshot(f.Player, out var player));
            Assert.Equal(selected, player.SelectedItem);
            Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(f.Tiles, 50, 52));
            f.Kill();
            Assert.False(f.Tiles.Get(50, 52).IsActive);
            Assert.Equal(1, f.Items.ActiveCount);
            Assert.Contains((byte)21, f.Drain());
            f.DiscoverOwner();
            Assert.True(f.Items.TryGetActive(0, out var item));
            Assert.Equal(f.Player.Slot.Value, item.OwnerPlayerId);
            Assert.Contains((byte)22, f.Drain());
            f.Remove(0);
            Assert.Equal(0, f.Items.ActiveCount);
            Assert.Equal(new byte[] { 151 }, f.Drain());
            // Ordinary vanilla client syncs the resulting inventory; no server-side fabricated GetItem.
            f.Equip(49, 2, checked((short)(i + 2)));
        }
        Assert.Equal(900, f.State.AppliedClientTileManipulations);
        Assert.Equal(900, f.State.AppliedWorldItemRemovals);
        Assert.Equal(0, f.State.RejectedClientTileManipulations);
        Assert.Equal(0, f.State.RejectedWorldItemAllocations);
        Assert.Equal(0, f.Replication.RejectedFrames);
    }

    // Direct official Player.ItemSpace fixtures: ordinary same-type/same-prefix partial stacks,
    // favorites, full stacks, cursor/coin slots, and existing ammo stacks.
    [Theory]
    [InlineData(2, 49, 9998, 0, 0, false, true)]
    [InlineData(2, 49, 9999, 0, 0, false, false)]
    [InlineData(2, 49, 9998, 1, 0, false, false)]
    [InlineData(2, 49, 9998, 0, 0, true, true)]
    [InlineData(3509, 49, 1, 0, 0, true, false)]
    [InlineData(2, 58, 9998, 0, 0, false, false)]
    [InlineData(2, 50, 9998, 0, 0, false, false)]
    [InlineData(47, 54, 9998, 0, 0, false, true)]
    [InlineData(2, 49, 9998, 1, 1, false, true)]
    public void Reservation_uses_source_itemspace_stack_boundaries(
        short type, short inventorySlot, short stack, byte slotPrefix, byte dropPrefix, bool favorite, bool expected)
    {
        using var f = new Fixture(fullInventory: true);
        f.Equip(49, 3509, 9999);
        f.Equip(inventorySlot, type, stack, slotPrefix, favorite);
        var drop = new WorldItemDropStateUpdate(800, 800, 0, 0, 1, dropPrefix,
            WorldItemOwnershipMode.None, type, false, 0, 0);
        Assert.True(f.Items.TryAllocateDrop(in drop, out var allocated));
        f.DiscoverOwner();
        Assert.True(f.Items.TryGetActive(allocated.Handle.Slot, out var item));
        Assert.Equal(expected ? f.Player.Slot.Value : byte.MaxValue, item.OwnerPlayerId);
    }

    [Fact]
    public void Missing_max_stack_remains_closed_and_nonowner_cannot_delete_or_release_a_lease()
    {
        using var f = new Fixture(fullInventory: true);
        f.Equip(49, 269, 1); // Sparse catalog has no maximum for this type.
        var drop = new WorldItemDropStateUpdate(800, 800, 0, 0, 1, 0,
            WorldItemOwnershipMode.None, 269, false, 0, 0);
        Assert.True(f.Items.TryAllocateDrop(in drop, out var original));
        f.DiscoverOwner();
        f.Remove(original.Handle.Slot);
        Assert.True(f.Items.TryGetActive(original.Handle.Slot, out var stillPresent));
        Assert.Equal(byte.MaxValue, stillPresent.OwnerPlayerId);
        Assert.Equal(1, f.State.RejectedWorldItemRemovals);
        Assert.True(f.Items.TryReserveDropSlot(out var lease));
        f.Remove(lease.Slot);
        Assert.True(f.Items.TryReleaseDropReservation(in lease));
    }

    [Fact]
    public void Previously_invisible_full_pool_recovers_when_owner_sends_compact_removal()
    {
        using var f = new Fixture(fullInventory: false);
        f.Equip(7, 3509, 1);
        f.Select(7);
        var drop = new WorldItemDropStateUpdate(800, 800, 0, 0, 1, 0,
            WorldItemOwnershipMode.None, 2, false, 0, 0);
        for (int i = 0; i < 400; i++)
        {
            Assert.True(f.Items.TryAllocateDrop(in drop, out _));
            f.Drain();
        }
        Assert.True(WorldTileTestMutations.TryPlaceDirtOnEmpty(f.Tiles, 50, 52));
        f.Kill();
        Assert.True(f.Tiles.Get(50, 52).IsActive); // No item loss / unbounded allocation to hide pressure.
        Assert.Equal(1, f.State.RejectedWorldItemAllocations);
        f.DiscoverOwner();
        f.Drain();
        f.Remove(0);
        Assert.Equal(399, f.Items.ActiveCount);
        f.Kill();
        Assert.False(f.Tiles.Get(50, 52).IsActive);
        Assert.True(f.Items.TryGetActive(0, out var replacement));
        Assert.Equal(2UL, replacement.Handle.Generation.Value);
        Assert.Equal(1, replacement.Stack);
    }

    [Theory]
    [InlineData(-1, 2)]
    [InlineData(400, 2)]
    [InlineData(0, 1)]
    [InlineData(0, 3)]
    public void Malformed_packet151_fails_closed(short slot, int length)
    {
        using var f = new Fixture(fullInventory: false);
        byte[] payload = new byte[length];
        if (length >= 2) BinaryPrimitives.WriteInt16LittleEndian(payload, slot);
        Assert.Equal(TerrariaFrameSinkResult.Stop, f.ItemSink.OnFrame(Frame(151, payload)));
        Assert.Equal(WorldItemFrameStopReason.MalformedRemoval, f.ItemSink.StopReason);
        Assert.Equal(0, f.State.AppliedWorldItemRemovals);
    }

    [Fact]
    public void Delayed_compact_pickup_cannot_remove_reused_item_generation_or_another_owners_item()
    {
        using var f = new Fixture(fullInventory: false);
        var drop = new WorldItemDropStateUpdate(800, 800, 0, 0, 1, 0,
            WorldItemOwnershipMode.None, 2, false, 0, 0);
        Assert.True(f.Items.TryAllocateDrop(in drop, out var first));
        f.DiscoverOwner();
        RuntimeCommand delayed = f.CaptureRemoval(first.Handle.Slot);
        f.Remove(first.Handle.Slot);
        Assert.True(f.Items.TryAllocateDrop(in drop, out var second));
        Assert.Equal(first.Handle.Slot, second.Handle.Slot);
        Assert.NotEqual(first.Handle.Generation, second.Handle.Generation);
        f.DiscoverOwner();
        f.State.Apply(delayed);
        Assert.True(f.Items.TryGetActive(second.Handle.Slot, out var present));
        Assert.Equal(second.Handle, present.Handle);
        var otherOwner = new WorldItemOwnerStateUpdate(1, 15, 255, 0, 800, 800);
        Assert.True(f.Items.TryApplyOwner(second.Handle.Slot, in otherOwner, out _));
        f.Remove(second.Handle.Slot);
        Assert.True(f.Items.TryGetActive(second.Handle.Slot, out _));
        Assert.Equal(2, f.State.RejectedWorldItemRemovals);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly PlayerBootstrapFrameSink bootstrap;
        private readonly ProjectileLifecycleFrameSink tileSink;
        private readonly TerrariaConnectionOutboundQueue outbound = new(
            new OutboundQueueOptions(maxFrames: 1024, maxQueuedBytes: 131072, maxFrameBytes: 2048));
        public WorldTileStore Tiles { get; } = new(new WorldDimensions(200, 150));
        public RuntimeWorldItemReplicationRegistry Replication { get; } = new();
        public RuntimeWorldItemStore Items { get; }
        public ServerRuntimeState State { get; }
        public WorldItemFrameSink ItemSink { get; }
        public PlayerHandle Player => bootstrap.AssignedPlayerHandle!.Value;

        public Fixture(bool fullInventory)
        {
            Items = new(Replication);
            State = new(playerEvents: Replication, worldItems: Items, worldTiles: Tiles, worldItemReplication: Replication);
            var source = GameCommandSourceId.FromConnection(9740);
            Assert.True(Replication.TryRegister(source, outbound));
            var ingress = new ApplyingIngress(State);
            bootstrap = new(new PlayerSlotPool(1), outbound,
                PlayerBootstrapPacketSet.CreateForTesting(new byte[] { 3, 0, 7 }, [], new byte[] { 3, 0, 49 }), source,
                new RuntimePlayerSpawnCommitIngress(ingress), appearanceIngress: null,
                new RuntimePlayerEquipmentIngress(ingress), new RuntimePlayerMovementIngress(ingress));
            tileSink = new(source, bootstrap, new Passthrough(), new RuntimeProjectileNetworkIngress(ingress));
            ItemSink = new(source, bootstrap, tileSink, new RuntimeWorldItemIngress(ingress, Items));
            Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Frame(1,
                [11, (byte)'T', (byte)'e', (byte)'r', (byte)'r', (byte)'a', (byte)'r', (byte)'i', (byte)'a', (byte)'3', (byte)'2', (byte)'6'])));
            // Every packet 5 precedes packet 6, as the real client does after receiving its slot.
            for (short slot = 0; slot < 59; slot++)
                Equip(slot, fullInventory ? (short)3509 : (short)0, fullInventory ? (short)9999 : (short)0);
            Equip(49, 2, 1);
            Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Frame(6, [])));
            Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Frame(8, new byte[9])));
            byte[] spawn = new byte[TerrariaJoinRequestDecoder.PlayerSpawnPayloadLength];
            BinaryPrimitives.WriteInt16LittleEndian(spawn.AsSpan(1), 50);
            BinaryPrimitives.WriteInt16LittleEndian(spawn.AsSpan(3), 52);
            Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Frame(12, spawn)));
            Assert.Equal(PlayerJoinState.Playing, bootstrap.JoinState);
            Select(7);
            Drain();
        }

        public void Equip(short slot, short type, short stack, byte prefix = 0, bool favorite = false)
        {
            byte[] payload = new byte[9];
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(1), slot);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(3), stack);
            payload[5] = prefix;
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(6), type);
            payload[8] = favorite ? (byte)1 : (byte)0;
            Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Frame(5, payload)));
            Assert.Equal(0, State.RejectedPlayerEquipmentUpdates);
        }

        public void Select(byte slot)
        {
            byte[] payload = new byte[14];
            payload[1] = 32;
            payload[2] = 16;
            payload[5] = slot;
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(6), 800);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(10), 800);
            Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Frame(13, payload)));
        }

        public void Kill(short x = 50, short y = 52, short fail = 0)
        {
            byte[] payload = new byte[8];
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(1), x);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(3), y);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(5), fail);
            Assert.Equal(TerrariaFrameSinkResult.Continue, tileSink.OnFrame(Frame(17, payload)));
        }

        public void Remove(short slot)
        {
            byte[] payload = new byte[2];
            BinaryPrimitives.WriteInt16LittleEndian(payload, slot);
            Assert.Equal(TerrariaFrameSinkResult.Continue, ItemSink.OnFrame(Frame(151, payload)));
        }

        public void DiscoverOwner() { for (int i = 0; i < 5; i++) State.Tick(); }

        public RuntimeCommand CaptureRemoval(short slot)
        {
            var capture = new CapturingIngress();
            var sink = new WorldItemFrameSink(GameCommandSourceId.FromConnection(9740), bootstrap,
                new Passthrough(), new RuntimeWorldItemIngress(capture, Items));
            byte[] payload = new byte[2];
            BinaryPrimitives.WriteInt16LittleEndian(payload, slot);
            Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Frame(151, payload)));
            return Assert.IsType<WorldItemRemoveRuntimeCommand>(capture.Command);
        }

        public byte[] Drain()
        {
            var queue = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
                .GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outbound)!;
            var ids = new List<byte>();
            while (queue.TryRead(out var frame)) ids.Add(frame.Bytes.Span[2]);
            return ids.ToArray();
        }

        public void Dispose() => bootstrap.Dispose();
    }

    private static TerrariaFrame Frame(byte id, byte[] payload) =>
        new(checked((ushort)(3 + payload.Length)), id, ReadOnlySequence<byte>.Empty, new ReadOnlySequence<byte>(payload));

    private sealed class ApplyingIngress(ServerRuntimeState state) : IGameCommandIngress<RuntimeCommand>
    {
        public bool TryPost(GameCommandSourceId source, RuntimeCommand command) { state.Apply(command); return true; }
    }
    private sealed class CapturingIngress : IGameCommandIngress<RuntimeCommand>
    {
        public RuntimeCommand? Command { get; private set; }
        public bool TryPost(GameCommandSourceId source, RuntimeCommand command) { Command = command; return true; }
    }
    private sealed class Passthrough : ITerrariaFrameSink
    {
        public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame) => TerrariaFrameSinkResult.Continue;
    }
}
