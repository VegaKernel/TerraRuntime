using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;
using Xunit;

namespace TerraRuntime.Tests.ToolPlayer;

internal static partial class ToolPlayerBoundaryChecks
{
    private static void Lucy141()
    {
        LucySourceAndCodec();
        LucyPlayingIngress();
        LucyVisualRelay();
        LucyQualifiedTree();
        LucyMissingWrongAndPartial();
        LucyStaleActorAndWorld();
    }

    private static JsonDocument LucySource(string platform)
    {
        using var input = typeof(ToolPlayerBoundaryChecks).Assembly.GetManifestResourceStream("ToolLucySource" + platform + "1458")
            ?? throw new InvalidOperationException("Missing independent original Lucy row41.");
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private static void LucySourceAndCodec()
    {
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using var doc = LucySource(platform);
            var row = doc.RootElement;
            Assert.Equal(5095, row.GetProperty("item").GetInt32());
            var frames = row.GetProperty("frames").EnumerateArray().Select(x => Convert.FromHexString(x.GetString()!)).ToArray();
            Assert.Equal(new byte[] { 17, 17, 120, 141, 17 }, frames.Select(x => x[2]).ToArray());
            Assert.Equal((short)1, BinaryPrimitives.ReadInt16LittleEndian(frames[0].AsSpan(8)));
            Assert.Equal((short)1, BinaryPrimitives.ReadInt16LittleEndian(frames[1].AsSpan(8)));
            Assert.Equal((short)0, BinaryPrimitives.ReadInt16LittleEndian(frames[4].AsSpan(8)));
            Assert.Equal("15008D04000000E0400000E0C09A01000028040000", Convert.ToHexString(frames[3]));
            var expected = new TerrariaLucyAxeMessageState1458(4, 0, 7, -7, 410, 1064);
            var packet = LucyFrame(frames[3]);
            Assert.Equal(TerrariaLucyAxeMessageDecodeResult1458.Decoded, TerrariaLucyAxeMessageCodec1458.TryDecode(in packet, out var decoded));
            Assert.Equal(expected, decoded);
            Assert.Equal(frames[3], TerrariaLucyAxeMessageCodec1458.Encode(in expected));
            var a = new LucySegment(frames[3].AsMemory(3, 1));
            var b = a.Append(frames[3].AsMemory(4, 6));
            var c = b.Append(frames[3].AsMemory(10));
            var segmented = new TerrariaFrame(21, 141, ReadOnlySequence<byte>.Empty, new ReadOnlySequence<byte>(a, 0, c, c.Memory.Length));
            Assert.Equal(TerrariaLucyAxeMessageDecodeResult1458.Decoded, TerrariaLucyAxeMessageCodec1458.TryDecode(in segmented, out decoded));
            Assert.Equal(expected, decoded);
            for (int length = 0; length <= 19; length++)
            {
                if (length == 18) continue;
                var invalid = new TerrariaFrame(checked((ushort)(length + 3)), 141, ReadOnlySequence<byte>.Empty, new ReadOnlySequence<byte>(new byte[length]));
                Assert.Equal(TerrariaLucyAxeMessageDecodeResult1458.InvalidPayloadLength, TerrariaLucyAxeMessageCodec1458.TryDecode(in invalid, out _));
            }
            var opaque = expected with { Source = 255, Variation = 255, PositionX = int.MinValue, PositionY = int.MaxValue };
            packet = LucyFrame(TerrariaLucyAxeMessageCodec1458.Encode(in opaque));
            Assert.Equal(TerrariaLucyAxeMessageDecodeResult1458.Decoded, TerrariaLucyAxeMessageCodec1458.TryDecode(in packet, out decoded));
            Assert.Equal(opaque, decoded); // Cosmetic bytes/positions do not identify a target or player.
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                var bad = expected with { VelocityX = invalid };
                Assert.Equal(TerrariaLucyAxeMessageEncodeResult1458.InvalidValues, TerrariaLucyAxeMessageCodec1458.TryEncode(in bad, out _));
                var raw = frames[3].ToArray(); BinaryPrimitives.WriteSingleLittleEndian(raw.AsSpan(5), invalid);
                packet = LucyFrame(raw);
                Assert.Equal(TerrariaLucyAxeMessageDecodeResult1458.InvalidValues, TerrariaLucyAxeMessageCodec1458.TryDecode(in packet, out _));
            }
            // Original full ItemCheck30: common crit, HitTile animation sample,
            // then Create's one idle-cooldown draw. No later CLIENT KillTile draw.
            // This is an isolated original-call oracle, not shared server RNG.
            var before = ReadLucyRandom(row.GetProperty("steps")[30].GetProperty("beforeRandom"));
            before.Next(100); before.NextDouble();
            var omitted = before.Clone();
            before.Next(7200, 14400);
            var at141 = ReadLucyRandom(row.GetProperty("publications")[3].GetProperty("random"));
            Assert.True(before.HasSameState(at141));
            Assert.False(omitted.HasSameState(at141));
            Assert.True(at141.HasSameState(ReadLucyRandom(row.GetProperty("steps")[30].GetProperty("random"))));
        }
    }

    private static void LucyPlayingIngress()
    {
        var source = GameCommandSourceId.FromConnection(98001);
        using var bootstrap = LucyBootstrap(source);
        var ingress = new LucyIngress();
        var sink = new TileManipulationFrameSink(source, bootstrap, new LucyPass(), ingress);
        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(LucySimpleFrame(TerrariaMessageId.Hello,
            new byte[] { 11, (byte)'T', (byte)'e', (byte)'r', (byte)'r', (byte)'a', (byte)'r', (byte)'i', (byte)'a', (byte)'3', (byte)'2', (byte)'6' })));
        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(LucySimpleFrame(TerrariaMessageId.RequestWorldData, [])));
        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(LucySimpleFrame(TerrariaMessageId.SpawnTileData, new byte[9])));
        var spawn = new byte[TerrariaJoinRequestDecoder.PlayerSpawnPayloadLength];
        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(LucySimpleFrame(TerrariaMessageId.PlayerSpawn, spawn)));
        Assert.Equal(PlayerJoinState.Playing, bootstrap.JoinState);
        var state = new TerrariaLucyAxeMessageState1458(4, 17, 7, -7, 410, 1064);
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(LucyFrame(TerrariaLucyAxeMessageCodec1458.Encode(in state))));
        Assert.Equal(1, ingress.Count); Assert.Equal(state, ingress.State); Assert.Equal(source, ingress.Connection.Source);
        Assert.Equal(new PlayerSlotId(0), ingress.Connection.Player.Slot);
        var malformed = LucySimpleFrame(TerrariaMessageId.LucyAxeMessage, new byte[17]);
        Assert.Equal(TerrariaFrameSinkResult.Stop, sink.OnFrame(in malformed));
        Assert.Equal(TileManipulationFrameStopReason.MalformedManipulation, sink.StopReason);
        Assert.Equal(1, ingress.Count);
        using var notPlaying = LucyBootstrap(GameCommandSourceId.FromConnection(98002));
        var early = new TileManipulationFrameSink(GameCommandSourceId.FromConnection(98002), notPlaying, new LucyPass(), ingress);
        Assert.Equal(TerrariaFrameSinkResult.Stop, early.OnFrame(LucyFrame(TerrariaLucyAxeMessageCodec1458.Encode(in state))));
        Assert.Equal(TileManipulationFrameStopReason.InvalidJoinState, early.StopReason);
    }

    private static void LucyVisualRelay()
    {
        using var f = new Fixture(7); // Idle/storage visual is independent of holding Lucy.
        var queues = LucyPeers(f);
        var image = f.CaptureInventory(); var random = CurrentLucyRandom(f).Clone();
        foreach (byte reason in new byte[] { 0, 1, 2, 3, 4, 255 })
        {
            var state = new TerrariaLucyAxeMessageState1458(reason, 39, -7, -7, int.MinValue, int.MaxValue);
            f.Apply(new ClientLucyAxeMessageRuntimeCommand1458(f.Connection, state));
            Assert.Equal(0, queues.Sender.QueuedFrames);
            Assert.Equal(state, DequeueLucy(queues.Peer));
            Assert.True(CurrentLucyRandom(f).HasSameState(random)); f.AssertInventory(image);
            Assert.Equal(0UL, f.Tools.ActionSerial);
        }
        var old = f.Connection; f.Replace();
        var stale = new TerrariaLucyAxeMessageState1458(0, 0, 7, -7, 1, 1);
        f.Apply(new ClientLucyAxeMessageRuntimeCommand1458(old, stale));
        Assert.Equal(0, queues.Peer.QueuedFrames);
    }

    private static void LucyQualifiedTree()
    {
        using var f = LucyTwoHits(); var queues = LucyPeers(f);
        var image = f.CaptureInventory();
        var before = CurrentLucyRandom(f).Clone();
        var report = LucyAtPose(f, 4);
        f.Apply(new ClientLucyAxeMessageRuntimeCommand1458(f.Connection, report));
        Assert.Equal(report, DequeueLucy(queues.Peer));
        Assert.Equal(0, queues.Sender.QueuedFrames);
        Assert.True(CurrentLucyRandom(f).HasSameState(before));
        var input = f.Tools.Input; Assert.NotNull(input);
        Assert.True(input!.LucyPublicationConsumed); Assert.Equal(0, input.LucyObservationIndex);
        f.Apply(new ClientLucyAxeMessageRuntimeCommand1458(f.Connection, report));
        Assert.Equal(0, queues.Peer.QueuedFrames); // Same-cohort duplicate141 is cosmetic-once.
        f.Apply(new ClientTileManipulationRuntimeCommand(f.Connection, new(0, 102, 102, 0, 0)));
        Assert.Equal(3UL, f.Tools.ActionSerial);
        Assert.False(f.Tiles.Get(102, 102).IsActive);
        // At this already-certified ItemCheck stage the common crit draw was
        // committed by the frame. Target action owns exactly HitTile+Lucy here.
        before.NextDouble(); before.Next(7200, 14400);
        Assert.True(CurrentLucyRandom(f).HasSameState(before));
        f.AssertInventory(image);
        Assert.DoesNotContain((byte)141, DrainLucyFrames(queues.Peer)); // No post-world duplicate141.
    }

    private static void LucyMissingWrongAndPartial()
    {
        using (var missing = LucyTwoHits())
        {
            var before = CurrentLucyRandom(missing).Clone();
            missing.Apply(new ClientTileManipulationRuntimeCommand(missing.Connection, new(0, 102, 102, 0, 0)));
            Assert.Equal(2UL, missing.Tools.ActionSerial); Assert.True(missing.Tiles.Get(102, 102).IsActive);
            Assert.True(CurrentLucyRandom(missing).HasSameState(before));
        }
        foreach (int fault in new[] { 0, 1, 2 })
        {
            using var wrong = LucyTwoHits(); var before = CurrentLucyRandom(wrong).Clone();
            var report = LucyAtPose(wrong, 4);
            report = fault switch { 0 => report with { Source = 5 }, 1 => report with { PositionX = report.PositionX + 1 }, _ => report with { VelocityX = -report.VelocityX } };
            wrong.Apply(new ClientLucyAxeMessageRuntimeCommand1458(wrong.Connection, report));
            wrong.Apply(new ClientTileManipulationRuntimeCommand(wrong.Connection, new(0, 102, 102, 0, 0)));
            Assert.Equal(2UL, wrong.Tools.ActionSerial); Assert.True(wrong.Tiles.Get(102, 102).IsActive);
            Assert.True(CurrentLucyRandom(wrong).HasSameState(before));
        }
        using (var partial = new Fixture(5095))
        {
            partial.Move(0, true); partial.Tick();
            var before = CurrentLucyRandom(partial).Clone(); var report = LucyAtPose(partial, 4);
            partial.Apply(new ClientLucyAxeMessageRuntimeCommand1458(partial.Connection, report));
            partial.Apply(new ClientTileManipulationRuntimeCommand(partial.Connection, new(0, 102, 102, 1, 0)));
            Assert.Equal(0UL, partial.Tools.ActionSerial); Assert.True(CurrentLucyRandom(partial).HasSameState(before));
            Assert.True(partial.Tiles.Get(102, 102).IsActive);
        }
    }

    private static void LucyStaleActorAndWorld()
    {
        using var f = LucyTwoHits(); var report = LucyAtPose(f, 4);
        f.Apply(new ClientLucyAxeMessageRuntimeCommand1458(f.Connection, report));
        var oldInput = f.Tools.Input; Assert.NotNull(oldInput);
        Assert.True(f.Players.TryPrepareToolAttempt(f.Connection, out var player)); Assert.NotNull(player);
        var world = Composition(f.State).WorldTileAuthority;
        Assert.True(world.TryPrepareToolActionAt(player!, 102, 102, out var action)); Assert.NotNull(action);
        using (action!)
        {
            Assert.True(action!.IsCurrent);
            var tile = f.Tiles.Get(102, 102); f.Tiles.Set(102, 102, tile with { FrameY = 150 });
            Assert.False(action.IsCurrent); Assert.False(action.TrySeal(oldInput!));
            Assert.Equal(2UL, f.Tools.ActionSerial);
        }
        f.Equip(0, 10); // Different canonical tool invalidates complete actor/full219 capture.
        Assert.False(oldInput!.IsCurrent);
        f.Apply(new ClientTileManipulationRuntimeCommand(f.Connection, new(0, 102, 102, 0, 0)));
        Assert.Equal(2L, world.CommittedToolActions); Assert.True(f.Tiles.Get(102, 102).IsActive);
    }

    private static Fixture LucyTwoHits()
    {
        var f = new Fixture(5095); f.Move(0, true); f.Tick();
        for (ulong serial = 1; serial <= 2; serial++)
        {
            f.Apply(new ClientTileManipulationRuntimeCommand(f.Connection, new(0, 102, 102, 1, 0)));
            Assert.Equal(serial, f.Tools.ActionSerial); Assert.True(f.Tiles.Get(102, 102).IsActive);
            int waits = 0;
            do { f.Tick(); waits++; }
            while (waits < 64 && (f.Tools.Clocks.Selected.ItemTime != 0 || f.Tools.Clocks.ToolTime != 0));
            Assert.InRange(waits, 1, 63);
        }
        Assert.Equal(0, f.Tools.Clocks.Selected.ItemTime); Assert.Equal(0, f.Tools.Clocks.ToolTime);
        return f;
    }

    private static TerrariaLucyAxeMessageState1458 LucyAtPose(Fixture f, byte source)
    {
        Assert.NotNull(f.Tools.SourceStagePose); var pose = f.Tools.SourceStagePose!.Value;
        return new(source, 173, (pose.Player.ControlFlags & 64) != 0 ? 7f : -7f, -7f,
            (int)(pose.Player.PositionX + pose.Width * .5f), (int)pose.Player.PositionY);
    }
    private static (TerrariaConnectionOutboundQueue Sender, TerrariaConnectionOutboundQueue Peer) LucyPeers(Fixture f)
    {
        var sender = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(4096, 1 << 20, 8192));
        var peer = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(4096, 1 << 20, 8192));
        var remote = new ConnectionHandle(GameCommandSourceId.FromConnection(98003), f.Connection.Player);
        Assert.True(f.TileReplication.TryRegister(f.Connection.Source, sender));
        Assert.True(f.TileReplication.TryRegister(remote.Source, peer));
        var spawn = new PlayerSpawnCommitRequest(f.Connection.Player.Slot, 0, 0, 0, 0, 0, 0, 0);
        f.TileReplication.PlayerSpawned(f.Connection, in spawn); f.TileReplication.PlayerSpawned(remote, in spawn);
        return (sender, peer);
    }
    private static TerrariaLucyAxeMessageState1458 DequeueLucy(TerrariaConnectionOutboundQueue queue)
    {
        Assert.True(LucyQueue(queue).TryRead(out var bytes)); var frame = LucyFrame(bytes.Bytes.ToArray());
        Assert.Equal(TerrariaLucyAxeMessageDecodeResult1458.Decoded, TerrariaLucyAxeMessageCodec1458.TryDecode(in frame, out var state));
        return state;
    }
    private static byte[] DrainLucyFrames(TerrariaConnectionOutboundQueue queue)
    {
        var ids = new List<byte>(); while (LucyQueue(queue).TryRead(out var frame)) ids.Add(frame.Bytes.Span[2]); return ids.ToArray();
    }
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_queue")]
    private static extern ref BoundedOutboundQueue LucyQueue(TerrariaConnectionOutboundQueue queue);
    private static TerrariaFrame LucyFrame(byte[] bytes) => new(checked((ushort)bytes.Length), bytes[2], new ReadOnlySequence<byte>(bytes), new ReadOnlySequence<byte>(bytes.AsMemory(3)));
    private static TerrariaFrame LucySimpleFrame(TerrariaMessageId id, byte[] bytes) => new(checked((ushort)(bytes.Length + 3)), (byte)id, ReadOnlySequence<byte>.Empty, new ReadOnlySequence<byte>(bytes));
    private static VanillaUnifiedRandom1458 CurrentLucyRandom(Fixture f) => LucyRandomOwner(f.Players) ?? throw new InvalidOperationException("Missing owned local stream.");
    private static VanillaUnifiedRandom1458 ReadLucyRandom(JsonElement element)
    {
        var value = new VanillaUnifiedRandom1458(0); var source = element.GetProperty("state").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        Assert.Equal(56, source.Length); source.CopyTo(LucyRandomState(value), 0);
        LucyRandomCursor(value) = element.GetProperty("cursor").GetUInt32(); return value;
    }
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "playerUpdateRandom")]
    private static extern ref VanillaUnifiedRandom1458? LucyRandomOwner(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "seedArray")]
    private static extern ref int[] LucyRandomState(VanillaUnifiedRandom1458 owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inext")]
    private static extern ref uint LucyRandomCursor(VanillaUnifiedRandom1458 owner);
    private sealed class LucySegment : ReadOnlySequenceSegment<byte>
    {
        internal LucySegment(ReadOnlyMemory<byte> memory) => Memory = memory;
        internal LucySegment Append(ReadOnlyMemory<byte> memory)
        { var next = new LucySegment(memory) { RunningIndex = RunningIndex + Memory.Length }; Next = next; return next; }
    }
    private static PlayerBootstrapFrameSink LucyBootstrap(GameCommandSourceId source) => new(new PlayerSlotPool(1),
        new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(32, 8192, 2048)),
        PlayerBootstrapPacketSet.CreateForTesting(new byte[] { 3, 0, (byte)TerrariaMessageId.WorldData }, Array.Empty<ReadOnlyMemory<byte>>(), new byte[] { 3, 0, (byte)TerrariaMessageId.PlayerSpawnSelf }), source, new LucySpawn());
    private sealed class LucySpawn : IPlayerSpawnCommitIngress
    {
        public bool TryPost(GameCommandSourceId source, PlayerJoinSession session, in PlayerSpawnCommitRequest request) => session.TryCommitSpawn(request.ClaimedSlot) == PlayerSpawnCommitResult.Committed;
    }
    private sealed class LucyPass : ITerrariaFrameSink { public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame) => TerrariaFrameSinkResult.Continue; }
    private sealed class LucyIngress : ITileNetworkIngress, ILucyAxeMessageIngress1458
    {
        internal int Count; internal ConnectionHandle Connection; internal TerrariaLucyAxeMessageState1458 State;
        public bool TryPost(ConnectionHandle connection, in TerrariaTileManipulationState state) => false;
        public bool TryPostLiquid(ConnectionHandle connection, in TerrariaLiquidState state) => false;
        public bool TryPostLucy(ConnectionHandle connection, in TerrariaLucyAxeMessageState1458 state)
        { Count++; Connection = connection; State = state; return true; }
    }
}
