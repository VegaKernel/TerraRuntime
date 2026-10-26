using System.Buffers;
using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;
using Xunit;

namespace TerraRuntime.Tests.ToolPlayer;

internal static partial class ToolPlayerBoundaryChecks
{
    internal static void RunFurniture()
    {
        FurnitureOriginalRows();
        foreach (ushort type in new ushort[] { 137, 424, 442 })
        {
            FurnitureSocketBeforeTick(type, false);
            FurnitureSocketBeforeTick(type, true);
        }
        FurnitureDualShape();
        FurnitureStaleAndForged();
        FurniturePublicationCurrentness();
        FurnitureServerKillReentry();
        FurnitureRangeEdgeAndWall();
    }

    private static JsonDocument FurnitureSource(string platform)
    {
        using var input = typeof(ToolPlayerBoundaryChecks).Assembly.GetManifestResourceStream("ToolFurnitureSource" + platform + "1458")
            ?? throw new InvalidOperationException("Missing independent original specialhammer source.");
        using var gzip = new GZipStream(input, CompressionMode.Decompress); return JsonDocument.Parse(gzip);
    }

    private static void FurnitureOriginalRows()
    {
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using var doc = FurnitureSource(platform); Assert.Equal(126, doc.RootElement.GetArrayLength());
            foreach (var row in doc.RootElement.EnumerateArray())
            {
                using var f = new Fixture(7); var world = Composition(f.State).WorldTileAuthority;
                var before = row.GetProperty("before");
                foreach (var cell in before.EnumerateArray()) f.Tiles.Set(cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32(), FurnitureTile(cell, row.GetProperty("rich").GetBoolean()));
                var window = new ToolFrameTile1458[9];
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                { var tile = f.Tiles.Get(50 + dx, 50 + dy); window[(dy + 1) * 3 + dx + 1] = FurnitureDto(in tile); }
                var target = f.Tiles.Get(50, 50); var targetDto = FurnitureDto(in target);
                Assert.True(VanillaToolFurniture1458.TryRotate(in targetDto, window, out short nextFrame, out bool report));
                var sourceFrames = row.GetProperty("clientFrames").EnumerateArray().Select(x => Convert.FromHexString(x.GetString()!)).ToArray();
                Assert.DoesNotContain((byte)17, sourceFrames.Select(x => x[2]).ToArray());
                Assert.Equal(report ? 1 : 0, sourceFrames.Count(x => x[2] == 20));
                var clientImage = new ToolWorldTileOverlay1458(f.Tiles); var serverImage = new ToolWorldTileOverlay1458(f.Tiles);
                var localRandom = ReadLucyRandom(row.GetProperty("beforeRandom"));
                if (row.GetProperty("full").GetBoolean()) { localRandom.Next(100); localRandom.Next(100); }
                localRandom.NextDouble(); // Source common HitTile.AddDamage, outside the raw orientation call.
                Assert.True(world.TryCaptureToolTreeWorld(out var context)); Assert.NotNull(context);
                Assert.True(f.Players.TryCaptureToolTreeActors(out var actors)); Assert.NotNull(actors);
                Assert.True(world.TryCreateToolSourcePass(clientImage, localRandom, true, false, context!.Facts, actors!.Actors, null, out var client));
                Assert.True(client!.TryRotateToolFurniture(50, 50, out var square));
                Assert.Equal(report, square is not null);
                Assert.True(localRandom.HasSameState(ReadLucyRandom(row.GetProperty("clientRandom"))));
                Assert.True(clientImage.TryRead(50, 50, out var after)); Assert.Equal(nextFrame, after.FrameX);
                if (square is not { } sent) continue;
                Assert.True(TerrariaTileSquareCodec.TryEncodeToolFrame(in sent, out var encoded));
                Assert.Equal(sourceFrames.Single(x => x[2] == 20), encoded);
                var packet = LucyFrame(encoded);
                Assert.Equal(TerrariaToolTileSquareDecodeResult1458.Decoded, TerrariaTileSquareCodec.TryDecodeToolFrame(in packet, out var decoded));
                Assert.Equal((byte)0, decoded.Tile.LiquidAmount);
                Assert.True(TerrariaTileSquareCodec.HasSameToolWire(in sent, in decoded));
                Assert.True(world.TryCreateToolMutationPlan(serverImage, out var plan)); Assert.NotNull(plan);
                using (plan!)
                {
                    // Detached source oracle only. The production owner keeps its
                    // own captured stream; this never adopts a selected client seed.
                    plan!.SourceRandom.CopyStateFrom(ReadLucyRandom(row.GetProperty("serverBeforeRandom")));
                    Assert.True(world.TryCreateToolSourcePass(serverImage, plan.SourceRandom, false, false, context.Facts, actors.Actors, plan, out var server));
                    Assert.True(server!.TryApplyToolFurnitureSquare(in sent), $"{platform}/{row.GetProperty("type")}/{row.GetProperty("fx")}/{row.GetProperty("fy")}/{row.GetProperty("mode")}/rich={row.GetProperty("rich")}");
                    Assert.True(plan.SourceRandom.HasSameState(ReadLucyRandom(row.GetProperty("serverRandom"))), $"RNG {platform}/{row.GetProperty("type")}/{row.GetProperty("fx")}/{row.GetProperty("fy")}/{row.GetProperty("mode")}: actualCursor={LucyRandomCursor(plan.SourceRandom)}, expected={row.GetProperty("serverRandom").GetProperty("cursor")}");
                    Assert.True(serverImage.TryRead(50, 50, out var accepted));
                    var acceptedState = new TerrariaToolTileSquareState1458(50, 50, accepted);
                    Assert.True(TerrariaTileSquareCodec.TryEncodeToolServerFrame(in acceptedState, out var serverWire));
                    var sourceServer = row.GetProperty("serverFrames").EnumerateArray().Select(x => Convert.FromHexString(x.GetString()!)).Where(x => x[2] == 20).ToArray();
                    Assert.Equal(serverWire, Assert.Single(sourceServer));
                    foreach (var sourceCell in row.GetProperty("serverAfter").EnumerateArray())
                    {
                        int x = sourceCell.GetProperty("x").GetInt32(), y = sourceCell.GetProperty("y").GetInt32();
                        Assert.True(serverImage.TryRead(x, y, out var actual));
                        var expected = FurnitureTile(sourceCell, row.GetProperty("rich").GetBoolean());
                        Assert.Equal(expected.IsActive, actual.IsActive);
                        if (expected.IsActive)
                        {
                            Assert.Equal(expected.Type, actual.Type);
                            if (VanillaWorldFrameImportance326.IsFrameImportant(expected.Type))
                            { Assert.Equal(expected.FrameX, actual.FrameX); Assert.Equal(expected.FrameY, actual.FrameY); }
                        }
                        Assert.Equal(expected.Wall, actual.Wall); Assert.Equal(expected.LiquidAmount, actual.LiquidAmount);
                        Assert.Equal(expected.SourceSlope, actual.SourceSlope); Assert.Equal(expected.IsHalfBrick, actual.IsHalfBrick);
                        Assert.Equal(expected.TileColor, actual.TileColor); Assert.Equal(expected.WallColor, actual.WallColor);
                        Assert.Equal(expected.Flags, actual.Flags);
                    }
                    Assert.Equal(target.Type, f.Tiles.Get(50, 50).Type); Assert.Equal(target.FrameX, f.Tiles.Get(50, 50).FrameX);
                }
            }
        }
    }

    private static void FurnitureSocketBeforeTick(ushort type, bool rich)
    {
        using var f = FurnitureFixture(type, rich); f.Tick(); // Genuine owned release stage, not a private flag write.
        var queues = LucyPeers(f); var image = f.CaptureInventory(); var rng = CurrentLucyRandom(f).Clone();
        var report = FurnitureExpected(f); Assert.True(TerrariaTileSquareCodec.TryEncodeToolFrame(in report, out var raw));
        using var bootstrap = FurniturePlayingBootstrap(f.Connection.Source);
        var posted = new FurnitureInbox(); var ingress = new RuntimeTileNetworkIngress(posted);
        var sink = new TileManipulationFrameSink(f.Connection.Source, bootstrap, new LucyPass(), ingress);
        f.Move(0, true); // actual13 then socket20 before the next Tick.
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(LucyFrame(raw)));
        Assert.Equal(1, posted.Count); var command = Assert.IsType<ClientToolTileSquareRuntimeCommand1458>(posted.Command);
        Assert.Equal(f.Connection, command.Connection); Assert.Equal(f.Connection.Source, posted.Source);
        f.Apply(command);
        Assert.Equal(0UL, f.Tools.ActionSerial); Assert.NotNull(f.Tools.Input?.SquareReport);
        Assert.Equal(0, f.Tools.Input!.Count); Assert.Equal(0, queues.Peer.QueuedFrames);
        f.Tick();
        Assert.Equal(1UL, f.Tools.ActionSerial); Assert.Equal(report.Tile.FrameX, f.Tiles.Get(102, 102).FrameX);
        Assert.Equal(20, f.Tools.Clocks.Selected.ItemTime); Assert.False(f.Tools.Clocks.PoundRelease);
        Assert.Null(f.Tools.ReleaseWitness); f.AssertInventory(image);
        rng.Next(100); rng.NextDouble(); Assert.True(CurrentLucyRandom(f).HasSameState(rng));
        Assert.Equal(rich ? (byte)87 : (byte)0, f.Tiles.Get(102, 102).LiquidAmount);
        var frames = FurnitureDrain(queues.Peer); var cell = Assert.Single(frames, FurnitureOneCellSquare);
        Assert.DoesNotContain((byte)17, frames.Select(x => x[2]).ToArray());
        var received = LucyFrame(cell);
        Assert.Equal(TerrariaToolTileSquareDecodeResult1458.Decoded, TerrariaTileSquareCodec.TryDecodeToolFrame(in received, false, out var owned));
        Assert.Equal(f.Tiles.Get(102, 102).LiquidAmount, owned.Tile.LiquidAmount);
        Assert.Equal(0, queues.Sender.QueuedFrames);
        f.Apply(command); // duplicate cannot reuse a consumed release or skip source cooldown.
        Assert.Equal(1UL, f.Tools.ActionSerial);
        Assert.DoesNotContain(FurnitureDrain(queues.Peer), FurnitureOneCellSquare); f.AssertInventory(image);
    }

    private static void FurnitureDualShape()
    {
        using var f = FurnitureFixture(137, true); var tile = f.Tiles.Get(102, 102);
        Assert.True(tile.TrySetSourceShape(true, 2)); f.Tiles.Set(102, 102, tile); f.Tick(); f.Move(0, true);
        var report = FurnitureExpected(f); Assert.True(TerrariaTileSquareCodec.TryEncodeToolFrame(in report, out var wire));
        var a = new LucySegment(wire.AsMemory(3, 2)); var b = a.Append(wire.AsMemory(5, 5)); var c = b.Append(wire.AsMemory(10));
        var packet = new TerrariaFrame(checked((ushort)wire.Length), 20, ReadOnlySequence<byte>.Empty, new ReadOnlySequence<byte>(a, 0, c, c.Memory.Length));
        Assert.Equal(TerrariaToolTileSquareDecodeResult1458.Decoded, TerrariaTileSquareCodec.TryDecodeToolFrame(in packet, out var decoded));
        Assert.True(decoded.Tile.IsHalfBrick); Assert.Equal((byte)2, decoded.Tile.SourceSlope);
        f.Apply(new ClientToolTileSquareRuntimeCommand1458(f.Connection, decoded)); f.Tick();
        Assert.Equal(1UL, f.Tools.ActionSerial); tile = f.Tiles.Get(102, 102);
        Assert.True(tile.IsHalfBrick); Assert.Equal((byte)2, tile.SourceSlope); Assert.Equal((byte)87, tile.LiquidAmount);
        // CLIENT never writes liquid in20. Do not normalize a forged liquid field
        // into an accepted source receipt even if the rest of the cell matches.
        var liquidClaim = new TerrariaToolTileSquareState1458(102, 102, tile);
        Assert.True(TerrariaTileSquareCodec.TryEncodeToolServerFrame(in liquidClaim, out var serverWire)); packet = LucyFrame(serverWire);
        Assert.Equal(TerrariaToolTileSquareDecodeResult1458.Malformed, TerrariaTileSquareCodec.TryDecodeToolFrame(in packet, out _));
        for (int length = 0; length < wire.Length - 3; length++)
        {
            var truncated = new TerrariaFrame(checked((ushort)(length + 3)), 20, ReadOnlySequence<byte>.Empty, new ReadOnlySequence<byte>(wire.AsMemory(3, length)));
            Assert.NotEqual(TerrariaToolTileSquareDecodeResult1458.Decoded, TerrariaTileSquareCodec.TryDecodeToolFrame(in truncated, out _));
        }
    }

    private static void FurnitureStaleAndForged()
    {
        using var f = FurnitureFixture(137, false); f.Tick(); f.Move(0, true); f.Tick();
        var report = FurnitureExpected(f); var forged = report; var tile = forged.Tile; tile.FrameX = 90; forged = forged with { Tile = tile };
        var rng = CurrentLucyRandom(f).Clone(); f.Apply(new ClientToolTileSquareRuntimeCommand1458(f.Connection, forged));
        Assert.Equal(0UL, f.Tools.ActionSerial); Assert.Equal((short)0, f.Tiles.Get(102, 102).FrameX);
        Assert.True(CurrentLucyRandom(f).HasSameState(rng));
        f.Move(0, false); f.Tick(); f.Move(0, true); f.Tick();
        Assert.Equal(ToolReportObservationResult1458.Retained, f.Players.ObserveToolSquare(f.Connection, in report, out var input));
        Assert.True(f.Players.TryPrepareToolAttempt(f.Connection, out var player));
        var world = Composition(f.State).WorldTileAuthority;
        Assert.True(world.TryPrepareToolActionAt(player!, 102, 102, out var action)); Assert.NotNull(action);
        using (action!)
        {
            Assert.True(action!.IsCurrent); var sibling = f.Tiles.Get(103, 102); sibling.Type = 1; sibling.Flags |= WorldTileFlags.Active;
            f.Tiles.Set(103, 102, sibling); Assert.False(action.IsCurrent); Assert.False(action.TrySeal(input!));
            Assert.Equal(0L, world.CommittedToolActions);
        }
        var old = f.Connection; f.Replace();
        f.Apply(new ClientToolTileSquareRuntimeCommand1458(old, report)); Assert.Equal(0L, world.CommittedToolActions);
    }

    private static void FurniturePublicationCurrentness()
    {
        foreach (bool changed in new[] { false, true })
        {
            using var f = FurnitureFixture(137, false); f.Tick(); f.Move(0, true); f.Tick(); var queues = LucyPeers(f);
            var report = FurnitureExpected(f);
            Assert.Equal(ToolReportObservationResult1458.Retained, f.Players.ObserveToolSquare(f.Connection, in report, out var input));
            Assert.True(f.Players.TryPrepareToolAttempt(f.Connection, out var player)); var owner = Composition(f.State).WorldTileAuthority;
            Assert.True(owner.TryPrepareToolActionAt(player!, 102, 102, out var action)); Assert.NotNull(action);
            using (action!)
            {
                Assert.True(action!.TrySeal(input!)); Assert.True(action.TryAdoptUnpublished());
                Assert.Equal(1UL, f.Tools.ActionSerial); Assert.Equal((short)36, f.Tiles.Get(102, 102).FrameX);
                if (changed)
                {
                    var sibling = f.Tiles.Get(103, 103); sibling.Wall = 1; f.Tiles.Set(103, 103, sibling);
                }
                Assert.True(action.TryPublishAll()); Assert.True(action.TryPublishAll());
                var cells = FurnitureDrain(queues.Peer).Where(FurnitureOneCellSquare).ToArray();
                if (changed) Assert.Empty(cells); else Assert.Single(cells);
                Assert.Equal(1UL, f.Tools.ActionSerial);
            }
        }
    }

    private static void FurnitureServerKillReentry()
    {
        var observer = new ReplantDropSink(); using var f = FurnitureFixture(424, false, observer);
        var invalid = f.Tiles.Get(102, 102); invalid.FrameY = 1; f.Tiles.Set(102, 102, invalid);
        f.Tick(); f.Move(0, true); var queues = LucyPeers(f); var image = f.CaptureInventory();
        var report = FurnitureExpected(f); int callbacks = 0;
        observer.OnDrop = () =>
        {
            callbacks++; Assert.Equal(1UL, f.Tools.ActionSerial); Assert.False(f.Tiles.Get(102, 102).IsActive);
            Assert.Equal(20, f.Tools.Clocks.Selected.ItemTime); f.AssertInventory(image);
            var neighbor = f.Tiles.Get(103, 103); neighbor.Wall = 1; f.Tiles.Set(103, 103, neighbor);
            f.Apply(new ClientToolTileSquareRuntimeCommand1458(f.Connection, report));
        };
        f.Apply(new ClientToolTileSquareRuntimeCommand1458(f.Connection, report)); f.Tick();
        Assert.Equal(1, callbacks); Assert.Equal(1UL, f.Tools.ActionSerial); Assert.False(f.Tiles.Get(102, 102).IsActive);
        Assert.DoesNotContain(FurnitureDrain(queues.Peer), FurnitureOneCellSquare); f.AssertInventory(image);
    }

    private static Fixture FurnitureFixture(ushort type, bool rich, IWorldItemStateCommitSink? sink = null)
    {
        var f = new Fixture(7, sink);
        for (int x = 100; x <= 105; x++) for (int y = 100; y <= 102; y++) f.Tiles.Set(x, y, default);
        var tile = new WorldTile { Type = type, Flags = WorldTileFlags.Active };
        if (rich)
        {
            tile.Wall = 1; tile.WallColor = 13; tile.TileColor = 9; tile.LiquidAmount = 87; tile.LiquidKind = WorldLiquidKind.Honey;
            tile.Flags |= WorldTileFlagMasks.Wires | WorldTileFlags.Actuator | WorldTileFlagMasks.Fullbright | WorldTileFlagMasks.Visibility;
        }
        f.Tiles.Set(102, 102, tile);
        if (type == 442)
            foreach (var point in new[] { (102, 101), (103, 102) }) f.Tiles.Set(point.Item1, point.Item2, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        return f;
    }
    private static TerrariaToolTileSquareState1458 FurnitureExpected(Fixture f)
    {
        Span<ToolFrameTile1458> window = stackalloc ToolFrameTile1458[9];
        for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
        { var tile = f.Tiles.Get(102 + dx, 102 + dy); window[(dy + 1) * 3 + dx + 1] = FurnitureDto(in tile); }
        var target = f.Tiles.Get(102, 102); var dto = FurnitureDto(in target);
        Assert.True(VanillaToolFurniture1458.TryRotate(in dto, window, out short frame, out bool report)); Assert.True(report);
        target.FrameX = frame;
        Assert.True(TerrariaTileSquareCodec.TryEncodeToolFrame(new(102, 102, target), out var wire)); var packet = LucyFrame(wire);
        Assert.Equal(TerrariaToolTileSquareDecodeResult1458.Decoded, TerrariaTileSquareCodec.TryDecodeToolFrame(in packet, out var decoded)); return decoded;
    }
    private static ToolFrameTile1458 FurnitureDto(in WorldTile tile) => new(tile.TileType, tile.FrameX, tile.FrameY, tile.IsActive, tile.IsActuated, tile.IsHalfBrick, tile.SourceSlope);
    private static WorldTile FurnitureTile(JsonElement cell, bool rich)
    {
        _ = rich; // All physical bits below come from the independent numeric snapshot.
        int header = cell.GetProperty("header").GetInt32(), b1 = cell.GetProperty("b1").GetInt32(), b3 = cell.GetProperty("b3").GetInt32();
        var tile = new WorldTile
        {
            Type = checked((ushort)cell.GetProperty("type").GetInt32()), Wall = checked((ushort)cell.GetProperty("wall").GetInt32()),
            FrameX = checked((short)cell.GetProperty("frameX").GetInt32()), FrameY = checked((short)cell.GetProperty("frameY").GetInt32()),
            LiquidAmount = checked((byte)cell.GetProperty("liquid").GetInt32()), TileColor = (byte)(header & 31), WallColor = (byte)(b1 & 31),
            LiquidKind = (WorldLiquidKind)((b1 & 96) >> 5)
        };
        if ((header & 32) != 0) tile.Flags |= WorldTileFlags.Active;
        if ((header & 64) != 0) tile.Flags |= WorldTileFlags.Inactive;
        if ((header & 128) != 0) tile.Flags |= WorldTileFlags.WireRed;
        if ((header & 256) != 0) tile.Flags |= WorldTileFlags.WireBlue;
        if ((header & 512) != 0) tile.Flags |= WorldTileFlags.WireGreen;
        if ((header & 2048) != 0) tile.Flags |= WorldTileFlags.Actuator;
        if ((header & 32768) != 0) tile.Flags |= WorldTileFlags.FullbrightWall;
        if ((b1 & 128) != 0) tile.Flags |= WorldTileFlags.WireYellow;
        if ((b3 & 32) != 0) tile.Flags |= WorldTileFlags.InvisibleBlock;
        if ((b3 & 64) != 0) tile.Flags |= WorldTileFlags.InvisibleWall;
        if ((b3 & 128) != 0) tile.Flags |= WorldTileFlags.FullbrightBlock;
        Assert.True(tile.TrySetSourceShape(cell.GetProperty("half").GetBoolean(), cell.GetProperty("slope").GetByte()));
        return tile;
    }

    private static void FurnitureRangeEdgeAndWall()
    {
        var observer = new ReplantDropSink(); using var f = FurnitureFixture(137, false, observer);
        f.Tiles.Set(104, 104, new WorldTile { Type = 424, FrameY = 1, Flags = WorldTileFlags.Active });
        f.Tiles.Set(104, 103, new WorldTile { Type = 1, WallColor = 9,
            Flags = WorldTileFlags.Active | WorldTileFlags.FullbrightWall | WorldTileFlags.InvisibleWall });
        f.Tick(); f.Move(0, true); int seen = 0; var image = f.CaptureInventory();
        observer.OnDrop = () =>
        {
            seen++; Assert.Equal(1UL, f.Tools.ActionSerial); Assert.Equal((short)36, f.Tiles.Get(102, 102).FrameX);
            Assert.False(f.Tiles.Get(104, 104).IsActive); Assert.Equal((byte)0, f.Tiles.Get(104, 103).WallColor);
            Assert.False(f.Tiles.Get(104, 103).IsWallFullbright); Assert.False(f.Tiles.Get(104, 103).IsWallInvisible);
            f.AssertInventory(image);
        };
        f.Apply(new ClientToolTileSquareRuntimeCommand1458(f.Connection, FurnitureExpected(f))); f.Tick();
        Assert.Equal(1, seen); Assert.False(f.Tiles.Get(104, 104).IsActive); f.AssertInventory(image);
    }

    private static byte[][] FurnitureDrain(TerrariaConnectionOutboundQueue queue)
    { var frames = new List<byte[]>(); while (LucyQueue(queue).TryRead(out var frame)) frames.Add(frame.Bytes.ToArray()); return frames.ToArray(); }
    private static bool FurnitureOneCellSquare(byte[] frame) => frame[2] == 20 && frame.Length >= 10 && frame[7] == 1 && frame[8] == 1;
    private static PlayerBootstrapFrameSink FurniturePlayingBootstrap(GameCommandSourceId source)
    {
        var value = LucyBootstrap(source);
        Assert.Equal(TerrariaFrameSinkResult.Continue, value.OnFrame(LucySimpleFrame(TerrariaMessageId.Hello,
            new byte[] { 11, (byte)'T', (byte)'e', (byte)'r', (byte)'r', (byte)'a', (byte)'r', (byte)'i', (byte)'a', (byte)'3', (byte)'2', (byte)'6' })));
        value.OnFrame(LucySimpleFrame(TerrariaMessageId.RequestWorldData, [])); value.OnFrame(LucySimpleFrame(TerrariaMessageId.SpawnTileData, new byte[9]));
        value.OnFrame(LucySimpleFrame(TerrariaMessageId.PlayerSpawn, new byte[TerrariaJoinRequestDecoder.PlayerSpawnPayloadLength]));
        Assert.Equal(PlayerJoinState.Playing, value.JoinState); return value;
    }
    private sealed class FurnitureInbox : IGameCommandIngress<RuntimeCommand>
    {
        internal int Count; internal RuntimeCommand? Command; internal GameCommandSourceId Source;
        public bool TryPost(GameCommandSourceId source, RuntimeCommand command)
        { if (Count != 0) return false; Count++; Source = source; Command = command; return true; }
    }
}
