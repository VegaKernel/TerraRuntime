using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.Tests.Larva1458;
using TerraRuntime.World;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeLarvaQueenBeeTests
{
    [Fact]
    public void Pick_breaks_complete_generated_larva_and_spawns_one_authoritative_queen_bee()
    {
        using var fixture = new Fixture();
        fixture.PlaceLarva(108, 100);
        fixture.Use();
        var inventory = fixture.CaptureInventory();
        for (int hit = 0; hit < 3; hit++)
        {
            fixture.Report(109, 101, fail: hit < 2);
            Assert.Equal(hit + 1, fixture.Source.Composition.WorldTileAuthority.CommittedToolActions);
            fixture.AssertInventory(inventory);
            if (hit < 2)
            {
                fixture.AssertLarvaPresent(108, 100);
                Assert.Equal(0, fixture.State.AppliedNpcSpawns);
                Assert.Equal(0, fixture.Npcs.ActiveCount);
                fixture.WaitForNextAttempt();
            }
        }
        fixture.AssertLarvaCleared(108, 100);
        Assert.Equal(3, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(0, fixture.State.RejectedClientTileManipulations);
        Assert.Equal(1, fixture.State.AppliedNpcSpawns);
        Assert.Equal(1, fixture.Npcs.ActiveCount);
        Span<NpcSnapshot> active = stackalloc NpcSnapshot[1];
        Assert.Equal(1, fixture.Npcs.CopyActive(active));
        Assert.Equal(VanillaNpcIds.QueenBee, active[0].TypeIdentity);
        Assert.Equal(fixture.Connection.Player.Slot.Value, active[0].Target);
        Assert.Equal(18740, active[0].Simulation.TimeLeft);
        Assert.Equal(3UL, fixture.Source.Member.ItemPhase!.Value.ToolUse!.ActionSerial);
        Assert.Equal(new byte[] { 23, 82 }, fixture.NpcFrames().Select(frame => frame[2]).ToArray());
    }

    [Fact]
    public void Malformed_larva_is_fail_closed_and_cannot_spawn_a_boss()
    {
        using var fixture = new Fixture();
        fixture.PlaceLarva(108, 100);
        WorldTile corrupt = fixture.Tiles.Get(110, 102);
        corrupt.FrameY = 18;
        fixture.Tiles.Set(110, 102, in corrupt);
        fixture.Use();
        var image = fixture.CaptureTiles(108, 100);
        var inventory = fixture.CaptureInventory();
        var random = fixture.Source.Random.Clone();
        var localRandom = LocalRandom(fixture.Source.Players)!.Clone();
        var clocks = fixture.Source.Member.ItemPhase!.Value.ToolUse!.Clocks;
        fixture.Report(109, 101, fail: true);
        fixture.AssertTiles(108, 100, image);
        fixture.AssertInventory(inventory);
        Assert.True(fixture.Source.Random.HasSameState(random));
        Assert.True(LocalRandom(fixture.Source.Players)!.HasSameState(localRandom));
        Assert.Equal(clocks, fixture.Source.Member.ItemPhase!.Value.ToolUse!.Clocks);
        Assert.Equal(0, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(0, fixture.Source.Composition.WorldTileAuthority.CommittedToolActions);
        Assert.Equal(0, fixture.State.AppliedNpcSpawns);
        Assert.Equal(0, fixture.Npcs.ActiveCount);
        Assert.Empty(fixture.NpcFrames());
        fixture.CloseRefusedCohort(109, 101);
        fixture.AssertTiles(108, 100, image);
        Assert.Equal(0, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(0, fixture.State.AppliedNpcSpawns);
        Assert.Equal(0, fixture.Npcs.ActiveCount);
        Assert.Empty(fixture.NpcFrames());
    }

    [Fact]
    public void Far_manual_tile_claim_is_refused_without_clearing_larva_or_spawning_boss()
    {
        using var fixture = new Fixture();
        fixture.PlaceLarva(400, 100);
        fixture.Use();
        var image = fixture.CaptureTiles(400, 100);
        var inventory = fixture.CaptureInventory();
        var random = fixture.Source.Random.Clone();
        var localRandom = LocalRandom(fixture.Source.Players)!.Clone();
        var clocks = fixture.Source.Member.ItemPhase!.Value.ToolUse!.Clocks;
        fixture.Report(401, 101, fail: false);
        fixture.AssertTiles(400, 100, image);
        fixture.AssertInventory(inventory);
        Assert.True(fixture.Source.Random.HasSameState(random));
        Assert.True(LocalRandom(fixture.Source.Players)!.HasSameState(localRandom));
        Assert.Equal(clocks, fixture.Source.Member.ItemPhase!.Value.ToolUse!.Clocks);
        Assert.Equal(0, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(0, fixture.Source.Composition.WorldTileAuthority.CommittedToolActions);
        Assert.Equal(0, fixture.State.AppliedNpcSpawns);
        Assert.Equal(0, fixture.Npcs.ActiveCount);
        Assert.Empty(fixture.NpcFrames());
        // The direct far WorldGen caller is independently covered by the64
        // official oracle rows; it does not authorize this out-of-reach manual17.
        fixture.CloseRefusedCohort(401, 101);
        fixture.AssertTiles(400, 100, image);
        Assert.Equal(0, fixture.State.AppliedClientTileManipulations);
        Assert.Equal(0, fixture.State.AppliedNpcSpawns);
        Assert.Equal(0, fixture.Npcs.ActiveCount);
        Assert.Empty(fixture.NpcFrames());
    }

    [Fact]
    public void Tile_definition_routes_larva_to_its_source_backed_object_path()
    {
        VanillaTileDefinition definition = VanillaTileDefinitionCatalog.Get(VanillaTileIds.Larva);
        Assert.True(definition.IsFrameImportant);
        Assert.Equal(VanillaTileBreakPath.LarvaObject, definition.BreakPath);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly LarvaRuntimeFixture1458 Source = new(false);
        internal ServerRuntimeState State => Source.State;
        internal RuntimeNpcStore Npcs => Source.Npcs;
        internal WorldTileStore Tiles => Source.Tiles;
        internal ConnectionHandle Connection => Source.Connection;
        internal Fixture()
        {
            for (int x = 0; x < 600; x++) Tiles.Set(x, 150, new WorldTile { Type = 0, Flags = WorldTileFlags.Active });
            Source.Apply(new PlayerInventorySyncCompleteRuntimeCommand(Connection)); State.Tick();
            Source.Apply(new PlayerEquipmentRuntimeCommand(Connection, new(Connection.Player.Slot, 0, 1, 0, 3509, 0)));
            Source.Apply(new PlayerInventorySyncCompleteRuntimeCommand(Connection));
            Source.Move(false); State.Tick();
        }
        internal void Use() { Source.Move(true); State.Tick(); }
        internal void PlaceLarva(int left, int top)
        {
            for (int x = 0; x < 3; x++) for (int y = 0; y < 3; y++)
                Tiles.Set(left + x, top + y, new WorldTile { Type = 231, Flags = WorldTileFlags.Active,
                    FrameX = (short)(x * 18), FrameY = (short)(y * 18) });
            for (int x = 0; x < 3; x++) Tiles.Set(left + x, top + 3, new WorldTile { Type = 0, Flags = WorldTileFlags.Active });
        }
        internal void Report(short x, short y, bool fail)
        {
            Source.Apply(new ClientTileManipulationRuntimeCommand(Connection, new(0, x, y, (short)(fail ? 1 : 0), 0)));
            if (fail) Source.Apply(new ClientTilePickingRuntimeCommand1458(Connection, new(Connection.Player.Slot.Value, x, y, 35)));
        }
        internal void WaitForNextAttempt()
        {
            int ticks = 0;
            do { State.Tick(); Assert.True(++ticks < 100); }
            while (Source.Member.ItemPhase!.Value.ToolUse!.Clocks.ToolTime > 0);
        }
        internal void CloseRefusedCohort(short x, short y)
        {
            Source.Move(false);
            Assert.Equal(1, State.RejectedClientTileManipulations);
            Assert.Null(Source.Member.ItemPhase!.Value.ToolUse!.Input);
            Assert.True(Queue(Source.TileOutbound).TryRead(out var correction));
            var bytes = correction.Bytes.Span;
            Assert.Equal(bytes.Length, BinaryPrimitives.ReadUInt16LittleEndian(bytes));
            Assert.Equal((byte)20, bytes[2]);
            Assert.Equal(x - 2, BinaryPrimitives.ReadInt16LittleEndian(bytes[3..]));
            Assert.Equal(y - 2, BinaryPrimitives.ReadInt16LittleEndian(bytes[5..]));
            Assert.Equal((byte)5, bytes[7]); Assert.Equal((byte)5, bytes[8]); Assert.Equal((byte)0, bytes[9]);
            int offset = 10;
            // Independent literal packet20 walk for this unpainted/unwired scene;
            // do not compare our encoder with itself to verify the correction.
            for (int tileX = x - 2; tileX <= x + 2; tileX++)
                for (int tileY = y - 2; tileY <= y + 2; tileY++)
                {
                    var tile = Tiles.Get(tileX, tileY);
                    Assert.Equal(tile.IsActive ? (byte)1 : (byte)0, bytes[offset++]);
                    Assert.Equal((byte)0, bytes[offset++]); Assert.Equal((byte)0, bytes[offset++]);
                    if (!tile.IsActive) continue;
                    Assert.Equal(tile.Type, BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..])); offset += 2;
                    if (tile.Type != 231) continue;
                    Assert.Equal(tile.FrameX, BinaryPrimitives.ReadInt16LittleEndian(bytes[offset..])); offset += 2;
                    Assert.Equal(tile.FrameY, BinaryPrimitives.ReadInt16LittleEndian(bytes[offset..])); offset += 2;
                }
            Assert.Equal(bytes.Length, offset);
            Assert.False(Queue(Source.TileOutbound).TryRead(out _));
            Source.Move(false);
            Assert.Equal(1, State.RejectedClientTileManipulations);
            Assert.False(Queue(Source.TileOutbound).TryRead(out _));
        }
        internal WorldTile[] CaptureTiles(int left, int top) => Enumerable.Range(0, 9).Select(i => Tiles.Get(left + i / 3, top + i % 3)).ToArray();
        internal void AssertTiles(int left, int top, WorldTile[] expected) => Assert.Equal(expected, CaptureTiles(left, top));
        internal void AssertLarvaPresent(int left, int top) { foreach (var tile in CaptureTiles(left, top)) Assert.True(tile.IsActive); }
        internal void AssertLarvaCleared(int left, int top) { foreach (var tile in CaptureTiles(left, top)) Assert.False(tile.IsActive); }
        internal InventoryImage CaptureInventory()
        {
            Assert.True(Inventory(Source.Players).TryCaptureShopJournal(Connection, out var capture)); Assert.NotNull(capture);
            return new(capture.Before.ToArray(), capture.Quantity.ToArray(), capture.Caps.ToArray(), capture.Copper.ToArray());
        }
        internal void AssertInventory(InventoryImage expected)
        {
            var actual = CaptureInventory(); Assert.Equal(expected.Items, actual.Items); Assert.Equal(expected.Quantity, actual.Quantity);
            Assert.Equal(expected.Caps, actual.Caps); Assert.Equal(expected.Copper, actual.Copper);
        }
        internal List<byte[]> NpcFrames()
        {
            var frames = new List<byte[]>(); while (Queue(Source.NpcOutbound).TryRead(out var frame)) frames.Add(frame.Bytes.ToArray()); return frames;
        }
        public void Dispose() => Source.Dispose();
    }
    private sealed record InventoryImage(RuntimePlayerInventoryItem[] Items, short[] Quantity, short[] Caps, long[] Copper);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inventory")] private static extern ref RuntimePlayerInventoryStore Inventory(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "playerUpdateRandom")] private static extern ref VanillaUnifiedRandom1458? LocalRandom(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_queue")] private static extern ref BoundedOutboundQueue Queue(TerrariaConnectionOutboundQueue owner);
}
