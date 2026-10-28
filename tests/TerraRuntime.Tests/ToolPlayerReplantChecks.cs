using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;
using Xunit;

namespace TerraRuntime.Tests.ToolPlayer;

internal static partial class ToolPlayerBoundaryChecks
{
    internal static void RunReplant()
    {
        ReplantOriginalChronology();
        ReplantOrderedIngress();
        ReplantClientDenial();
        ReplantIndependentRoleRandoms();
        ReplantStalePreparation();
        ReplantReentryPublication();
    }

    private static void ReplantOriginalChronology()
    {
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using var input = typeof(ToolPlayerBoundaryChecks).Assembly.GetManifestResourceStream("ToolReplantSource" + platform + "1458")
                ?? throw new InvalidOperationException("Missing independent original regrowth tree row38.");
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var doc = JsonDocument.Parse(gzip); var row = doc.RootElement;
            Assert.Equal(5295, row.GetProperty("item").GetInt32());
            Assert.True(row.GetProperty("setup").GetBoolean());
            var frames = row.GetProperty("frames").EnumerateArray().Select(x => Convert.FromHexString(x.GetString()!)).ToArray();
            Assert.Equal(new byte[] { 17, 17, 17, 17, 79 }, frames.Select(x => x[2]).ToArray());
            for (int i = 0; i < 3; i++) Assert.Equal((short)1, BinaryPrimitives.ReadInt16LittleEndian(frames[i].AsSpan(8)));
            Assert.Equal((short)0, BinaryPrimitives.ReadInt16LittleEndian(frames[3].AsSpan(8)));
            Assert.Equal("0E004F1A00450014000000000201", Convert.ToHexString(frames[4]));
            Assert.Equal((short)20, BinaryPrimitives.ReadInt16LittleEndian(frames[4].AsSpan(7)));
            Assert.Equal((byte)0, frames[4][11]); Assert.Equal((byte)2, frames[4][12]); Assert.Equal((byte)1, frames[4][13]);
            var final = row.GetProperty("steps")[row.GetProperty("steps").GetArrayLength() - 1].GetProperty("after");
            Assert.Equal(12, final.GetProperty("itemTime").GetInt32());
            Assert.Equal(12, final.GetProperty("itemTimeMax").GetInt32());
        }
    }

    private static void ReplantOrderedIngress()
    {
        using var f = ReplantThreeHits(); var queues = LucyPeers(f); var image = f.CaptureInventory();
        var before = CurrentLucyRandom(f).Clone();
        f.Apply(new ClientTileManipulationRuntimeCommand(f.Connection, new(0, 102, 102, 0, 0)));
        Assert.Equal(3UL, f.Tools.ActionSerial); Assert.Equal((ushort)5, f.Tiles.Get(102, 102).Type);
        Assert.True(CurrentLucyRandom(f).HasSameState(before)); // Missing79 cannot commit an optimistic local kill.
        var report = ReplantReport();
        f.Apply(new ClientPlaceObjectRuntimeCommand(f.Connection, report));
        Assert.Equal(4UL, f.Tools.ActionSerial); ReplantAssertSapling(f, 36);
        Assert.Equal(12, f.Tools.Clocks.Selected.ItemTime); Assert.Equal(12, f.Tools.Clocks.Selected.ItemTimeMax);
        f.AssertInventory(image); // No acorn debit or private funding minted by item5295.
        before.NextDouble(); before.Next(3); // Target HitTile then CLIENT CanPlace, crit was already frame-owned.
        Assert.True(CurrentLucyRandom(f).HasSameState(before));
        var ids = DrainLucyFrames(queues.Peer);
        Assert.Equal(1, ids.Count(x => x == 79));
        Assert.True(Array.IndexOf(ids, (byte)17) >= 0);
        Assert.True(Array.IndexOf(ids, (byte)17) < Array.IndexOf(ids, (byte)79));
        Assert.Equal(0, queues.Sender.QueuedFrames);
        f.Apply(new ClientPlaceObjectRuntimeCommand(f.Connection, report));
        Assert.DoesNotContain((byte)79, DrainLucyFrames(queues.Peer));
        Assert.Equal(4UL, f.Tools.ActionSerial); f.AssertInventory(image);
    }

    private static void ReplantClientDenial()
    {
        using var f = ReplantThreeHits(); var queues = LucyPeers(f); var image = f.CaptureInventory();
        // Configured represented source anchor denial, not an independently captured original row.
        f.Tiles.Set(102, 103, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        f.Tick(); // New ordinary source stage binds the changed world before the measured command.
        int waits = 0;
        while (waits++ < 64 && (f.Tools.Clocks.Selected.ItemTime != 0 || f.Tools.Clocks.ToolTime != 0)) f.Tick();
        var before = CurrentLucyRandom(f).Clone();
        f.Apply(new ClientTileManipulationRuntimeCommand(f.Connection, new(0, 102, 102, 0, 0)));
        Assert.Equal(4UL, f.Tools.ActionSerial);
        Assert.False(f.Tiles.Get(102, 102).IsActive); Assert.NotEqual((ushort)20, f.Tiles.Get(102, 101).Type);
        Assert.Equal(12, f.Tools.Clocks.Selected.ItemTime); f.AssertInventory(image);
        before.NextDouble(); before.Next(3); // CanPlace consumes its choice despite invalid anchor.
        Assert.True(CurrentLucyRandom(f).HasSameState(before));
        Assert.DoesNotContain((byte)79, DrainLucyFrames(queues.Peer));
    }

    private static void ReplantIndependentRoleRandoms()
    {
        // Detached source-role boundary: the CLIENT and dedicated SERVER working
        // images can disagree. This proves the existing concrete role factory,
        // not a new wire permission or a claim that this denial was source-captured.
        foreach (bool serverDenies in new[] { false, true })
        {
            using var f = new Fixture(5295); var owner = Composition(f.State).WorldTileAuthority;
            f.Tiles.Set(120, 101, default); f.Tiles.Set(120, 102, default);
            f.Tiles.Set(120, 103, new WorldTile { Type = 2, Flags = WorldTileFlags.Active });
            var clientImage = new ToolWorldTileOverlay1458(f.Tiles);
            var serverImage = new ToolWorldTileOverlay1458(f.Tiles);
            if (serverDenies)
            {
                var invalid = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
                Assert.True(serverImage.TryStageChanges(new (int X, int Y, WorldTile After)[] { (120, 103, invalid) }));
            }
            Assert.True(owner.TryCreateToolMutationPlan(serverImage, out var world)); Assert.NotNull(world);
            using (world!)
            {
                Assert.True(owner.TryCaptureToolTreeWorld(out var context)); Assert.NotNull(context);
                var localRandom = new VanillaUnifiedRandom1458(31); var localBefore = localRandom.Clone();
                var serverBefore = world!.SourceRandom.Clone();
                Assert.True(owner.TryCreateToolSourcePass(clientImage, localRandom, true, false, context!.Facts, [], null, out var client));
                Assert.True(owner.TryCreateToolSourcePass(serverImage, world.SourceRandom, false, false, context.Facts, [], world, out var server));
                Assert.NotNull(client); Assert.NotNull(server);
                Assert.True(client!.TryReplantTree(120, 102, 1, out var local, 2)); Assert.True(local.Placed);
                Assert.Equal((sbyte)2, local.Report.Random);
                Assert.True(server!.TryReplantTree(120, 102, 1, out var remote, local.Report.Random));
                Assert.Equal(!serverDenies, remote.Placed);
                Assert.False(localRandom.HasSameState(localBefore));
                Assert.False(world.SourceRandom.HasSameState(serverBefore));
                // The server still advances its OWN draw before overriding only
                // the accepted style choice with the bounded source79 value.
                localBefore.Next(3); serverBefore.Next(3);
                Assert.True(localRandom.HasSameState(localBefore));
                Assert.True(world.SourceRandom.HasSameState(serverBefore));
                Assert.True(clientImage.TryRead(120, 102, out var afterClient));
                Assert.True(serverImage.TryRead(120, 102, out var afterServer));
                Assert.Equal((ushort)20, afterClient.Type);
                Assert.Equal(serverDenies ? (ushort)0 : (ushort)20, afterServer.Type);
                Assert.False(f.Tiles.Get(120, 102).IsActive); // No live writes or callbacks during preparation.
            }
        }
        // Explicit CLIENT CanPlace denial also consumes one local draw and emits no79.
        using (var f = new Fixture(5295))
        {
            var owner = Composition(f.State).WorldTileAuthority;
            f.Tiles.Set(120, 102, default); f.Tiles.Set(120, 103, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            var image = new ToolWorldTileOverlay1458(f.Tiles); var random = new VanillaUnifiedRandom1458(41); var before = random.Clone();
            Assert.True(owner.TryCaptureToolTreeWorld(out var context)); Assert.NotNull(context);
            Assert.True(owner.TryCreateToolSourcePass(image, random, true, false, context!.Facts, [], null, out var client));
            Assert.True(client!.TryReplantTree(120, 102, 1, out var denied)); Assert.False(denied.Placed);
            before.Next(3); Assert.True(random.HasSameState(before)); Assert.False(f.Tiles.Get(120, 102).IsActive);
        }
    }

    private static void ReplantStalePreparation()
    {
        foreach (bool inventoryChange in new[] { false, true })
        {
            using var f = ReplantThreeHits(); var world = Composition(f.State).WorldTileAuthority;
            f.Apply(new ClientTileManipulationRuntimeCommand(f.Connection, new(0, 102, 102, 0, 0)));
            var report = ReplantReport();
            Assert.Equal(ToolReportObservationResult1458.Retained, f.Players.ObserveToolObject(f.Connection, in report, out var input));
            Assert.NotNull(input); Assert.True(f.Players.TryPrepareToolAttempt(f.Connection, out var player));
            Assert.True(world.TryPrepareToolActionAt(player!, 102, 102, out var action)); Assert.NotNull(action);
            using (action!)
            {
                Assert.True(action!.IsCurrent); Assert.Equal(ToolWireMatch1458.Complete, action.Match(input!));
                if (inventoryChange) f.Equip(1, 9);
                else f.Tiles.Set(102, 103, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
                Assert.False(action.IsCurrent); Assert.False(action.TrySeal(input!));
                Assert.Equal(3L, world.CommittedToolActions); Assert.Equal((ushort)5, f.Tiles.Get(102, 102).Type);
            }
        }
    }

    private static void ReplantReentryPublication()
    {
        var sink = new ReplantDropSink(); using var f = ReplantThreeHits(sink); var queues = LucyPeers(f);
        var image = f.CaptureInventory(); var report = ReplantReport(); int callbacks = 0;
        sink.OnDrop = () =>
        {
            callbacks++; Assert.Equal(4UL, f.Tools.ActionSerial); ReplantAssertSapling(f, 36); f.AssertInventory(image);
            f.Tiles.Set(102, 101, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            // An observer reentering the same79 cannot mint a second action or
            // cause stale accepted-footprint79 to overwrite its intervening edit.
            f.Apply(new ClientPlaceObjectRuntimeCommand(f.Connection, report));
        };
        f.Apply(new ClientTileManipulationRuntimeCommand(f.Connection, new(0, 102, 102, 0, 0)));
        f.Apply(new ClientPlaceObjectRuntimeCommand(f.Connection, report));
        Assert.Equal(1, callbacks); Assert.Equal(4UL, f.Tools.ActionSerial);
        Assert.Equal((ushort)1, f.Tiles.Get(102, 101).Type); f.AssertInventory(image);
        Assert.DoesNotContain((byte)79, DrainLucyFrames(queues.Peer));
    }

    private static Fixture ReplantThreeHits(IWorldItemStateCommitSink? sink = null)
    {
        var f = new Fixture(5295, sink); f.Move(0, true); f.Tick();
        for (ulong serial = 1; serial <= 3; serial++)
        {
            f.Apply(new ClientTileManipulationRuntimeCommand(f.Connection, new(0, 102, 102, 1, 0)));
            Assert.Equal(serial, f.Tools.ActionSerial); Assert.Equal((ushort)5, f.Tiles.Get(102, 102).Type);
            int wait = 0;
            do { f.Tick(); wait++; }
            while (wait < 64 && (f.Tools.Clocks.Selected.ItemTime != 0 || f.Tools.Clocks.ToolTime != 0));
            Assert.InRange(wait, 1, 63);
        }
        return f;
    }
    private static TerrariaPlaceObjectState ReplantReport() => new(102, 102, 20, 0, 0, 2, true);
    private static void ReplantAssertSapling(Fixture f, short frameX)
    {
        var upper = f.Tiles.Get(102, 101); var lower = f.Tiles.Get(102, 102);
        Assert.True(upper.IsActive); Assert.True(lower.IsActive);
        Assert.Equal((ushort)20, upper.Type); Assert.Equal((ushort)20, lower.Type);
        Assert.Equal(frameX, upper.FrameX); Assert.Equal(frameX, lower.FrameX);
        Assert.Equal((short)0, upper.FrameY); Assert.Equal((short)18, lower.FrameY);
    }
    private sealed class ReplantDropSink : IWorldItemStateCommitSink
    {
        internal Action? OnDrop;
        public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot snapshot, int? reservationOffer = null, GameCommandSourceId excludedSource = default)
        {
            if (kind != WorldItemStateCommitKind.Drop || OnDrop is not { } action) return;
            OnDrop = null; action(); // Consume before callback/reentry; no mutable world-item store access.
        }
    }
}
