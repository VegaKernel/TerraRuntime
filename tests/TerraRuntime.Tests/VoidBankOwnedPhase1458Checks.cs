using Xunit;
using System.Buffers;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Network;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public static class VoidBankOwnedPhase1458Checks
{
    // Genuine UpdateEquips stored-role source facts; configured runtime membership.
    // State.Tick checks the actual phase caller without claiming whole Main source parity.
    public static void Run()
    {
        foreach (int id in new[] { 3, 9, 109, 292, 4346, 5095, 1321, 854 })
            NeutralStored(id, 0, false);
        NeutralStored(1321, 72, true);
        foreach (int id in new[] { 15, 6190 }) ActiveWriterRefuses(id);
        WrongRoleRefuses();
        UnknownRawReportDoesNotCreateKnownStorage();
        foreach (int id in new[] { 15, 6190 })
            foreach (string gate in new[] { "closed", "cursor", "bank", "open" })
                StoredWriterGate(id, gate);
        LateBankMutationRefuses();
        LateGateMutationRefuses();
    }

    private static void SetItem(Fixture fixture, short slot, short id, byte prefix = 0, byte flags = 0)
    {
        var connection = fixture.Connections[2];
        fixture.Apply(new PlayerEquipmentRuntimeCommand(connection,
            new(connection.Player.Slot, slot, id == 0 ? (short)0 : (short)1, prefix, id, flags)));
    }

    private static void DrainSetup(Fixture fixture)
    {
        var connection = fixture.Connections[2];
        fixture.Apply(new PlayerInventorySyncCompleteRuntimeCommand(connection));
        int steps = 0;
        while (!fixture.Players.CanApplyShopSource(connection.Source))
        {
            Assert.True(++steps <= 10000);
            fixture.Players.TickShopJournalWork();
            Assert.InRange(fixture.Players.LastShopWorkSteps, 0, 64);
        }
    }

    private static void NeutralStored(int id, byte prefix, bool favorite)
    {
        using var fixture = new Fixture("inactive-first", runtimeTick: true);
        SetItem(fixture, 2, 4131);
        DrainSetup(fixture);
        Assert.True(fixture.Players.TryTickRemotePlayerPhase());
        var before = fixture.Member(2).ItemPhase!.Value.DerivedCombat;
        var buffs = fixture.Buffs(2).Clone();
        SetItem(fixture, 739, (short)id, prefix, favorite ? (byte)1 : (byte)0);
        DrainSetup(fixture);
        Assert.True(fixture.Players.TryTickRemotePlayerPhase(), "source-neutral stored role must admit");
        Assert.NotNull(fixture.Member(2).ItemPhase);
        Assert.Equal(before, fixture.Member(2).ItemPhase!.Value.DerivedCombat);
        Assert.True(Inventory(fixture.Players).TryCaptureShopJournal(fixture.Connections[2], out var ownedBeforeTick));
        Assert.NotNull(ownedBeforeTick);
        fixture.Tick();
        Assert.True(ownedBeforeTick!.IsCurrent, "full219 physical/funding/copper/caps remain unchanged");
        Assert.NotNull(fixture.Member(2).ItemPhase);
        Assert.Equal(before, fixture.Member(2).ItemPhase!.Value.DerivedCombat);
        Assert.True(buffs.HasSameSlots(fixture.Buffs(2)));
        var bank = new RuntimePlayerInventoryItem[160];
        var copper = new long[160];
        Assert.True(Inventory(fixture.Players).TryCaptureShopBanks(fixture.Connections[2], bank, copper));
        Assert.Equal(new RuntimePlayerInventoryItem(new(id), 1, new(prefix), favorite ? (byte)1 : (byte)0), bank[159]);
        Assert.All(copper, value => Assert.Equal(0L, value));
    }

    private static void ActiveWriterRefuses(int id)
    {
        using var fixture = new Fixture("inactive-first", runtimeTick: true);
        SetItem(fixture, 2, 4131);
        DrainSetup(fixture);
        Assert.True(fixture.Players.TryTickRemotePlayerPhase());
        SetItem(fixture, 739, (short)id);
        DrainSetup(fixture);
        var random = fixture.Random.Clone();
        var phase = fixture.Member(2).ItemPhase;
        Assert.False(fixture.Players.TryTickRemotePlayerPhase(), "unrepresented bank writer must refuse whole census");
        Assert.Equal(phase, fixture.Member(2).ItemPhase);
        Assert.True(random.HasSameState(fixture.Random));
        fixture.Tick();
        Assert.Null(fixture.Member(2).ItemPhase);
        Assert.Empty(fixture.Buffs(2).CaptureTypes());
    }

    private static void WrongRoleRefuses()
    {
        using var fixture = new Fixture("inactive-first", runtimeTick: true);
        SetItem(fixture, 59, 9);
        DrainSetup(fixture);
        Assert.False(fixture.Players.TryTickRemotePlayerPhase(), "stored neutral does not make armor role neutral");
    }

    private static void StoredWriterGate(int id, string gate)
    {
        using var fixture = new Fixture("inactive-first", runtimeTick: true);
        Assert.True(fixture.Players.TryTickRemotePlayerPhase());
        var baselineCombat = fixture.Member(2).ItemPhase!.Value.DerivedCombat;
        if (gate == "open") SetItem(fixture, 2, 4131);
        else if (gate == "cursor") SetItem(fixture, 58, 4131);
        else if (gate == "bank") SetItem(fixture, 738, 4131);
        SetItem(fixture, 739, (short)id);
        DrainSetup(fixture);
        var random = fixture.Random.Clone();
        var buffs = fixture.Buffs(2).Clone();
        bool admitted = fixture.Players.TryTickRemotePlayerPhase();
        Assert.Equal(gate != "open", admitted);
        if (admitted) Assert.Equal(baselineCombat, fixture.Member(2).ItemPhase!.Value.DerivedCombat);
        if (!admitted) Assert.True(random.HasSameState(fixture.Random));
        else
        {
            // Compare ordinary whole-phase offers with the same source gate and an empty stored slot.
            // This preserves RNG equality without confusing isolated UpdateEquips with whole Update.
            using var emptyStoredControl = new Fixture("inactive-first", runtimeTick: true);
            Assert.True(emptyStoredControl.Players.TryTickRemotePlayerPhase());
            if (gate == "cursor") SetItem(emptyStoredControl, 58, 4131);
            else if (gate == "bank") SetItem(emptyStoredControl, 738, 4131);
            SetItem(emptyStoredControl, 739, 0);
            DrainSetup(emptyStoredControl);
            Assert.True(random.HasSameState(emptyStoredControl.Random));
            Assert.True(emptyStoredControl.Players.TryTickRemotePlayerPhase());
            Assert.True(fixture.Random.HasSameState(emptyStoredControl.Random));
        }
        Assert.True(Inventory(fixture.Players).TryCaptureShopJournal(fixture.Connections[2], out var ownedBeforeTick), $"owned image {id}/{gate}");
        Assert.NotNull(ownedBeforeTick);
        fixture.Tick();
        Assert.True(ownedBeforeTick!.IsCurrent, "full219 physical/funding/copper/caps remain unchanged");
        Assert.Equal(gate != "open", fixture.Member(2).ItemPhase is not null);
        if (gate != "open") Assert.Equal(baselineCombat, fixture.Member(2).ItemPhase!.Value.DerivedCombat);
        Assert.True(buffs.HasSameSlots(fixture.Buffs(2)));
    }

    private static void LateBankMutationRefuses()
    {
        using var fixture = new Fixture("inactive-first", runtimeTick: true);
        SetItem(fixture, 2, 4131);
        SetItem(fixture, 739, 9);
        DrainSetup(fixture);
        Assert.True(fixture.Players.TryTickRemotePlayerPhase());
        var phase = fixture.Member(2).ItemPhase;
        var random = fixture.Random.Clone();
        bool invoked = false;
        fixture.Players.SetNpcHealthGrapplingFacts(_ =>
        {
            if (!invoked)
            {
                invoked = true;
                SetItem(fixture, 739, 6190);
            }
            return false;
        });
        Assert.False(fixture.Players.TryTickRemotePlayerPhase());
        Assert.True(invoked);
        Assert.Equal(phase, fixture.Member(2).ItemPhase);
        Assert.True(random.HasSameState(fixture.Random));
    }

    private static void UnknownRawReportDoesNotCreateKnownStorage()
    {
        using var fixture = new Fixture("inactive-first", runtimeTick: true);
        SetItem(fixture, 739, 32767);
        var bank = new RuntimePlayerInventoryItem[160];
        var copper = new long[160];
        Assert.True(Inventory(fixture.Players).TryCaptureShopBanks(fixture.Connections[2], bank, copper));
        Assert.True(bank[159].IsEmpty, "legacy normalization must not represent an unknown stored source item");
        Assert.True(Inventory(fixture.Players).TryCaptureShopBankOwnership(fixture.Connections[2], out var quantity, out _));
        Assert.Equal((short)0, quantity[159]);
        Assert.All(copper, value => Assert.Equal(0L, value));
        Assert.True(fixture.Players.TryTickRemotePlayerPhase());
    }

    private static void LateGateMutationRefuses()
    {
        using var fixture = new Fixture("inactive-first", runtimeTick: true);
        SetItem(fixture, 739, 6190);
        DrainSetup(fixture);
        Assert.True(fixture.Players.TryTickRemotePlayerPhase());
        var phase = fixture.Member(2).ItemPhase;
        var random = fixture.Random.Clone();
        bool invoked = false;
        fixture.Players.SetNpcHealthGrapplingFacts(_ =>
        {
            if (!invoked)
            {
                invoked = true;
                SetItem(fixture, 2, 4131);
            }
            return false;
        });
        Assert.False(fixture.Players.TryTickRemotePlayerPhase());
        Assert.True(invoked);
        Assert.Equal(phase, fixture.Member(2).ItemPhase);
        Assert.True(random.HasSameState(fixture.Random));
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inventory")]
    private static extern ref RuntimePlayerInventoryStore Inventory(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "membership")]
    private static extern ref RuntimePlayerMembership Membership(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "transferProfiles")]
    private static extern ref RuntimePlayerTransferProfileStore Profiles(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "playerUpdateRandom")]
    private static extern ref VanillaUnifiedRandom1458? UpdateRandom(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_runtime")]
    private static extern ref ServerRuntimeComposition Composition(ServerRuntimeState owner);
    private sealed class Fixture : IDisposable
    {
        internal readonly PlayerAuthority Players;
        internal readonly WorldTileStore Tiles = new(new WorldDimensions(400, 300));
        internal readonly VanillaUnifiedRandom1458 Random;
        private readonly ServerRuntimeState? state;
        internal readonly EventSink Events = new();
        internal readonly Dictionary<byte, ConnectionHandle> Connections = [];
        internal readonly Dictionary<byte, short> ItemIds = [];
        private readonly List<PlayerJoinSession> sessions = [];
        private readonly HashSet<byte> moved = [];
        private readonly PlayerSlotPool pool = new(3);
        internal Fixture(string mode, bool runtimeTick = false)
        {
            for (int x = 0; x < 400; x++) Tiles.Set(x, 103, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            var projectiles = new RuntimeProjectileStore();
            if (runtimeTick)
            {
                state = new(playerEvents: Events, worldTiles: Tiles, projectiles: projectiles,
                    townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { WorldSurface = 80 },
                    playerUpdateRandomSeed: new(0));
                Players = Composition(state).Players;
                Random = UpdateRandom(Players)!;
            }
            else { Players = new(Events, Tiles); Random = new(0); }
            // These prefix-none references have identical clocks on both source platforms.
            Players.SetPlayerUpdateWorldFacts(new(400, 300, 80, false, false)
            { WindowsItemPrefixArithmetic = OperatingSystem.IsWindows() });
            Players.SetPlayerUpdateRandom(Random);
            Players.SetRemotePlayerEnvironment(new(false, false), projectiles);
            Players.SetNpcHealthWorldFacts(() => new(false, false));
            Players.SetNpcHealthGrapplingFacts(_ => false);
            for (int i = 0; i < 3; i++)
            {
                Assert.True(pool.TryAcquireConnection(out var lease));
                sessions.Add(new(lease!));
            }
            var membership = Membership(Players);
            // Slot255 is not a connection lease. A trusted imported source placeholder remains untouched.
            membership.Commit(new RuntimePlayerMember
            {
                Slot = new(255),
                Revision = 1,
                Connection = new(GameCommandSourceId.FromConnection(99255),
                new(new(255), new(1))),
                ItemPhase = null,
                PhysicsPhase = null
            });
            foreach (byte slot in mode == "reverse-join" ? new byte[] { 2, 0 } : new byte[] { 0, 2 })
            {
                if (slot == 0 && mode == "inactive-first") continue;
                var session = sessions[slot]; session.ObserveWorldRequest(); session.ObserveSectionRequest();
                var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(99000 + slot), session.Handle);
                Connections.Add(slot, connection);
                short id = slot == 2 ? (short)0 : slot == 0 && mode == "potion-first" ? (short)28 : slot == 0 && mode == "weapon98-first" ? (short)98 : (short)0;
                if (slot == 2 && mode == "potion-first") id = 0;
                ItemIds.Add(slot, id);
                Apply(new PlayerHealthRuntimeCommand(connection, new(session.Slot, 50, 400)));
                Apply(new PlayerManaRuntimeCommand(connection, new(session.Slot, 10, 200)));
                Apply(new PlayerEquipmentRuntimeCommand(connection, new(session.Slot, 0, id == 0 ? (short)0 : (short)3, 0, id, 0)));
                Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
                Move(slot, false);
                // Explicit source-configured imported lifecycle branches, after genuine membership creation.
                // Packet movement admission is not weakened to inject outside coordinates.
                if (slot == 0 && mode == "outside-first") { Member(slot).PositionX = 0; Member(slot).PositionY = 0; }
                if (slot == 0 && mode == "dead-first") Member(slot).IsDead = true;
                if (slot == 0 && mode == "ghost-first") Member(slot).MovementFlags |= VanillaPlayerHealthContext1458.GhostMovementFlag;
            }
        }
        internal RuntimePlayerMember Member(byte slot) { Assert.True(Players.TryGet(slot, out var member)); return member; }
        internal void Apply(RuntimeCommand command)
        {
            if (state is null) Assert.True(Players.TryApply(command));
            else state.Apply(command);
        }
        internal void Tick() { Assert.NotNull(state); state.Tick(); }
        internal bool Step()
        {
            if (state is null) return Players.TryTickRemotePlayerPhase();
            state.Tick();
            return Member(2).ItemPhase is not null;
        }
        internal void ReconnectFirst()
        {
            Apply(new PlayerDisconnectRuntimeCommand(Connections[0]));
            sessions[0].Dispose();
            Assert.True(pool.TryAcquireConnection(out var lease));
            var session = new PlayerJoinSession(lease!); sessions.Add(session);
            Assert.Equal(new PlayerSlotId(0), session.Slot);
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(99100), session.Handle);
            Connections[0] = connection;
            Apply(new PlayerHealthRuntimeCommand(connection, new(session.Slot, 50, 400)));
            Apply(new PlayerManaRuntimeCommand(connection, new(session.Slot, 10, 200)));
            Apply(new PlayerEquipmentRuntimeCommand(connection, new(session.Slot, 0, 0, 0, 0, 0)));
            Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
        }
        internal void Move(byte slot, bool control)
        {
            var member = Member(slot);
            bool first = moved.Add(slot);
            if (!first && member.PositionX == 0 && member.PositionY == 0)
            {
                // Trusted source-configured outside import: ordinary movement would reject this geometry.
                member.ControlFlags = control ? (byte)32 : (byte)0;
                return;
            }
            Apply(new PlayerMovementRuntimeCommand(Connections[slot], new(new(slot), control ? (byte)32 : (byte)0,
                (byte)(member.MovementFlags | 16), 0, 64, 0, first ? 1600 + slot * 40 : member.PositionX,
                first ? 1606 : member.PositionY, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        }
        internal PlayerBuffState Buffs(byte slot) => Profiles(Players).CaptureBuffState(Connections[slot])!;
        public void Dispose() { foreach (var session in sessions) session.Dispose(); }
    }
    private sealed class EventSink : IRuntimePlayerEventSink
    {
        internal int Count;
        internal readonly List<byte> Seen = [];
        internal readonly List<ConnectionHandle> BuffConnections = [];
        internal Action<ConnectionHandle, PlayerEquipmentCommitRequest>? OnEquipment = null;
        public void PlayerAppearanceUpdated(ConnectionHandle c, in PlayerAppearanceCommitRequest r) => Count++;
        public void PlayerEquipmentUpdated(ConnectionHandle c, in PlayerEquipmentCommitRequest r) { Count++; Seen.Add(5); OnEquipment?.Invoke(c, r); }
        public void PlayerHealthUpdated(ConnectionHandle c, in PlayerHealthCommitRequest r) { Count++; Seen.Add(16); }
        public void PlayerManaUpdated(ConnectionHandle c, in PlayerManaCommitRequest r) { Count++; Seen.Add(42); }
        public void PlayerItemAnimationUpdated(ConnectionHandle c, float rotation, short animation) => Count++;
        public void PlayerBuffTypesUpdated(ConnectionHandle c, in PlayerBuffTypesCommitRequest r) { Count++; Seen.Add(50); BuffConnections.Add(c); }
        public void PlayerSpawned(ConnectionHandle c, in PlayerSpawnCommitRequest r) => Count++;
        public void PlayerMoved(ConnectionHandle c, in PlayerMovementCommitRequest r) => Count++;
        public void PlayerDisconnected(ConnectionHandle c) => Count++;
    }
}
