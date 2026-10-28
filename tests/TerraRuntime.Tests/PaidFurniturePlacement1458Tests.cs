using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.Tests.Larva1458;
using TerraRuntime.World;
using Xunit;

namespace TerraRuntime.Tests.PaidFurniture1458;

public sealed class PaidFurniturePlacement1458Tests
{
    [Fact]
    public void Paid_object_placement_owns_source_footprint_entity_ids_payment_and_checkpoint()
    {
        SourceRows(); EntityNetworkBytes(); IdLifecycle(); DeletedSlotOrder(); StateRows(); PublicationCallbacks(); AliasedStreams(); PendingPaymentTail(); CheckpointCycle();
    }

    private static JsonDocument Source(string kind, string platform)
    {
        using var input = typeof(PaidFurniturePlacement1458Tests).Assembly.GetManifestResourceStream("PaidFurniture" + kind + platform + "1458")
            ?? throw new InvalidOperationException("Missing original numeric furniture resource: " + kind + platform);
        using var gzip = new GZipStream(input, CompressionMode.Decompress); return JsonDocument.Parse(gzip);
    }
    private static void SourceRows()
    {
        foreach (var platform in new[] { "Linux", "Windows" })
        foreach (var kind in new[] { "ProducerRows", "OrdinaryRows" })
        {
            using var source = Source(kind, platform); Assert.Equal(112, source.RootElement.GetArrayLength());
            foreach (var row in source.RootElement.EnumerateArray())
            {
                using var f = new LarvaRuntimeFixture1458(platform == "Windows");
                Assert.True(f.Tiles.TryAttachTileEntities([], 100000));
                foreach (var cell in row.GetProperty("before").EnumerateArray()) f.Tiles.Set(cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32(), Cell(cell));
                Assert.True(VanillaLocalPlacementCatalog1458.TryGet(new(row.GetProperty("itemId").GetInt32()), out var definition));
                Assert.True(FurniturePlacementShape1458.TryGet(in definition, out var shape));
                var owner = f.Composition.WorldTileAuthority;
                Assert.True(owner.TryCaptureToolTreeWorld(out var context)); Assert.True(f.Players.TryCaptureToolTreeActors(out var actors));
                var overlay = new ToolWorldTileOverlay1458(f.Tiles); Assert.True(owner.TryCreateToolMutationPlan(overlay, out var plan));
                using (plan!)
                {
                    bool client = row.GetProperty("role").GetInt32() == 1;
                    var random = client ? ReadRandom(row.GetProperty("beforeRandom")) : plan!.SourceRandom;
                    Assert.True(random.HasSameState(ReadRandom(row.GetProperty("beforeRandom"))));
                    Assert.True(owner.TryCreateToolSourcePass(overlay, random, client, false, context!.Facts, actors!.Actors,
                        client ? null : plan, out var pass));
                    int x = row.GetProperty("clickX").GetInt32(), y = row.GetProperty("clickY").GetInt32();
                    Assert.True(pass!.TryPlaceFurniture(in shape, x, y, row.GetProperty("direction").GetInt32(), true,
                        out bool placed, out var report, out var footprint), platform + "/" + kind + "/" + row.GetProperty("itemId") + "/" + row.GetProperty("scenario"));
                    Assert.Equal(row.GetProperty("canPlace").GetBoolean(), placed);
                    if (placed && !client && shape.Kind is not null)
                        Assert.True(pass.TryCreateFurnitureEntity(in shape, x - shape.OriginX, y - shape.OriginY, out _));
                    SameCells(row.GetProperty("after"), overlay);
                    Assert.True(random.HasSameState(ReadRandom(row.GetProperty("afterRandom"))), "original placement fullMain RNG");
                    foreach (var encoded in row.GetProperty("frames").EnumerateArray())
                    {
                        var bytes = Convert.FromHexString(encoded.GetString()!);
                        if (bytes[2] == 20)
                        {
                            Assert.True(TerrariaTileSquareCodec.TryEncodeFurnitureFrame(footprint, x - shape.OriginX, y - shape.OriginY,
                                shape.Width, shape.Height, out var expected)); Assert.Equal(bytes, expected);
                        }
                        if (bytes[2] == 79)
                        { Assert.Equal(TerrariaPlaceObjectEncodeResult.Encoded, TerrariaPlaceObjectCodec.TryEncode(in report, out var expected)); Assert.Equal(bytes, expected); }
                    }
                    if (kind == "OrdinaryRows" && row.GetProperty("afterKill").ValueKind != JsonValueKind.Null)
                    {
                        Assert.True(pass.TryKillTile(50, 50, false, false, false, out _));
                        SameCells(row.GetProperty("afterKill").GetProperty("cells"), overlay);
                        Assert.True(random.HasSameState(ReadRandom(row.GetProperty("afterKill").GetProperty("random"))), "original ordinary removal fullMain RNG");
                    }
                }
            }
        }
    }
    private static void EntityNetworkBytes()
    {
        foreach (var platform in new[] { "Linux", "Windows" })
        {
            using var source = Source("NetworkRows", platform); Assert.Equal(34, source.RootElement.GetArrayLength());
            foreach (var row in source.RootElement.EnumerateArray())
            foreach (var stage in row.GetProperty("stages").EnumerateArray())
            {
                var input = Convert.FromHexString(stage.GetProperty("input").GetString()!);
                if (input[2] != 87) continue;
                var frame = Frame(input);
                Assert.Equal(TerrariaFurnitureEntityPlacementDecodeResult1458.Decoded,
                    TerrariaFurnitureEntityPlacementCodec1458.TryDecode(in frame, out var request));
                Assert.Equal(input, TerrariaFurnitureEntityPlacementCodec1458.Encode(in request));
                var expected = stage.GetProperty("frames").EnumerateArray().Select(e => Convert.FromHexString(e.GetString()!)).First(f => f[2] == 86);
                var kind = (WorldTileEntityKind)request.Kind;
                WorldTileEntityPayload payload = kind switch
                {
                    WorldTileEntityKind.DisplayDoll => new WorldDisplayDollPayload(0, new WorldTileEntityItem?[9], new WorldTileEntityItem?[9], null),
                    WorldTileEntityKind.HatRack => new WorldHatRackPayload(new WorldTileEntityItem?[2], new WorldTileEntityItem?[2]),
                    _ => new WorldItemTileEntityPayload(default)
                };
                Assert.True(TerrariaFurnitureEntityCodec1458.TryEncode(new(0, request.TileX, request.TileY, kind, payload), out var encoded));
                Assert.Equal(expected, encoded);
            }
        }
    }
    private static void IdLifecycle()
    {
        var tiles = new WorldTileStore(new(100, 100)); Assert.True(tiles.TryAttachTileEntities([], 16));
        var owner = tiles.RuntimeTileEntities!; Assert.True(owner.TryPrepare(16, out var first));
        foreach (int x in new[] { 46, 50, 54 }) Assert.True(first!.TryStageEmptyPlacement(x, 50, WorldTileEntityKind.DisplayDoll, out _));
        Assert.True(first!.TryStageRemoval(50, 50, WorldTileEntityKind.DisplayDoll)); Adopt(tiles, first);
        Assert.Equal(new[] { 0, 2 }, owner.CaptureSnapshot().Select(e => e.PersistedId));
        using var encoded = new MemoryStream();
        Assert.Equal(WorldFileTileEntityEncodeResult.Encoded, WorldFileTileEntityEncoder.TryEncode(owner.CaptureSnapshot(), tiles.Dimensions, 16, encoded, out _));
        foreach (var platform in new[] { "Linux", "Windows" })
        {
            using var source = Source("NetworkLifecycle", platform);
            Assert.Equal(Convert.FromHexString(source.RootElement[1].GetProperty("saved").GetString()!), encoded.ToArray());
        }
        var loaded = new WorldTileStore(new(100, 100)); Assert.True(loaded.TryAttachLoadedTileEntities(owner.CaptureSnapshot(), 16));
        Assert.Equal(new[] { 0, 1 }, loaded.RuntimeTileEntities!.CaptureSnapshot().Select(e => e.PersistedId));
        Assert.True(loaded.RuntimeTileEntities.TryPrepare(16, out var next)); Assert.True(next!.TryStageEmptyPlacement(50, 50, WorldTileEntityKind.DisplayDoll, out int nextId));
        Assert.Equal(2, nextId); Adopt(loaded, next);
        Assert.True(loaded.RuntimeTileEntities.TryPrepare(16, out var stale));
        Assert.True(loaded.RuntimeTileEntities.TryPrepare(16, out var winner)); Assert.True(winner!.TryStageRemoval(50, 50, WorldTileEntityKind.DisplayDoll)); Adopt(loaded, winner);
        Assert.False(stale!.IsCurrentOwned);
        Assert.True(loaded.RuntimeTileEntities.TryPrepare(16, out var cycle));
        Assert.True(cycle!.TryStageEmptyPlacement(50, 50, WorldTileEntityKind.DisplayDoll, out int id3)); Assert.Equal(3, id3);
        Assert.True(cycle.TryStageRemoval(50, 50, WorldTileEntityKind.DisplayDoll));
        Assert.True(cycle.TryStageEmptyPlacement(50, 50, WorldTileEntityKind.DisplayDoll, out int id4)); Assert.Equal(4, id4); Adopt(loaded, cycle);
        Assert.Contains(loaded.RuntimeTileEntities.CaptureSnapshot(), e => e.PersistedId == 4);
    }
    private static void Adopt(WorldTileStore tiles, RuntimeWorldFurnitureStore1458.Preparation preparation)
    {
        var overlay = new ToolWorldTileOverlay1458(tiles); Assert.True(overlay.TryPrepareMetadata(preparation)); Assert.True(overlay.TryAdoptUnpublished(preparation));
    }
    private static void DeletedSlotOrder()
    {
        foreach (var platform in new[] { "Linux", "Windows" })
        {
            using var source = Source("IdOrder", platform);
            var tiles = new WorldTileStore(new(100, 100)); Assert.True(tiles.TryAttachTileEntities([], 16));
            var owner = tiles.RuntimeTileEntities!; Assert.True(owner.TryPrepare(16, out var image));
            foreach (int x in new[] { 46, 50, 54 }) Assert.True(image!.TryStageEmptyPlacement(x, 50, WorldTileEntityKind.DisplayDoll, out _));
            Assert.True(image!.TryStageRemoval(50, 50, WorldTileEntityKind.DisplayDoll));
            Assert.True(image.TryStageEmptyPlacement(50, 50, WorldTileEntityKind.DisplayDoll, out int replacement)); Assert.Equal(3, replacement); Adopt(tiles, image);
            void Same(JsonElement row, RuntimeWorldFurnitureStore1458 store)
            {
                var expected = row.GetProperty("entities").EnumerateArray().Select(e => (e.GetProperty("id").GetInt32(), e.GetProperty("position").GetProperty("X").GetInt16())).ToArray();
                Assert.Equal(expected, store.CaptureSnapshot().Select(e => (e.PersistedId, e.X)).ToArray());
            }
            Same(source.RootElement[1], owner);
            using var bytes = new MemoryStream();
            Assert.Equal(WorldFileTileEntityEncodeResult.Encoded, WorldFileTileEntityEncoder.TryEncode(owner.CaptureSnapshot(), tiles.Dimensions, 16, bytes, out _));
            Assert.Equal(Convert.FromHexString(source.RootElement[2].GetProperty("saved").GetString()!), bytes.ToArray());
            var restored = new WorldTileStore(new(100, 100)); Assert.True(restored.TryAttachLoadedTileEntities(owner.CaptureSnapshot(), 16));
            owner = restored.RuntimeTileEntities!; Same(source.RootElement[2], owner);
            Assert.True(owner.TryPrepare(16, out image)); Assert.True(image!.TryStageEmptyPlacement(62, 50, WorldTileEntityKind.DisplayDoll, out int id)); Assert.Equal(3, id); Adopt(restored, image);
            Same(source.RootElement[3], owner);
            Assert.True(owner.TryPrepare(16, out image)); Assert.True(image!.TryStageRemoval(46, 50, WorldTileEntityKind.DisplayDoll)); Assert.True(image.TryStageRemoval(54, 50, WorldTileEntityKind.DisplayDoll));
            Assert.True(image.TryStageEmptyPlacement(46, 50, WorldTileEntityKind.DisplayDoll, out id)); Assert.Equal(4, id);
            Assert.True(image.TryStageEmptyPlacement(54, 50, WorldTileEntityKind.DisplayDoll, out id)); Assert.Equal(5, id); Adopt(restored, image);
            Same(source.RootElement[4], owner);
        }
    }

    private static void StateRows()
    {
        foreach (short item in new short[] { 498, 1989, 2699, 3977, 32, 34, 35, 36 })
        foreach (short funding in new short[] { 0, 1, 3 })
        {
            using var f = CreatePaidScene(item, funding, out var shape);
            int x = 102, y = 103 - shape.Height;
            var inventory = Inventory(f.Players);
            using var source = Source(shape.HasEntity ? "ProducerRows" : "OrdinaryRows", "Linux");
            var row = source.RootElement.EnumerateArray().First(r => r.GetProperty("itemId").GetInt32() == item && r.GetProperty("role").GetInt32() == 1 &&
                r.GetProperty("direction").GetInt32() == 1 && r.GetProperty("scenario").GetString() == "clear");
            foreach (var raw in row.GetProperty("frames").EnumerateArray())
            {
                var bytes = Convert.FromHexString(raw.GetString()!);
                int dx = x - 50, dy = y - 50;
                BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(3), checked((short)(BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(3)) + dx)));
                BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(5), checked((short)(BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(5)) + dy)));
                Post(f, bytes);
                Assert.Equal(0, f.Composition.WorldTileAuthority.CommittedToolActions);
                Assert.Empty(f.Tiles.RuntimeTileEntities!.CaptureSnapshot());
                Assert.False(f.Tiles.Get(x, y).IsActive);
            }
            var beforeRandom = f.Random.Clone(); var beforeClocks = f.Member.ItemPhase!.Value.ToolUse!.Clocks;
            f.Apply(new PlayerEquipmentRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, 0, 2, 0, item, 0)));
            if (funding == 0)
            {
                Assert.Equal(0, f.Composition.WorldTileAuthority.CommittedToolActions); Assert.False(f.Tiles.Get(x, y).IsActive);
                Assert.Empty(f.Tiles.RuntimeTileEntities!.CaptureSnapshot()); Assert.True(f.Random.HasSameState(beforeRandom));
                Assert.Equal(beforeClocks, f.Member.ItemPhase!.Value.ToolUse!.Clocks);
                Assert.True(inventory.TryGet(f.Connection, 0, out var unchanged)); Assert.Equal(3, unchanged.Stack);
                Assert.Equal(funding, inventory.CaptureFundedQuantities(f.Connection)[0]);
                long rejected = f.Composition.WorldTileAuthority.RejectedToolReports;
                f.Move(false); Assert.Equal(rejected + 1, f.Composition.WorldTileAuthority.RejectedToolReports);
                Assert.Null(f.Member.ItemPhase!.Value.ToolUse!.Input); Assert.True(f.Random.HasSameState(beforeRandom));
                Assert.Equal(beforeClocks, f.Member.ItemPhase!.Value.ToolUse!.Clocks);
                f.Move(false); Assert.Equal(rejected + 1, f.Composition.WorldTileAuthority.RejectedToolReports);
                continue;
            }
            Assert.Equal(1, f.Composition.WorldTileAuthority.CommittedToolActions);
            Assert.True(inventory.TryGet(f.Connection, 0, out var remaining)); Assert.Equal(2, remaining.Stack);
            Assert.Equal((short)(funding - 1), inventory.CaptureFundedQuantities(f.Connection)[0]);
            for (int tx = 0; tx < shape.Width; tx++) for (int ty = 0; ty < shape.Height; ty++)
            { var tile = f.Tiles.Get(x + tx, y + ty); Assert.True(tile.IsActive); Assert.Equal(shape.Tile, tile.Type); }
            if (shape.HasEntity)
            { var created = Assert.Single(f.Tiles.RuntimeTileEntities!.CaptureSnapshot()); Assert.Equal(0, created.PersistedId); Assert.Equal(shape.Kind, created.Kind); }
            else Assert.Empty(f.Tiles.RuntimeTileEntities!.CaptureSnapshot());
            var serial = f.Member.ItemPhase!.Value.ToolUse!.ActionSerial;
            // A standalone87 and replayed inventory ACK cannot allocate/fund a second object.
            f.Apply(new ClientFurniturePlacementEntityCommand1458(f.Connection, new((short)x, (short)y, (byte)(shape.Kind ?? WorldTileEntityKind.DisplayDoll))));
            f.Apply(new PlayerEquipmentRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, 0, 2, 0, item, 0)));
            Assert.Equal(serial, f.Member.ItemPhase!.Value.ToolUse!.ActionSerial);
            Assert.Equal((short)(funding - 1), inventory.CaptureFundedQuantities(f.Connection)[0]);
            if (funding == 1) UseAndMine(f, item, shape, x, y);
        }
    }

    private static LarvaRuntimeFixture1458 CreatePaidScene(short item, short funding, out FurniturePlacementShape1458 shape,
        bool alias = false, Action<LarvaRuntimeFixture1458, FurniturePlacementShape1458>? beforePhase = null)
    {
    var f = new LarvaRuntimeFixture1458(OperatingSystem.IsWindows(), aliasPlayerRandom: alias);
        Assert.True(f.Tiles.TryAttachTileEntities([], 100000));
        Assert.True(VanillaLocalPlacementCatalog1458.TryGet(new(item), out var definition));
        Assert.True(FurniturePlacementShape1458.TryGet(in definition, out shape));
        int x = 102, y = 103 - shape.Height;
        if (shape.NeedsWall)
            for (int dx = 0; dx < shape.Width; dx++) for (int dy = 0; dy < shape.Height; dy++)
                f.Tiles.Set(x + dx, y + dy, new WorldTile { Wall = 1 });
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection)); f.State.Tick();
        f.Apply(new PlayerEquipmentRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, 0, 3, 0, item, 0)));
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection));
        var inventory = Inventory(f.Players); var q = inventory.CaptureFundedQuantities(f.Connection); q[0] = funding; inventory.AdoptFundedQuantities(f.Connection, q);
        beforePhase?.Invoke(f, shape);
        f.Move(false); f.State.Tick(); f.State.Tick(); f.Move(true); f.State.Tick();
        Assert.True(f.Member.ItemPhase!.Value.ToolUse is { LocalClocksKnown: true, SourcePlacement: not null, SourceStagePose: not null });
        return f;
    }
    private static void PlaceReports(LarvaRuntimeFixture1458 f, short item, in FurniturePlacementShape1458 shape)
    {
        using var source = Source(shape.HasEntity ? "ProducerRows" : "OrdinaryRows", "Linux");
        var row = source.RootElement.EnumerateArray().First(r => r.GetProperty("itemId").GetInt32() == item && r.GetProperty("role").GetInt32() == 1 &&
            r.GetProperty("direction").GetInt32() == 1 && r.GetProperty("scenario").GetString() == "clear");
        foreach (var raw in row.GetProperty("frames").EnumerateArray())
        {
            var bytes = Convert.FromHexString(raw.GetString()!);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(3), checked((short)(BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(3)) + 52)));
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(5), checked((short)(BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(5)) + 53 - shape.Height)));
            Post(f, bytes);
        }
    }
    private static void MoveSelected(LarvaRuntimeFixture1458 f, byte slot, bool use)
    {
        var member = f.Member;
        f.Apply(new PlayerMovementRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, use ? (byte)96 : (byte)64,
            (byte)(member.MovementFlags | 16), 0, 64, slot, member.PositionX, member.PositionY,
            false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
    }
    private static void StartTool(LarvaRuntimeFixture1458 f, short item)
    {
        MoveSelected(f, 0, false);
        int wait = 120;
        while (f.Member.ItemPhase!.Value.ToolUse!.Clocks is { } clocks &&
            (clocks.Selected.Animation > 0 || clocks.Selected.ItemTime > 0 || clocks.ToolTime > 0))
        { Assert.True(wait-- > 0); f.State.Tick(); }
        f.Apply(new PlayerEquipmentRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, 1, 1, 0, item, 0)));
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection));
        MoveSelected(f, 1, false); f.State.Tick(); f.State.Tick(); MoveSelected(f, 1, true); f.State.Tick();
    }
    private static void UseAndMine(LarvaRuntimeFixture1458 f, short item, in FurniturePlacementShape1458 shape, int x, int y)
    {
        var inventory = Inventory(f.Players); ulong beforeSerial = f.Member.ItemPhase!.Value.ToolUse!.ActionSerial;
        if (shape.Tile == 470)
        {
            StartTool(f, 7);
            Post(f, TerrariaDollPoseCodec1458.Encode(new(f.Connection.Player.Slot.Value, 0, 1, 1)));
            Assert.Equal((byte)1, Assert.IsType<WorldDisplayDollPayload>(Assert.Single(f.Tiles.RuntimeTileEntities!.CaptureSnapshot()).Payload).Pose);
            Assert.Equal(beforeSerial + 1, f.Member.ItemPhase!.Value.ToolUse!.ActionSerial);
        }
        StartTool(f, 3509); ulong miningSerial = f.Member.ItemPhase!.Value.ToolUse!.ActionSerial;
        for (int hit = 0; hit < 3; hit++)
        {
            var report = new TerrariaTileManipulationState(shape.Tile is 470 or 475 && hit == 2 ? (byte)20 : (byte)0,
                (short)x, (short)y, hit < 2 ? (short)1 : (short)0, 0);
            Assert.Equal(TerrariaTileManipulationEncodeResult.Encoded, TerrariaTileManipulationCodec.TryEncode(in report, out var bytes)); Post(f, bytes);
            if (hit < 2) Post(f, TerrariaTilePickingCodec1458.Encode(new(f.Connection.Player.Slot.Value, (short)x, (short)y, 35)));
            Assert.Equal(miningSerial + (ulong)hit + 1, f.Member.ItemPhase!.Value.ToolUse!.ActionSerial);
            if (hit < 2)
            {
                int remaining = 60;
                do { f.State.Tick(); Assert.True(remaining-- > 0); } while (f.Member.ItemPhase!.Value.ToolUse!.Clocks.ToolTime > 0);
            }
        }
        for (int dx = 0; dx < shape.Width; dx++) for (int dy = 0; dy < shape.Height; dy++) Assert.False(f.Tiles.Get(x + dx, y + dy).IsActive);
        Assert.Empty(f.Tiles.RuntimeTileEntities!.CaptureSnapshot()); Assert.Equal(1, f.Items.ActiveCount);
        var drops = new WorldItemSnapshot[400]; int count = f.Items.CopyActive(drops);
        Assert.Equal(1, count); Assert.Equal(item, drops[0].ItemNetId);
        Assert.True(inventory.TryGet(f.Connection, 0, out var remainingItem)); Assert.Equal(item, remainingItem.ItemType.Value); Assert.Equal(2, remainingItem.Stack);
        Assert.Equal((short)0, inventory.CaptureFundedQuantities(f.Connection)[0]);
    }
    private sealed class ExpectedPublicationException : Exception;
    private sealed class ItemObserver(IWorldItemStateCommitSink? inner, Action<WorldItemStateCommitKind, WorldItemSnapshot> callback) : IWorldItemStateCommitSink
    {
        public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot item, int? reservationOffer = null, GameCommandSourceId excludedSource = default)
        { callback(kind, item); inner?.WorldItemStateCommitted(kind, in item, reservationOffer, excludedSource); }
    }
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_queue")] private static extern ref BoundedOutboundQueue Queue(TerrariaConnectionOutboundQueue owner);
    private static byte[][] Drain(TerrariaConnectionOutboundQueue queue)
    { var frames = new List<byte[]>(); while (Queue(queue).TryRead(out var frame)) frames.Add(frame.Bytes.ToArray()); return frames.ToArray(); }
    private static void PublicationCallbacks()
    {
        foreach (bool changeMetadata in new[] { false, true })
        {
            using var f = CreatePaidScene(498, 3, out var shape, beforePhase: (scene, _) =>
            {
                // This unsupported-ground table lies in both frame stencils but
                // outside the placed object/player body. Its source Check3x2
                // drop gives a genuine pre20 publication callback boundary.
                for (int dx = 0; dx < 3; dx++) for (int dy = 0; dy < 2; dy++)
                    scene.Tiles.Set(99 + dx, 100 + dy, new WorldTile { Type = 14, Flags = WorldTileFlags.Active, FrameX = (short)(dx * 18), FrameY = (short)(dy * 18) });
            });
            var peer = new ConnectionHandle(GameCommandSourceId.FromConnection(99123), new(new(0), new(1)));
            var peerQueue = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(1024, 262144, 1024));
            Assert.True(f.TileReplication.TryRegister(peer.Source, peerQueue));
            var spawn = new PlayerSpawnCommitRequest(peer.Player.Slot, 100, 103, 0, 0, 0, 0, 0); f.TileReplication.PlayerSpawned(peer, in spawn);
            Drain(f.TileOutbound); Drain(peerQueue);
            int observed = 0; var inner = ItemSink(f.Items);
            ItemSink(f.Items) = new ItemObserver(inner, (kind, item) =>
            {
                if (kind != WorldItemStateCommitKind.Drop) return;
                observed++;
                Assert.Equal(1, f.Composition.WorldTileAuthority.CommittedToolActions);
                Assert.True(Inventory(f.Players).TryGet(f.Connection, 0, out var stock)); Assert.Equal(2, stock.Stack);
                Assert.Equal((short)2, Inventory(f.Players).CaptureFundedQuantities(f.Connection)[0]);
                var metadata = f.Tiles.RuntimeTileEntities!; Assert.Equal(0, Assert.Single(metadata.CaptureSnapshot()).PersistedId);
                for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 3; dy++) Assert.Equal((ushort)470, f.Tiles.Get(102 + dx, 100 + dy).Type);
                f.Apply(new ClientFurniturePlacementEntityCommand1458(f.Connection, new(102, 100, 3)));
                Assert.Equal(1, f.Composition.WorldTileAuthority.CommittedToolActions);
                if (changeMetadata)
                {
                    Assert.True(metadata.TryPrepare(1, out var pose)); Assert.True(pose!.TryStageDisplayDollPose(102, 100, 0, 0, 1)); Adopt(f.Tiles, pose);
                }
                else throw new ExpectedPublicationException();
            });
            PlaceReports(f, 498, in shape);
            var payment = new PlayerEquipmentRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, 0, 2, 0, 498, 0));
            if (changeMetadata) f.Apply(payment); else Assert.Throws<ExpectedPublicationException>(() => f.Apply(payment));
            Assert.Equal(1, observed); Assert.Equal(1, f.Composition.WorldTileAuthority.CommittedToolActions);
            var source = Drain(f.TileOutbound).Where(frame => frame[2] is 20 or 86).ToArray();
            var peers = Drain(peerQueue).Where(frame => frame[2] is 20 or 86).ToArray();
            if (changeMetadata)
            {
                Assert.Empty(source); Assert.Empty(peers);
                Assert.Equal((byte)1, Assert.IsType<WorldDisplayDollPayload>(Assert.Single(f.Tiles.RuntimeTileEntities!.CaptureSnapshot()).Payload).Pose);
            }
            else { Assert.Equal(new byte[] { 86 }, source.Select(frame => frame[2])); Assert.Equal(new byte[] { 20, 86 }, peers.Select(frame => frame[2])); }
        }
    }
    private static void AliasedStreams()
    {
        using var f = CreatePaidScene(498, 3, out var shape, alias: true, beforePhase: (scene, _) =>
        {
            for (int dx = 0; dx < 3; dx++) for (int dy = 0; dy < 2; dy++)
                scene.Tiles.Set(99 + dx, 100 + dy, new WorldTile { Type = 14, Flags = WorldTileFlags.Active, FrameX = (short)(dx * 18), FrameY = (short)(dy * 18) });
        });
        PlaceReports(f, 498, in shape); var before = f.Random.Clone(); var clocks = f.Member.ItemPhase!.Value.ToolUse!.Clocks;
        f.Apply(new PlayerEquipmentRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, 0, 2, 0, 498, 0)));
        Assert.Equal(0, f.Composition.WorldTileAuthority.CommittedToolActions); Assert.False(f.Tiles.Get(102, 100).IsActive);
        Assert.Empty(f.Tiles.RuntimeTileEntities!.CaptureSnapshot()); Assert.True(f.Random.HasSameState(before)); Assert.Equal(clocks, f.Member.ItemPhase!.Value.ToolUse!.Clocks);
        Assert.Equal((short)3, Inventory(f.Players).CaptureFundedQuantities(f.Connection)[0]);
    }
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_commitSink")] private static extern ref IWorldItemStateCommitSink? ItemSink(RuntimeWorldItemStore owner);

    private static void PendingPaymentTail()
    {
        using var f = CreatePaidScene(498, 3, out var shape);
        PlaceReports(f, 498, in shape);
        var pending = f.Member.ItemPhase!.Value.ToolUse!.Input; Assert.NotNull(pending);
        Assert.Null(pending.PlacementInventoryReport);
        f.Move(false);
        Assert.Same(pending, f.Member.ItemPhase!.Value.ToolUse!.Input);
        Assert.Equal(0, f.Composition.WorldTileAuthority.CommittedToolActions);
        f.Apply(new PlayerEquipmentRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, 0, 2, 0, 498, 0)));
        Assert.Equal(1, f.Composition.WorldTileAuthority.CommittedToolActions);
        Assert.Single(f.Tiles.RuntimeTileEntities!.CaptureSnapshot());
        Assert.Equal((short)2, Inventory(f.Players).CaptureFundedQuantities(f.Connection)[0]);
    }

    private static void CheckpointCycle()
    {
        byte[] original = TerraRuntime.Tests.DurableWorldChestCustody.WorldFixture.CreateCompleteCurrentWorld(100, 100);
        var limits = TerraRuntime.Tests.DurableWorldChestCustody.WorldFixture.CreateLimits() with { MaxTileCount = 10000 };
        Assert.True(WorldFileLoader.TryLoad(original, limits, out var world).IsLoaded); Assert.NotNull(world);
        Assert.True(WorldFilePreservedSections.TryCapture(original, world.Envelope, out var preserved));
        foreach (int x in new[] { 46, 50, 54 })
        {
            for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 3; dy++)
                world.Tiles.Set(x + dx, 50 + dy, new WorldTile { Type = 470, Flags = WorldTileFlags.Active, FrameX = (short)(dx * 18), FrameY = (short)(dy * 18) });
            for (int dx = 0; dx < 2; dx++) world.Tiles.Set(x + dx, 53, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        }
        Assert.True(world.Tiles.TryAttachTileEntities([], 100000)); var metadata = world.Tiles.RuntimeTileEntities!;
        Assert.True(metadata.TryPrepare(16, out var initial));
        foreach (int x in new[] { 46, 50, 54 }) Assert.True(initial!.TryStageEmptyPlacement(x, 50, WorldTileEntityKind.DisplayDoll, out _));
        Assert.True(initial!.TryStageRemoval(50, 50, WorldTileEntityKind.DisplayDoll)); Adopt(world.Tiles, initial);
        var snapshots = new RuntimeWorldCheckpointSnapshotSource(world.Tiles, new RuntimeChestStore([]), 8);
        while (!snapshots.IsTileShadowReady) snapshots.CaptureTileBootstrap(8);
        Assert.True(snapshots.TryCapture(out var snapshot)); Assert.NotNull(snapshot); Assert.NotNull(snapshot.TileEntities);
        Assert.Equal(new[] { 0, 2 }, snapshot.TileEntities!.Select(e => e.PersistedId));
        Assert.True(metadata.TryPrepare(16, out var pose)); Assert.True(pose!.TryStageDisplayDollPose(54, 50, 2, 0, 1)); Adopt(world.Tiles, pose);
        Assert.Equal((byte)0, Assert.IsType<WorldDisplayDollPayload>(snapshot.TileEntities[1].Payload).Pose);
        using var entityBytes = new MemoryStream();
        Assert.Equal(WorldFileTileEntityEncodeResult.Encoded, WorldFileTileEntityEncoder.TryEncode(snapshot.TileEntities, world.Header.Dimensions, 100000, entityBytes, out _));
        var bits = world.Envelope.FrameImportanceBits.ToArray();
        for (int type = 0; type < world.Envelope.FrameImportanceCount; type++)
            if (VanillaWorldFrameImportance326.IsFrameImportant(type)) bits[type >> 3] |= (byte)(1 << (type & 7));
        var envelope = new WorldFileEnvelope(world.Envelope.FormatVersion, world.Envelope.Revision, world.Envelope.FavoriteFlags,
            world.Envelope.SectionOffsets.ToArray(), world.Envelope.FrameImportanceCount, bits);
        using var saved = new MemoryStream();
        Assert.Equal(WorldFileTileChestRewriteResult.Rewritten, WorldFileTileChestRewriter.TryRewrite(envelope, world.Header,
            preserved!.Header.Span, preserved, snapshot.Tiles, snapshot.Chests, preserved.Signs.Span, preserved.Npcs.Span,
            preserved.TownRooms.Span, saved, out _, null, null, entityBytes.ToArray()));
        Assert.True(WorldFileLoader.TryLoad(saved.ToArray(), limits, out var restored).IsLoaded); Assert.NotNull(restored);
        Assert.Equal(new[] { 0, 2 }, restored.TileEntities.Select(e => e.PersistedId)); // Binary decoder ABI remains untouched.
        var bootstrap = PlayerBootstrapPacketSet.Create(restored);
        Assert.True(bootstrap.TryGetBaseSectionFrames(out var oldFrames)); Assert.NotEmpty(oldFrames);
        var section = new WorldSectionId(0, 0); long oldVersion = restored.Tiles.GetSectionVersion(section);
        string path = Path.Combine(Path.GetTempPath(), "TerraRuntime-PaidFurniture-" + Guid.NewGuid().ToString("N") + ".wld");
        try
        {
            File.WriteAllBytes(path, saved.ToArray());
            using var runtime = new WorldRuntime(new(WorldRuntimeId.CreateNew(), WorldSessionId.CreateNew()),
                new SandboxWorldSource.WorldFile(path) { TrustInitialVanillaStock = true }, restored,
                bootstrap, new InterestManagementControl(), new WorldRuntimeOptions { MaxPlayers = 1 });
            var loaded = restored.Tiles.RuntimeTileEntities!;
            Assert.Equal(new[] { 0, 1 }, loaded.CaptureSnapshot().Select(e => e.PersistedId));
            Assert.True(restored.Tiles.GetSectionVersion(section) > oldVersion);
            // Source-correct live IDs must invalidate bytes precompressed before
            // the WorldRuntime constructor; never serve the old0/2 namespace.
            Assert.NotEqual(SectionFrameLookupResult.Available, bootstrap.ResolveSectionFrameForInitialBootstrap(section, out _));
            Assert.True(restored.Tiles.TryCaptureSectionSnapshot(section, out var current));
            Assert.Equal(WorldSectionPacketSnapshotCaptureResult.Captured,
                WorldSectionPacketSnapshotCapture.TryCapture(restored, current!, WorldSectionEncodingContext.Capture(restored), out var packet));
            Assert.NotNull(packet);
            Assert.Equal(WorldSectionObjectMetadataEncodeResult.Encoded,
                WorldSectionObjectMetadataEncoder.TryEncode(restored, 0, 0, 100, 100, out var metadataBytes));
            Assert.Equal(metadataBytes, packet.ObjectMetadata.ToArray());
            Assert.True(loaded.TryPrepare(16, out var placement));
            Assert.True(placement!.TryStageEmptyPlacement(50, 50, WorldTileEntityKind.DisplayDoll, out int id)); Assert.Equal(2, id);
            Adopt(restored.Tiles, placement);
            Assert.Equal(WorldSectionPacketSnapshotCaptureResult.StaleTileSnapshot,
                WorldSectionPacketSnapshotCapture.TryCapture(restored, current!, WorldSectionEncodingContext.Capture(restored), out _));
            Assert.True(loaded.TryPrepare(16, out var nextPose)); Assert.True(nextPose!.TryStageDisplayDollPose(50, 50, 2, 0, 1)); Adopt(restored.Tiles, nextPose);
            Assert.True(loaded.TryCaptureById(2, out var changed)); Assert.Equal((byte)1, Assert.IsType<WorldDisplayDollPayload>(changed!.Payload).Pose);
        }
        finally { File.Delete(path); }
    }

    private static void Post(LarvaRuntimeFixture1458 fixture, byte[] bytes)
    {
        var pool = new PlayerSlotPool(3); Assert.True(pool.TryAcquireConnection(out var first)); Assert.True(pool.TryAcquireConnection(out var second));
        using (first!) using (second!)
        using (var bootstrap = new PlayerBootstrapFrameSink(pool, new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(32, 8192, 2048)),
            PlayerBootstrapPacketSet.CreateForTesting(new byte[] { 3, 0, (byte)TerrariaMessageId.WorldData }, [], new byte[] { 3, 0, (byte)TerrariaMessageId.PlayerSpawnSelf }), fixture.Connection.Source, new Spawn()))
        {
            Feed(bootstrap, (byte)TerrariaMessageId.Hello, [11, (byte)'T',(byte)'e',(byte)'r',(byte)'r',(byte)'a',(byte)'r',(byte)'i',(byte)'a',(byte)'3',(byte)'2',(byte)'6']);
            Feed(bootstrap, (byte)TerrariaMessageId.RequestWorldData, []); Feed(bootstrap, (byte)TerrariaMessageId.SpawnTileData, new byte[9]);
            var spawn = new byte[TerrariaJoinRequestDecoder.PlayerSpawnPayloadLength]; spawn[0] = fixture.Connection.Player.Slot.Value;
            Feed(bootstrap, (byte)TerrariaMessageId.PlayerSpawn, spawn); Assert.Equal(PlayerJoinState.Playing, bootstrap.JoinState);
            var inbox = new Inbox();
            var objectSink = new ObjectPlacementFrameSink(fixture.Connection.Source, bootstrap, new Pass(), new RuntimeObjectPlacementNetworkIngress(inbox));
            var sink = new TileManipulationFrameSink(fixture.Connection.Source, bootstrap, objectSink, new RuntimeTileNetworkIngress(inbox));
            var frame = Frame(bytes); Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(in frame)); Assert.NotNull(inbox.Command); fixture.Apply(inbox.Command!);
        }
    }
    private static TerrariaFrame Frame(byte[] bytes) => new((ushort)bytes.Length, bytes[2], new ReadOnlySequence<byte>(bytes), new ReadOnlySequence<byte>(bytes.AsMemory(3)));
    private static void Feed(ITerrariaFrameSink sink, byte id, byte[] payload)
    {
        byte[] bytes = new byte[payload.Length + 3]; BinaryPrimitives.WriteUInt16LittleEndian(bytes, checked((ushort)bytes.Length)); bytes[2] = id; payload.CopyTo(bytes, 3);
        var frame = Frame(bytes); Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(in frame));
    }
    private sealed class Spawn : IPlayerSpawnCommitIngress
    { public bool TryPost(GameCommandSourceId source, PlayerJoinSession session, in PlayerSpawnCommitRequest request) => session.TryCommitSpawn(request.ClaimedSlot) == PlayerSpawnCommitResult.Committed; }
    private sealed class Pass : ITerrariaFrameSink { public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame) => TerrariaFrameSinkResult.Continue; }
    private sealed class Inbox : IGameCommandIngress<RuntimeCommand>
    { internal RuntimeCommand? Command; public bool TryPost(GameCommandSourceId source, RuntimeCommand command) { Command = command; return true; } }
    private static WorldTile Cell(JsonElement row)
    {
        var tile = new WorldTile { Type = row.GetProperty("type").GetUInt16(), Wall = row.GetProperty("wall").GetUInt16(),
            FrameX = row.GetProperty("frameX").GetInt16(), FrameY = row.GetProperty("frameY").GetInt16(),
            LiquidAmount = row.GetProperty("liquid").GetByte(), LiquidKind = (WorldLiquidKind)((row.GetProperty("b1").GetByte() & 96) >> 5),
            Flags = row.GetProperty("active").GetBoolean() ? WorldTileFlags.Active : 0 };
        Assert.True(tile.TrySetSourceShape(row.GetProperty("halfBrick").GetBoolean(), row.GetProperty("slope").GetByte())); return tile;
    }
    private static void SameCells(JsonElement rows, ToolWorldTileOverlay1458 overlay)
    {
        foreach (var row in rows.EnumerateArray())
        {
            var expected = Cell(row); Assert.True(overlay.TryReadSourceSafely(row.GetProperty("x").GetInt32(), row.GetProperty("y").GetInt32(), out var actual));
            if (actual.IsActive && !VanillaWorldFrameImportance326.IsFrameImportant(actual.Type)) { expected.FrameX = actual.FrameX; expected.FrameY = actual.FrameY; }
            Assert.Equal(expected, actual);
        }
    }
    private static VanillaUnifiedRandom1458 ReadRandom(JsonElement row)
    { var random = new VanillaUnifiedRandom1458(0); Cursor(random) = row.GetProperty("cursor").GetUInt32(); row.GetProperty("state").EnumerateArray().Select(x => x.GetInt32()).ToArray().CopyTo(Seed(random), 0); return random; }
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inext")] private static extern ref uint Cursor(VanillaUnifiedRandom1458 owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "seedArray")] private static extern ref int[] Seed(VanillaUnifiedRandom1458 owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inventory")] private static extern ref RuntimePlayerInventoryStore Inventory(PlayerAuthority owner);
}
