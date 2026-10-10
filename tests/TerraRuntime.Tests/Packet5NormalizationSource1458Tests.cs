using System.Reflection;
using System.IO.Compression;
using System.Buffers;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.Core.Npcs;
using System.Text.Json.Nodes;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Network;
using TerraRuntime.World;
using TerraRuntime.Gameplay.Items;

using Xunit;
namespace TerraRuntime.Tests;

// Original configured receive references are distinct from standard server and Windows Prefix subcall scopes.
public sealed class Packet5NormalizationSource1458Tests
{
    private static void Check(bool value, string detail) => Assert.True(value, detail);
    private static JsonNode Load(string name)
    {
        using var stream = typeof(Packet5NormalizationSource1458Tests).Assembly.GetManifestResourceStream(name)!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonNode.Parse(gzip)!;
    }

    [Fact]
    public void Original_receive_rows_adopt_canonical_inventory_and_cursor_before_observation()
    {
        var nextRows = Load("Packet5FamilyNormalizationNext1458")!["rows"]!.AsArray();
        var rows = Load("Packet5FamilyNormalization1458")["rows"]!.AsArray();
        foreach (var raw in rows)
        {
            var row = raw!;
            var spec = row["spec"]!;
            var nextRow = nextRows.Single(r => r!["spec"]!["Name"]!.GetValue<string>() == spec["Name"]!.GetValue<string>())!;
            using var f = new ReceiveFixture(spec["Player"]!.GetValue<int>(), spawn: true);
            f.Equip(0, 219, 0, 1, 0);
            f.Equip(54, 97, 0, 7, 0);
            f.Random = new(spec["Seed"]!.GetValue<int>());
            f.Players.BindReceiveEquipmentRandom(f.Random);
            ulong beforeInput = f.Member.ProjectileUseInputRevision;
            var expected = row["afterItem"]!["canonical"]!;
            f.Observer = () =>
            {
                Check(f.Players.TryGetInventoryItem(f.Connection.Player, 0, out var item) &&
                    item.Prefix.Value == expected["prefix"]!.GetValue<int>(), "observer normalized item");
                Check(f.Member.ProjectileUseInputRevision > beforeInput, "observer input adoption");
                Check(SameRandom(f.Random, row["afterRng"]!), "observer full56 adoption");
                Assert.Equal(nextRow["afterNext"]!.GetValue<int>(), f.Random.Clone().Next());
                Check(f.Players.AppliedEquipmentUpdates == 3, "observer counter adoption");
            };
            f.Equip(0, spec["Type"]!.GetValue<int>(), spec["Prefix"]!.GetValue<int>(), 1, 1);
            Check(f.Events == 3, "one receive publication");
            foreach (var slotRow in row["afterInventory"]!.AsArray())
            {
                int slot = slotRow!["slot"]!.GetValue<int>();
                var expectedItem = slotRow["item"]!;
                Check(f.Players.TryGetInventoryItem(f.Connection.Player, slot, out var item), "captured inventory slot");
                Check(item.ItemType.Value == expectedItem["type"]!.GetValue<int>() &&
                    item.Stack == expectedItem["stack"]!.GetValue<int>() && item.Prefix.Value == expectedItem["prefix"]!.GetValue<int>() &&
                    item.ItemFlags == (expectedItem["favorited"]!.GetValue<bool>() ? 1 : 0), "full inventory tuple");
            }
        }
        int admittedTies = 0;
        int excludedTies = 0;
        foreach (bool windows in new[] { false, true })
        {
            var tieRows = Load("Packet5AcceptanceTies1458")[windows ? "WindowsPrefix" : "LinuxGetData5"]!.AsArray();
            foreach (var raw in tieRows)
            {
                var row = raw!;
                int type = windows ? row["weapon"]!.GetValue<int>() : row["spec"]!["Type"]!.GetValue<int>();
                int requested = windows ? row["requested"]!.GetValue<int>() : row["spec"]!["Prefix"]!.GetValue<int>();
                bool excluded = requested != 0 && (type == 1319 || type == 5282);
                Assert.Equal(!excluded, VanillaItemPrefixNormalization1458.IsSupported(new(type), new(requested)));
                var pureRandom = new VanillaUnifiedRandom1458(0);
                var pureBefore = pureRandom.Clone();
                var status = VanillaItemPrefixNormalization1458.Resolve(new(type), new(requested), pureRandom.Next, windows, out var resolved);
                if (excluded)
                {
                    Assert.Equal(VanillaItemPrefixNormalizationStatus1458.Unsupported, status);
                    Assert.True(pureRandom.HasSameState(pureBefore));
                    excludedTies++;
                    continue;
                }
                Assert.Equal(VanillaItemPrefixNormalizationStatus1458.Resolved, status);
                int sourcePrefix = windows ? row["actual"]!.GetValue<int>() : row["afterItem"]!["canonical"]!["prefix"]!.GetValue<int>();
                bool sourceApplied = windows ? row["returned"]!.GetValue<bool>() : row["prefixReturnFromIndependentReplay"]!.GetValue<bool>();
                Assert.Equal(sourcePrefix, resolved.Prefix.Value);
                Assert.Equal(sourceApplied, resolved.Applied);
                Assert.True(SameRandom(pureRandom, row["afterRng"]!));
                using var f = new ReceiveFixture(0, true);
                f.Random = new(0);
                f.Players.BindReceiveEquipmentRandom(f.Random, windows);
                f.Equip(0, type, requested, 1, 0);
                Assert.Equal(1, f.Events);
                Assert.True(f.Players.TryGetInventoryItem(f.Connection.Player, 0, out var item));
                int expected = windows ? row["actual"]!.GetValue<int>() : row["afterItem"]!["canonical"]!["prefix"]!.GetValue<int>();
                Assert.Equal(expected, item.Prefix.Value);
                Assert.True(SameRandom(f.Random, row["afterRng"]!));
                // Windows is genuine Prefix subcall, not Windows wholeReceive5.
                if (windows) Assert.Equal(row["next"]!.GetValue<int>(), f.Random.Clone().Next());
                admittedTies++;
            }
        }
        Assert.Equal(194, admittedTies);
        Assert.Equal(16, excludedTies);
        // Genuine original server relay is compared only at its matching server caller.
        foreach (var raw in rows.Where(r => r!["actualNetMode"]!.GetValue<int>() == 2))
        {
            var row = raw!;
            using var bootstrap = new Packet5BootstrapSupport(row["spec"]!["Seed"]!.GetValue<int>());
            Assert.True(bootstrap.Players.TryGet(bootstrap.Connection, out var member));
            ulong input = member.ProjectileUseInputRevision;
            bootstrap.Observer = () =>
            {
                Assert.True(bootstrap.Players.TryGetInventoryItem(bootstrap.Connection.Player, 0, out var item));
                Assert.Equal(row["afterItem"]!["canonical"]!["prefix"]!.GetValue<int>(), item.Prefix.Value);
                Assert.True(SameRandom(bootstrap.Random, row["afterRng"]!));
                Assert.True(member.ProjectileUseInputRevision > input);
            };
            bootstrap.Report(Convert.FromHexString(row["incomingHex"]!.GetValue<string>()));
            Assert.Equal(1, bootstrap.Events);
            Assert.Equal(row["outboundFrames"]!.AsArray().Select(n => n!.GetValue<string>()), bootstrap.DrainPeer());
            Assert.Empty(bootstrap.DrainOwner());
            Assert.False(bootstrap.State.TryCapturePlayerSnapshot(new(new(31), new(1)), out _));
        }
    }
    [Fact]
    public void Prepared_receive_guards_and_reentry_preserve_newer_owners()
    {
        // Bounded resolver policy; original Prefix has an unbounded retry loop.
        int unsupportedOffers = 0;
        foreach (int type in new[] { 112, 157, 197, 517, 544, 556, 557, 683, 725, 1314, 1319, 1325, 2623, 3069, 4060, 5147, 5279, 5280, 5281, 5282, 5283, 5284, 5334, 5687, 5688 })
        {
            Assert.False(VanillaItemPrefixNormalization1458.IsSupported(new(type), new(82)));
            Assert.True(VanillaItemPrefixNormalization1458.IsSupported(new(type), new(0)));
            Assert.Equal(VanillaItemPrefixNormalizationStatus1458.Unsupported,
                VanillaItemPrefixNormalization1458.Resolve(new(type), new(82), _ => { unsupportedOffers++; return 0; }, false, out _));
            Assert.Equal(VanillaItemPrefixNormalizationStatus1458.Resolved,
                VanillaItemPrefixNormalization1458.Resolve(new(type), new(0), _ => { unsupportedOffers++; return 0; }, false, out var zero));
            Assert.Equal(default, zero);
        }
        Assert.Equal(0, unsupportedOffers);
        Assert.True(VanillaItemPrefixTable1458.TryGet(new(98), out var record));
        var family = VanillaItemPrefixTable1458.GetFamily(record.Family);
        int invalidIndex = -1;
        for (int i = 0; i < family.Length; i++)
            if (!VanillaItemPrefixTable1458.Accepts(in record, family[i], false))
            {
                invalidIndex = i;
                break;
            }
        Assert.True(invalidIndex >= 0);
        int offers = 0;
        int Rejecting(int bound)
        {
            offers++;
            return bound == 4 ? 1 : invalidIndex;
        }
        Assert.Equal(256, VanillaItemPrefixNormalization1458.MaximumAttempts);
        Assert.Equal(VanillaItemPrefixNormalizationStatus1458.BudgetExhausted,
            VanillaItemPrefixNormalization1458.Resolve(new(98), new(82), Rejecting, false, out var exhausted));
        Assert.Equal(default, exhausted);
        Assert.Equal(510, offers);
        Assert.Equal(VanillaItemPrefixNormalizationStatus1458.InvalidDraw,
            VanillaItemPrefixNormalization1458.Resolve(new(98), new(82), _ => -1, false, out _));

        foreach (int type in new[] { 112, 157, 197, 517, 544, 556, 557, 683, 725, 1314, 1319, 1325, 2623, 3069, 4060, 5147, 5279, 5280, 5281, 5282, 5283, 5284, 5334, 5687, 5688 })
        {
            using var f = new ReceiveFixture(0, true);
            f.Random = new(0);
            f.Players.BindReceiveEquipmentRandom(f.Random);
            var before = f.Random.Clone();
            var request = new PlayerEquipmentRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, 0, 1, 82, (short)type, 0));
            Assert.False(f.Players.TryPrepareReceivedEquipment(request, out var refused));
            Assert.Null(refused);
            Assert.True(f.Random.HasSameState(before));
            Assert.Equal(0, f.Events);
            // Unsupported source variant retains the explicit component report policy; it is not normalized-source fidelity.
            f.Equip(0, type, 82, 1, 0);
            Assert.True(f.Players.TryGetInventoryItem(f.Connection.Player, 0, out var legacy));
            Assert.Equal(82, legacy.Prefix.Value);
            Assert.True(f.Random.HasSameState(before));
            f.Equip(0, type, 0, 1, 0);
            Assert.True(f.Players.TryGetInventoryItem(f.Connection.Player, 0, out var neutral));
            Assert.Equal(0, neutral.Prefix.Value);
            Assert.True(f.Random.HasSameState(before));
        }
        // The receive producer also owns canonical pending inventory before an active member exists.
        using (var f = new ReceiveFixture(0, spawn: false))
        {
            f.Random = new(1458);
            f.Players.BindReceiveEquipmentRandom(f.Random);
            f.Equip(0, 98, 82, 1, 1);
            var store = (RuntimePlayerInventoryStore)typeof(PlayerAuthority).GetField("inventory", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Players)!;
            Check(store.TryGet(f.Connection, 0, out var pendingItem) && pendingItem.Prefix.Value == 47, "pre-spawn normalized item");
            Check(!f.Players.TryGet(f.Connection, out _), "pre-spawn does not fabricate member");
        }
        foreach (string stale in new[] { "item", "profile", "member", "rng" })
        {
            using var f = new ReceiveFixture(0, true);
            f.Random = new(1458);
            f.Players.BindReceiveEquipmentRandom(f.Random);
            var command = new PlayerEquipmentRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, 0, 1, 82, 98, 1));
            Check(f.Players.TryPrepareReceivedEquipment(command, out var token) && token is not null, "real receive preparation");
            if (stale == "item") f.Equip(1, 97, 0, 5, 0);
            if (stale == "profile") f.Equip(59, 90, 0, 1, 0);
            if (stale == "member") f.Players.TryApply(new PlayerItemAnimationRuntimeCommand(f.Connection, .25f, 3));
            if (stale == "rng") f.Random.Next();
            var before = f.Random.Clone();
            int events = f.Events;
            Check(!token!.TryAdoptUnpublished(), "stale receive refused: " + stale);
            Check(f.Random.HasSameState(before) && f.Events == events, "stale has no cursor/publication writes");
        }
        using (var f = new ReceiveFixture(0, false))
        {
            f.Random = new(1458);
            f.Players.BindReceiveEquipmentRandom(f.Random);
            var command = new PlayerEquipmentRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, 59, 1, 0, 90, 0));
            Check(f.Players.TryPrepareReceivedEquipment(command, out var token) && token!.TryAdoptUnpublished(), "pre-spawn profile adopted");
            var profiles = (RuntimePlayerTransferProfileStore)typeof(PlayerAuthority).GetField("transferProfiles", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Players)!;
            var newer = new PlayerEquipmentCommitRequest(f.Connection.Player.Slot, 59, 1, 0, 92, 1);
            Check(profiles.TrySetEquipment(f.Connection, in newer), "direct newer profile write");
            var before = f.Random.Clone();
            Check(!token!.TryPublish() && f.Events == 0 && f.Random.HasSameState(before), "stale pre-spawn publication suppressed without rewind");
        }
        using (var f = new ReceiveFixture(0, true))
        {
            f.Random = new(1458);
            f.Players.BindReceiveEquipmentRandom(f.Random);
            f.Observer = () =>
            {
                f.Observer = null;
                f.Equip(0, 39, 36, 1, 1);
            };
            f.Equip(0, 98, 82, 1, 1);
            Check(f.Players.TryGetInventoryItem(f.Connection.Player, 0, out var item) && item.ItemType.Value == 39 && item.Prefix.Value == 36, "new observer report survives");
            Check(f.Events == 2, "observer publications exactly once");
            var before = f.Random.Clone();
            ulong input = f.Member.ProjectileUseInputRevision;
            f.Equip(0, 39, 36, 1, 1);
            Check(f.Random.HasSameState(before) && f.Member.ProjectileUseInputRevision > input, "duplicate still invalidates input without fabricated RNG");
        }
    }
    [Fact]
    public void Same_value_reports_cancel_pending_use_and_unknown_prefix_policy_stays_distinct()
    {
        using var source = PendingBulletPlayerPhase1458Tests.Source();
        foreach (int mutation in new[] { 0, 1, 2 })
        {
            using var f = new PendingBulletPlayerPhase1458Tests.Fixture(PendingBulletPlayerPhase1458Tests.Row(source));
            f.Players().BindReceiveEquipmentRandom(f.Random, windowsArithmetic: OperatingSystem.IsWindows());
            f.Report(0);
            ulong input = f.Member().ProjectileUseInputRevision;
            if (mutation == 0) f.SetItem(0, 534, 1);
            if (mutation == 1) f.SetItem(54, 97, 20);
            if (mutation == 2) f.State.Apply(new PlayerEquipmentRuntimeCommand(f.Connection,
                new(f.Connection.Player.Slot, 1, 1, 82, 98, 1)));
            Assert.True(f.Member().ProjectileUseInputRevision > input);
            var afterReceive = f.Random.Clone();
            f.Complete();
            Assert.Equal(0, f.Store.ActiveCount);
            Assert.Equal(20, f.Ammo());
            Assert.True(f.Random.HasSameState(afterReceive));
            Assert.Equal(0, f.Outbound.QueuedFrames);
        }
        // Explicit ingress clipping policy differs from source retaining unknown byte prefixes.
        foreach (byte requestPrefix in new byte[] { 98, 255 })
        {
            using var f = new ReceiveFixture(0, true);
            f.Random = new(1458);
            f.Players.BindReceiveEquipmentRandom(f.Random);
            var before = f.Random.Clone();
            var ingress = new RuntimePlayerEquipmentIngress(new ApplyingIngress(f.Players));
            var request = new PlayerEquipmentCommitRequest(f.Connection.Player.Slot, 0, 1, requestPrefix, 98, 1);
            Assert.True(ingress.TryPost(f.Connection, in request));
            Assert.True(f.Players.TryGetInventoryItem(f.Connection.Player, 0, out var item));
            Assert.Equal(0, item.Prefix.Value);
            Assert.True(f.Random.HasSameState(before));
        }
    }
    static bool SameRandom(VanillaUnifiedRandom1458 random, JsonNode expected)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        uint cursor = (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(random)!;
        var state = (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(random)!;
        return cursor == expected["cursor"]!.GetValue<uint>() && state.SequenceEqual(expected["state"]!.AsArray().Select(n => n!.GetValue<int>()));
    }
    sealed class ReceiveFixture : IRuntimePlayerEventSink, IDisposable
    {
        private readonly List<PlayerJoinSession> sessions = [];
        internal readonly PlayerAuthority Players;
        internal ConnectionHandle Connection;
        internal VanillaUnifiedRandom1458 Random = new(0);
        internal Action? Observer;
        internal int Events;
        internal RuntimePlayerMember Member
        {
            get
            {
                Players.TryGet(Connection, out var member);
                return member;
            }
        }
        internal ReceiveFixture(int slot, bool spawn)
        {
            var slots = new PlayerSlotPool(slot + 1);
            for (int i = 0; i <= slot; i++)
            {
                if (!slots.TryAcquireConnection(out var lease)) throw new Exception("lease");
                sessions.Add(new(lease!));
            }
            var session = sessions[^1];
            session.ObserveWorldRequest();
            session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(1), session.Handle);
            Players = new(this, new WorldTileStore(new WorldDimensions(400, 300)));
            if (spawn) Players.TryApply(new PlayerSpawnRuntimeCommand(Connection, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
        }
        internal void Equip(short slot, int type, int prefix, short stack, byte flags) => Players.TryApply(new PlayerEquipmentRuntimeCommand(Connection, new(Connection.Player.Slot, slot, stack, (byte)prefix, (short)type, flags)));
        public void PlayerEquipmentUpdated(ConnectionHandle connection, in PlayerEquipmentCommitRequest request)
        {
            Events++;
            Observer?.Invoke();
        }
        public void PlayerAppearanceUpdated(ConnectionHandle connection, in PlayerAppearanceCommitRequest request) { }
        public void PlayerSpawned(ConnectionHandle connection, in PlayerSpawnCommitRequest request) { }
        public void PlayerMoved(ConnectionHandle connection, in PlayerMovementCommitRequest request) { }
        public void PlayerDisconnected(ConnectionHandle connection) { }
        public void Dispose()
        {
            foreach (var session in sessions) session.Dispose();
        }
    }

    private sealed class ApplyingIngress(PlayerAuthority players) : IGameCommandIngress<RuntimeCommand>
    {
        public bool TryPost(GameCommandSourceId source, RuntimeCommand command) => players.TryApply(command);
    }
}

// Managed-only queue/private composition access. Native uses a separate typed fixture.
internal sealed class Packet5BootstrapSupport : IRuntimePlayerEventSink, IDisposable
{
    internal readonly VanillaUnifiedRandom1458 Random;
    internal readonly ServerRuntimeState State;
    internal readonly RuntimeConnectionRegistry Registry = new();
    private readonly PlayerBootstrapFrameSink bootstrap;
    private readonly TerrariaConnectionOutboundQueue ownerQueue = new(new(256, 131072, 2048));
    private readonly TerrariaConnectionOutboundQueue peerQueue = new(new(256, 131072, 2048));
    internal PlayerAuthority Players
    {
        get;
    }
    internal ConnectionHandle Connection => new(GameCommandSourceId.FromConnection(99112), bootstrap.AssignedPlayerHandle!.Value);
    internal Action? Observer;
    internal int Events;

    internal Packet5BootstrapSupport(int seed)
    {
        Random = new(seed);
        State = new(playerEvents: this, worldTiles: new WorldTileStore(new WorldDimensions(400, 300)),
            npcs: new RuntimeNpcStore(8), naturalSpawnRandom: new SystemVanillaNpcRandom(Random));
        object runtime = typeof(ServerRuntimeState).GetField("_runtime", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(State)!;
        Players = (PlayerAuthority)runtime.GetType().GetProperty("Players", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(runtime)!;
        // This must already be the composition's exact source owner; rebinding another cursor is forbidden.
        Players.BindReceiveEquipmentRandom(Random, OperatingSystem.IsWindows());
        var source = GameCommandSourceId.FromConnection(99112);
        Assert.True(Registry.TryRegister(source, ownerQueue));
        var ingress = new ApplyingIngress(State);
        bootstrap = new(new PlayerSlotPool(1), ownerQueue,
            PlayerBootstrapPacketSet.CreateForTesting(new byte[] { 3, 0, 7 }, [], new byte[] { 3, 0, 49 }), source,
            new RuntimePlayerSpawnCommitIngress(ingress), appearanceIngress: null,
            new RuntimePlayerEquipmentIngress(ingress), new RuntimePlayerMovementIngress(ingress));
        Continue(1, [11, (byte)'T', (byte)'e', (byte)'r', (byte)'r', (byte)'a', (byte)'r', (byte)'i', (byte)'a', (byte)'3', (byte)'2', (byte)'6']);
        Assert.Equal(0, bootstrap.AssignedPlayerHandle!.Value.Slot.Value);
        // Actual bootstrap accepts packet5 before6, without inventing an active player.
        Report(Convert.FromHexString("0C00051F0000010000DB0000"));
        Report(Convert.FromHexString("0C00051F3600070000610000"));
        Assert.False(Players.TryGet(Connection, out _));
        Continue(6, []);
        Continue(8, new byte[9]);
        byte[] spawn = new byte[TerrariaJoinRequestDecoder.PlayerSpawnPayloadLength];
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(spawn.AsSpan(1), 100);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(spawn.AsSpan(3), 103);
        Continue(12, spawn);
        Assert.Equal(PlayerJoinState.Playing, bootstrap.JoinState);
        var peer = new ConnectionHandle(GameCommandSourceId.FromConnection(99113), new(new(1), new(1)));
        Assert.True(Registry.TryRegister(peer.Source, peerQueue));
        var peerSpawn = new PlayerSpawnCommitRequest(peer.Player.Slot, 100, 103, 0, 0, 0, 0, 0);
        Registry.PlayerSpawned(peer, in peerSpawn);
        Drain(ownerQueue);
        Drain(peerQueue);
        Events = 0;
    }

    internal void Report(byte[] frame)
    {
        Assert.Equal(12, frame.Length);
        Assert.Equal(5, frame[2]);
        Continue(5, frame[3..]);
    }

    internal IReadOnlyList<string> DrainPeer() => Drain(peerQueue);
    internal IReadOnlyList<string> DrainOwner() => Drain(ownerQueue);
    private static IReadOnlyList<string> Drain(TerrariaConnectionOutboundQueue outbound)
    {
        var queue = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
            .GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outbound)!;
        var frames = new List<string>();
        while (queue.TryRead(out var frame)) frames.Add(Convert.ToHexString(frame.Bytes.Span));
        return frames;
    }

    private void Continue(byte id, byte[] payload)
    {
        var frame = new TerrariaFrame(checked((ushort)(3 + payload.Length)), id, ReadOnlySequence<byte>.Empty, new(payload));
        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(in frame));
    }

    public void PlayerEquipmentUpdated(ConnectionHandle connection, in PlayerEquipmentCommitRequest request)
    {
        Registry.PlayerEquipmentUpdated(connection, in request);
        Events++;
        Observer?.Invoke();
    }
    public void PlayerSpawned(ConnectionHandle connection, in PlayerSpawnCommitRequest request) => Registry.PlayerSpawned(connection, in request);
    public void PlayerAppearanceUpdated(ConnectionHandle connection, in PlayerAppearanceCommitRequest request) => Registry.PlayerAppearanceUpdated(connection, in request);
    public void PlayerMoved(ConnectionHandle connection, in PlayerMovementCommitRequest request) => Registry.PlayerMoved(connection, in request);
    public void PlayerDisconnected(ConnectionHandle connection) => Registry.PlayerDisconnected(connection);
    public void Dispose() => bootstrap.Dispose();

    private sealed class ApplyingIngress(ServerRuntimeState state) : IGameCommandIngress<RuntimeCommand>
    {
        public bool TryPost(GameCommandSourceId source, RuntimeCommand command)
        {
            state.Apply(command);
            return true;
        }
    }
}
