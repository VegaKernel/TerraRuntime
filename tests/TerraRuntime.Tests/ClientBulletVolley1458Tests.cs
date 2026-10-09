using System.Buffers;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Network;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class ClientBulletVolley1458Tests
{
    [Fact]
    public void Original_report_keys_complete_one_atomic_use_and_publish_every_pressure_birth()
    {
        using var stream = typeof(ClientBulletVolley1458Tests).Assembly.GetManifestResourceStream("RangedSpawnBatch1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var source = JsonDocument.Parse(gzip);
        int cases = 0;
        foreach (var row in source.RootElement.GetProperty("rows").EnumerateArray())
        {
            cases++;
            using var fixture = new Fixture(row.GetProperty("weapon").GetInt16(), row.GetProperty("poolMode").GetString()!);
            foreach (var change in row.GetProperty("generationChanges").EnumerateArray())
                ProjectileSpawnBatch1458Tests.SetGeneration(fixture.Store, change.GetProperty("slot").GetInt32(),
                    change.GetProperty("before").GetUInt64());
            string[] hex = row.GetProperty("frames").EnumerateArray().Skip(2).Select(x => x.GetString()!).ToArray();
            TerrariaProjectileUpdateState[] packets = hex.Select(Decode).ToArray();
            var beforeRandom = fixture.Random.Clone();
            int beforeActive = fixture.Store.ActiveCount;
            for (int index = 0; index < packets.Length; index++)
            {
                Assert.True(fixture.Authority.TryApply(new ClientProjectileUpdateRuntimeCommand(fixture.Connection, packets[index])));
                if (index == packets.Length - 1) continue;
                Assert.Equal(beforeActive, fixture.Store.ActiveCount);
                Assert.Equal(20, fixture.Ammo());
                Assert.Equal(0, fixture.Authority.PromotedClientProjectileSpawns);
                Assert.True(fixture.Random.HasSameState(beforeRandom));
                Assert.Equal(0, fixture.Outbound.QueuedFrames);
            }
            Assert.Equal(packets.Length, fixture.Authority.PromotedClientProjectileSpawns);
            Assert.Equal(row.GetProperty("afterAmmo").GetInt32(), fixture.Ammo());
            Assert.Equal(row.GetProperty("next").GetInt32(), fixture.Random.Clone().Next());
            var wire = new List<string>();
            var queue = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
                .GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Outbound)!;
            while (queue.TryRead(out var frame)) wire.Add(Convert.ToHexString(frame.Bytes.Span));
            Assert.Equal(hex, wire);
            foreach (var change in row.GetProperty("generationChanges").EnumerateArray())
            {
                Assert.True(fixture.Store.TryGetActive((ushort)change.GetProperty("slot").GetInt32(), out var final));
                Assert.Equal(change.GetProperty("after").GetUInt64(), final.Handle.Generation.Value);
                Assert.True(fixture.Store.IsCombatTrusted(final.Handle));
            }
            foreach (var group in packets.GroupBy(p => p.Key.ProjectileIndex))
            {
                var last = group.Last();
                Assert.True(fixture.Replication.WireIdentities.TryResolve(last.Key, out var handle));
                Assert.True(fixture.Store.TryGet(handle, out _));
                foreach (var overwritten in group.SkipLast(1))
                    Assert.False(fixture.Replication.WireIdentities.TryResolve(overwritten.Key, out _));
            }
        }
        Assert.Equal(12, cases);
    }

    [Fact]
    public void Coupled_source_launches_reach_authoritative_use_with_one_debit_and_exact_cursor()
    {
        using var stream = typeof(ClientBulletVolley1458Tests).Assembly.GetManifestResourceStream("RangedBulletLaunch1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var source = JsonDocument.Parse(gzip);
        int cases = 0;
        foreach (var row in source.RootElement.GetProperty("launches").EnumerateArray())
        {
            cases++;
            byte prefix = row.TryGetProperty("prefix", out var prefixValue) ? prefixValue.GetByte() : (byte)0;
            using var fixture = new Fixture(row.GetProperty("weapon").GetInt16(), "one-free", prefix,
                row.GetProperty("ammo").GetInt16(), row.GetProperty("initialAmmo").GetInt16(), row.GetProperty("seed").GetInt32());
            var buffs = new List<BuffTypeId>(2);
            if (row.GetProperty("ammoBox").GetBoolean()) buffs.Add(new(93));
            if (row.GetProperty("ammoPotion").GetBoolean()) buffs.Add(new(112));
            Assert.True(fixture.Players.TryApply(new PlayerBuffTypesRuntimeCommand(fixture.Connection,
                new PlayerBuffTypesCommitRequest(fixture.Connection.Player.Slot, buffs.ToArray()))));
            Assert.Equal(1, fixture.Players.AppliedBuffSnapshots);
            int index = 0;
            foreach (var shot in row.GetProperty("shots").EnumerateArray())
            {
                var position = shot.GetProperty("position");
                var velocity = shot.GetProperty("velocity");
                var packet = new TerrariaProjectileUpdateState(new(0, checked((ushort)(701 + index++)), 1),
                    shot.GetProperty("type").GetInt16(), position.GetProperty("X").GetSingle(), position.GetProperty("Y").GetSingle(),
                    velocity.GetProperty("X").GetSingle(), velocity.GetProperty("Y").GetSingle(),
                    0, 0, 0, 0, shot.GetProperty("damage").GetInt16(), shot.GetProperty("knockBack").GetSingle(), 0);
                Assert.True(fixture.Authority.TryApply(new ClientProjectileUpdateRuntimeCommand(fixture.Connection, packet)));
            }
            Assert.True(index == fixture.Authority.PromotedClientProjectileSpawns,
                $"weapon={row.GetProperty("weapon")}, seed={row.GetProperty("seed")}, prefix={prefix}, case={cases}, promoted={fixture.Authority.PromotedClientProjectileSpawns}, rejected={fixture.Authority.RejectedClientProjectileProvenance}");
            Assert.Equal(row.GetProperty("afterAmmo").GetInt32(), fixture.Ammo());
            Assert.Equal(row.GetProperty("next").GetInt32(), fixture.Random.Clone().Next());
            Assert.Equal(index, fixture.Outbound.QueuedFrames);
        }
        Assert.Equal(92, cases);
    }

    [Fact]
    public void First_use_callback_observes_whole_adoption_and_preserves_a_newer_replacement()
    {
        using var stream = typeof(ClientBulletVolley1458Tests).Assembly.GetManifestResourceStream("RangedSpawnBatch1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var source = JsonDocument.Parse(gzip);
        var row = source.RootElement.GetProperty("rows").EnumerateArray().First(x =>
            x.GetProperty("weapon").GetInt32() == 534 && x.GetProperty("poolMode").GetString() == "full-old-short");
        var packets = row.GetProperty("frames").EnumerateArray().Skip(2).Select(x => Decode(x.GetString()!)).ToArray();
        foreach (bool replace in new[] { false, true })
        {
            using var fixture = new Fixture(534, "full-old-short");
            ProjectileSnapshot? replacement = null;
            int callbacks = 0;
            fixture.Events.OnEquipment = () =>
            {
                callbacks++;
                Assert.Equal(packets.Length, fixture.Authority.PromotedClientProjectileSpawns);
                Assert.Equal(row.GetProperty("afterAmmo").GetInt32(), fixture.Ammo());
                Assert.Equal(row.GetProperty("next").GetInt32(), fixture.Random.Clone().Next());
                foreach (var packet in packets)
                {
                    Assert.True(fixture.Replication.WireIdentities.TryResolve(packet.Key, out var handle));
                    Assert.True(fixture.Store.TryGet(handle, out _));
                    Assert.True(fixture.Store.IsCombatTrusted(handle));
                }
                using var sequentialStream = typeof(ClientBulletVolley1458Tests).Assembly
                    .GetManifestResourceStream("ShotgunSequentialShoot1458")!;
                using var sequentialGzip = new GZipStream(sequentialStream, CompressionMode.Decompress);
                using var sequential = JsonDocument.Parse(sequentialGzip);
                var secondUse = sequential.RootElement.GetProperty("shots")[1];
                Assert.Equal(row.GetProperty("next").GetInt32(), secondUse.GetProperty("nextBefore").GetInt32());
                int nestedIndex = 0;
                foreach (var sourceShot in secondUse.GetProperty("shots").EnumerateArray())
                {
                    var position = sourceShot.GetProperty("position");
                    var velocity = sourceShot.GetProperty("velocity");
                    var nested = packets[0] with
                    {
                        Key = new(0, checked((ushort)(900 + nestedIndex++)), 1),
                        PositionX = position.GetProperty("X").GetSingle(),
                        PositionY = position.GetProperty("Y").GetSingle(),
                        VelocityX = velocity.GetProperty("X").GetSingle(),
                        VelocityY = velocity.GetProperty("Y").GetSingle()
                    };
                    Assert.True(fixture.Authority.TryApply(new ClientProjectileUpdateRuntimeCommand(fixture.Connection, nested)));
                }
                Assert.Equal(packets.Length, fixture.Authority.PromotedClientProjectileSpawns);
                Assert.Equal(row.GetProperty("afterAmmo").GetInt32(), fixture.Ammo());
                Assert.Equal(row.GetProperty("next").GetInt32(), fixture.Random.Clone().Next());
                var pending = (Array)typeof(ProjectileAuthority).GetField("pendingBulletVolleys",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Authority)!;
                Assert.Null(pending.GetValue(fixture.Connection.Player.Slot.Value));
                if (!replace) return;
                Assert.True(fixture.Replication.WireIdentities.TryResolve(packets[0].Key, out var retired));
                Assert.True(fixture.Store.TryDespawn(retired, out _));
                var update = new ProjectileStateUpdate(new(14), 0, 600, 600, 3, 0, default, 0, 20, 2, 0);
                Assert.True(fixture.Store.TrySpawn(retired.Slot, update, out var newer));
                replacement = newer;
            };
            foreach (var packet in packets)
                Assert.True(fixture.Authority.TryApply(new ClientProjectileUpdateRuntimeCommand(fixture.Connection, packet)));
            Assert.Equal(1, callbacks);
            Assert.Equal(row.GetProperty("afterAmmo").GetInt32(), fixture.Ammo());
            Assert.Equal(row.GetProperty("next").GetInt32(), fixture.Random.Clone().Next());
            if (replacement is { } newActor)
            {
                Assert.True(fixture.Store.TryGet(newActor.Handle, out var current));
                Assert.Equal(newActor, current);
                Assert.True(fixture.Replication.WireIdentities.TryGetWireKey(newActor.Handle, out var key));
                Assert.Equal(new TerrariaProjectileKeyState(0, newActor.Handle.Slot,
                    checked((ushort)newActor.Handle.Generation.Value)), key);
                Assert.True(fixture.Replication.WireIdentities.TryResolve(key, out var owned));
                Assert.Equal(newActor.Handle, owned);
                Assert.Equal(2, fixture.Outbound.QueuedFrames); // New destroy/spawn, no stale birth journal.
            }
            else Assert.Equal(packets.Length, fixture.Outbound.QueuedFrames);
        }
    }

    [Fact]
    public void Partial_duplicate_and_stale_reports_never_adopt_a_partial_volley()
    {
        using var stream = typeof(ClientBulletVolley1458Tests).Assembly.GetManifestResourceStream("RangedSpawnBatch1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var source = JsonDocument.Parse(gzip);
        var row = source.RootElement.GetProperty("rows").EnumerateArray().First(x =>
            x.GetProperty("weapon").GetInt32() == 534 && x.GetProperty("poolMode").GetString() == "one-free");
        var packets = row.GetProperty("frames").EnumerateArray().Skip(2).Select(x => Decode(x.GetString()!)).ToArray();
        foreach (int mutation in new[] { 0, 1, 2, 3 })
        {
            using var fixture = new Fixture(534, "one-free");
            int active = fixture.Store.ActiveCount;
            var before = fixture.Random.Clone();
            Assert.True(fixture.Authority.TryApply(new ClientProjectileUpdateRuntimeCommand(fixture.Connection, packets[0])));
            Assert.True(fixture.Authority.TryApply(new ClientProjectileUpdateRuntimeCommand(fixture.Connection, packets[0])));
            var second = packets[1];
            switch (mutation)
            {
                case 0: fixture.SetItem(VanillaPlayerItemSlotCatalog.AmmoSlotStart, 97, 19); break;
                case 1: fixture.Random.Next(); before = fixture.Random.Clone(); break;
                case 2: second = second with { VelocityX = second.VelocityX + 1f }; break;
                case 3: fixture.Tick += 100; break;
            }
            Assert.True(fixture.Authority.TryApply(new ClientProjectileUpdateRuntimeCommand(fixture.Connection, second)));
            Assert.Equal(active, fixture.Store.ActiveCount);
            Assert.Equal(mutation == 0 ? 19 : 20, fixture.Ammo());
            Assert.Equal(0, fixture.Authority.PromotedClientProjectileSpawns);
            Assert.Equal(0, fixture.Outbound.QueuedFrames);
            Assert.True(fixture.Random.HasSameState(before));
            Assert.True(fixture.Authority.RejectedClientProjectileProvenance > 0);
        }

        // A real system spawn can take the first report's exact key while the volley remains detached.
        // This changes neither the captured player/inventory nor the retained source RNG.
        using (var fixture = new Fixture(534, "one-free"))
        {
            var key = packets[0].Key;
            ProjectileSpawnBatch1458Tests.SetGeneration(fixture.Store, key.ProjectileIndex, (ulong)key.Generation - 1);
            var before = fixture.Random.Clone();
            Assert.True(fixture.Authority.TryApply(new ClientProjectileUpdateRuntimeCommand(fixture.Connection, packets[0])));
            var state = new ProjectileStateUpdate(new(14), 0, 800, 600, 1, 0, default, 0, 20, 2, 0);
            Assert.True(fixture.Store.TrySpawn(key.ProjectileIndex, state, out var concurrent));
            Assert.True(fixture.Replication.WireIdentities.TryResolve(key, out var original));
            Assert.Equal(concurrent.Handle, original);
            int active = fixture.Store.ActiveCount;
            int queued = fixture.Outbound.QueuedFrames;
            foreach (var packet in packets.Skip(1))
                Assert.True(fixture.Authority.TryApply(new ClientProjectileUpdateRuntimeCommand(fixture.Connection, packet)));
            Assert.Equal(0, fixture.Authority.PromotedClientProjectileSpawns);
            Assert.Equal(20, fixture.Ammo());
            Assert.True(fixture.Random.HasSameState(before));
            Assert.Equal(active, fixture.Store.ActiveCount);
            Assert.Equal(queued, fixture.Outbound.QueuedFrames);
            Assert.True(fixture.Replication.WireIdentities.TryResolve(key, out var retained));
            Assert.Equal(concurrent.Handle, retained);
            Assert.True(fixture.Store.TryGet(concurrent.Handle, out var current));
            Assert.Equal(concurrent, current);
        }
    }

    private static TerrariaProjectileUpdateState Decode(string hex)
    {
        var buffer = new ReadOnlySequence<byte>(Convert.FromHexString(hex));
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref buffer, out var frame));
        Assert.Equal(TerrariaProjectileDecodeResult.Decoded, TerrariaProjectileDecoder.TryDecodeUpdate(in frame, out var state));
        Assert.Equal(0, buffer.Length);
        return state;
    }

    internal sealed class Fixture : IDisposable
    {
        internal readonly PlayerJoinSession Session;
        internal readonly ConnectionHandle Connection;
        internal readonly PlayerAuthority Players;
        internal readonly RuntimeProjectileReplicationRegistry Replication = new();
        internal readonly RuntimeProjectileStore Store;
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly ProjectileAuthority Authority;
        internal readonly Events Events = new();
        internal long Tick = 100;
        internal readonly TerrariaConnectionOutboundQueue Outbound = new(new OutboundQueueOptions(32, 16_384, 1_024));

        internal Fixture(short weapon, string mode, byte prefix = 0, short ammo = 97, short stack = 20, int seed = 0)
        {
            Random = new(seed);
            var slots = new PlayerSlotPool(1);
            Assert.True(slots.TryAcquireConnection(out var lease));
            Session = new(lease!);
            Session.ObserveWorldRequest();
            Session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(9951), Session.Handle);
            Players = new(Events, null);
            var spawn = new PlayerSpawnCommitRequest(Session.Slot, 25, 25, 0, 0, 0, 0, 0);
            Assert.True(Players.TryApply(new PlayerSpawnRuntimeCommand(Connection, Session, spawn)));
            Assert.True(Players.TryApply(new PlayerEquipmentRuntimeCommand(Connection,
                new PlayerEquipmentCommitRequest(Connection.Player.Slot, 0, 1, prefix, weapon, 0))));
            SetItem(VanillaPlayerItemSlotCatalog.AmmoSlotStart, ammo, stack);
            Assert.True(Players.TryApply(new PlayerMovementRuntimeCommand(Connection,
                new PlayerMovementCommitRequest(Connection.Player.Slot, 64, 0, 0, 0, 0, 400f, 400f,
                    false, 0f, 0f, false, 0, false, 0f, 0f, 0f, 0f, false, 0f, 0f))));
            var peer = new ConnectionHandle(GameCommandSourceId.FromConnection(9952),
                new PlayerHandle(new PlayerSlotId(1), new PlayerSessionGeneration(1)));
            Assert.True(Replication.TryRegister(peer.Source, Outbound));
            var peerSpawn = new PlayerSpawnCommitRequest(peer.Player.Slot, 25, 25, 0, 0, 0, 0, 0);
            Replication.PlayerSpawned(peer, in peerSpawn);
            var sink = new SetupSink(Replication);
            Store = ProjectileSpawnBatch1458Tests.CreatePool(mode, sink);
            sink.Enabled = true;
            Authority = new(Store, Players, new RuntimeNpcStore(), new RuntimePlayerSnapshotLookup(Players, null),
                null, Replication, () => Tick, projectileRandom: Random);
        }

        internal void SetItem(short slot, short type, short stack) => Assert.True(Players.TryApply(
            new PlayerEquipmentRuntimeCommand(Connection,
                new PlayerEquipmentCommitRequest(Connection.Player.Slot, slot, stack, 0, type, 0))));
        internal short Ammo()
        {
            Assert.True(Players.TryGetInventoryItem(Connection, VanillaPlayerItemSlotCatalog.AmmoSlotStart, out var ammo));
            return ammo.Stack;
        }
        public void Dispose() => Session.Dispose();
    }

    private sealed class SetupSink(IProjectileStateCommitSink inner) : IProjectileStateCommitSink
    {
        internal bool Enabled;
        public void ProjectileStateCommitted(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot)
        {
            if (Enabled) inner.ProjectileStateCommitted(kind, in snapshot);
        }
    }

    internal sealed class Events : IRuntimePlayerEventSink
    {
        internal Action? OnEquipment;
        public void PlayerEquipmentUpdated(ConnectionHandle connection, in PlayerEquipmentCommitRequest request)
        {
            var callback = OnEquipment;
            OnEquipment = null;
            callback?.Invoke();
        }
        public void PlayerAppearanceUpdated(ConnectionHandle connection, in PlayerAppearanceCommitRequest request) { }
        public void PlayerSpawned(ConnectionHandle connection, in PlayerSpawnCommitRequest request) { }
        public void PlayerMoved(ConnectionHandle connection, in PlayerMovementCommitRequest request) { }
        public void PlayerDisconnected(ConnectionHandle connection) { }
    }
}
