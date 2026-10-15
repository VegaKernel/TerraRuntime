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

public static class FoodClockBoundaryChecks
{
    public static void Run()
    {
        for (int mutation = 0; mutation < 7; mutation++)
            QuickClock(mutation);
        QuickClock(upgrade: true);
        Lifetime(false);
        Lifetime(true);
        Lifetime(false, death: true);
        Lifetime(false, death: true, outside: true);
        TransferClock();
        RepeatedCompaction();
        ExpiredProvider();
    }

    public static void QuickClock(int mutation = 0, bool upgrade = false)
    {
        ushort oldFood = upgrade ? (ushort)26 : (ushort)207;
        ushort offer = upgrade ? (ushort)206 : (ushort)5;
        using var f = new Fixture("inactive-first", runtimeTick: true);
        var c = f.Connections[2];
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
        f.Tick();
        f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 0, 3, 0, upgrade ? (short)967 : (short)4022, 0)));
        f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 1, 3, 0, upgrade ? (short)357 : (short)292, 0)));
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
        var inv = Inventory(f.Players);
        short[] q = new short[59];
        q[0] = q[1] = 1;
        inv.AdoptFundedQuantities(c, q);
        var m = f.Member(2);
        m.ItemPhase = m.ItemPhase!.Value with
        {
            Selected = m.ItemPhase.Value.Selected with
            {
                ReleaseUseItem = true
            }
        };
        f.Move(2, true);
        f.Tick();
        Assert.True(f.Buffs(2).HasOwnedFood);
        f.Move(2, false);
        for (int i = 0; i < 55; i++)
            f.Tick();
        if (mutation == 4)
        {
            var before = f.Buffs(2);
            var shortBudget = before.Clone();
            Durations(shortBudget)[0] = 2;
            Assert.True(Profiles(f.Players).TryAdoptBuffState(c, before, shortBudget));
        }
        if (mutation == 5)
        {
            var before = f.Buffs(2);
            var initial = before.Clone();
            Assert.True(initial.TryApplySelectedConsumable(new(16), 123, false));
            Assert.True(Profiles(f.Players).TryAdoptBuffState(c, before, initial));
        }
        int beforeFood = f.Buffs(2).GetDuration(new(oldFood));
        Assert.True(beforeFood > 0);
        Assert.NotNull(m.ItemPhase);
        Console.WriteLine($"PRE physical1={inv.TryGet(c, 1, out var pi)}:{pi.Stack} phase={m.ItemPhase is not null} control={m.ControlFlags}");
        // Configured source-shaped QuickBuff: unchanged control13 and ordinary potion debit.
        f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 1, 2, 0, upgrade ? (short)357 : (short)292, 0)));
        Console.WriteLine($"AFTER5 shop={m.CursorMove?.Shop is not null} before={m.CursorMove?.Shop?.QuickBuffBefore is not null}");
        f.Apply(new PlayerBuffTypesRuntimeCommand(c, new(c.Player.Slot, upgrade ? new[] { new BuffTypeId(206) } : mutation == 5 ? new[] { new BuffTypeId(207), new BuffTypeId(16), new BuffTypeId(5) } : new[] { new BuffTypeId(207), new BuffTypeId(5) })));
        Console.WriteLine($"AFTER50 incoming={m.CursorMove?.Shop?.QuickBuffIncoming?.Length}");
        f.Move(2, false);
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
        Console.WriteLine($"AFTER138 sealed={m.CursorMove?.Shop?.QuickBuffSealed}");
        if (mutation == 1)
            f.Apply(new PlayerBuffTypesRuntimeCommand(c, new(c.Player.Slot, Array.Empty<BuffTypeId>())));
        if (mutation == 2)
            Profiles(f.Players).RestoreBuffState(c, f.Buffs(2));
        if (mutation == 3)
        {
            var before = f.Buffs(2);
            var changed = before.Clone();
            Assert.True(changed.TryApplySelectedConsumable(new(5), 123, false));
            Assert.True(Profiles(f.Players).TryAdoptBuffState(c, before, changed));
        }
        if (mutation == 5)
        {
            // A semantic write with exactly equal public fields is not elapsed clock time.
            var before = f.Buffs(2);
            var rewritten = before.Clone();
            Assert.True(rewritten.TryApplySelectedConsumable(new(16), 123, false));
            Assert.True(Profiles(f.Players).TryAdoptBuffState(c, before, rewritten));
        }
        if (mutation == 6)
        {
            var changedFunding = inv.CaptureFundedQuantities(c);
            changedFunding[1] = 2;
            inv.AdoptFundedQuantities(c, changedFunding);
        }
        var expectedItems = new RuntimePlayerInventoryItem[59];
        Assert.True(inv.TryCopyInventory(c, expectedItems));
        var expectedQ = inv.CaptureFundedQuantities(c);
        var expectedCopper = inv.CaptureFundedCoinValues(c);
        var expectedCaps = inv.CaptureOwnedQuantities(c);
        var bankItems = new RuntimePlayerInventoryItem[160];
        var bankCopper = new long[160];
        Assert.True(inv.TryCaptureShopBanks(c, bankItems, bankCopper));
        Assert.True(inv.TryCaptureShopBankOwnership(c, out var bankQ, out var bankCaps));
        int observers = 0;
        f.Events.OnEquipment = (connection, request) =>
        {
            if (connection != c)
                return;
            observers++;
            for (short slot = 0; slot < 59; slot++)
            {
                Assert.True(inv.TryGet(c, slot, out var actual));
                Assert.Equal(slot == 1 ? expectedItems[slot] with
                {
                    Stack = 2
                } : expectedItems[slot], actual);
            }
            var nextQ = (short[])expectedQ.Clone();
            nextQ[1] = 0;
            Assert.Equal(nextQ, inv.CaptureFundedQuantities(c));
            Assert.Equal(expectedCopper, inv.CaptureFundedCoinValues(c));
            Assert.Equal(expectedCaps, inv.CaptureOwnedQuantities(c));
            var nextBank = new RuntimePlayerInventoryItem[160];
            var nextCopper = new long[160];
            Assert.True(inv.TryCaptureShopBanks(c, nextBank, nextCopper));
            Assert.Equal(bankItems, nextBank);
            Assert.Equal(bankCopper, nextCopper);
            Assert.True(inv.TryCaptureShopBankOwnership(c, out var nextBankQ, out var nextBankCaps));
            Assert.Equal(bankQ, nextBankQ);
            Assert.Equal(bankCaps, nextBankCaps);
            Assert.Equal(28800, f.Buffs(2).GetDuration(new(offer)));
        };
        f.Tick();
        for (int i = 0; i < 80 && !f.Players.CanApplyShopSource(c.Source); i++)
            f.Tick();
        Console.WriteLine($"QUICK clock old={beforeFood} now={f.Buffs(2).GetDuration(new(oldFood))} q1={inv.CaptureFundedQuantities(c)[1]} buff5={f.Buffs(2).GetDuration(new(offer))}");
        Assert.Equal(mutation == 6 ? (short)2 : mutation is 1 or 2 or 3 or 5 ? (short)1 : (short)0, inv.CaptureFundedQuantities(c)[1]);
        if (mutation is 1 or 2 or 3 or 5 or 6)
        {
            Assert.NotEqual(28800, f.Buffs(2).GetDuration(new(offer)));
            return;
        }
        Assert.Equal(28800, f.Buffs(2).GetDuration(new(offer)));
        Assert.True(observers > 0);
        if (upgrade)
            Assert.Equal(28800, f.Buffs(2).GetDuration(new(206)));
        if (f.Buffs(2).HasOwnedFood)
        {
            var ackBefore = f.Buffs(2);
            f.Apply(new PlayerBuffTypesRuntimeCommand(c, new(c.Player.Slot, ackBefore.CaptureTypes())));
            Assert.True(ackBefore.HasSameSlots(f.Buffs(2)));
        }
        if (mutation == 4)
        {
            Assert.False(f.Buffs(2).HasOwnedFood);
            f.Apply(new PlayerBuffTypesRuntimeCommand(c, new(c.Player.Slot, upgrade ? new[] { new BuffTypeId(206) } : mutation == 5 ? new[] { new BuffTypeId(207), new BuffTypeId(16), new BuffTypeId(5) } : new[] { new BuffTypeId(207), new BuffTypeId(5) })));
            Assert.False(f.Buffs(2).HasOwnedFood);
            Assert.Equal(0, f.Buffs(2).GetDuration(new(oldFood)));
        }
        Assert.True(f.Buffs(2).GetDuration(new(oldFood)) < beforeFood);
    }
    public static void ExpiredProvider()
    {
        using var f = new Fixture("empty-first", runtimeTick: true);
        var c = f.Connections[2];
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
        f.Tick();
        f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 0, 3, 0, 4022, 0)));
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
        var inv = Inventory(f.Players);
        short[] q = new short[59];
        q[0] = 1;
        inv.AdoptFundedQuantities(c, q);
        var m = f.Member(2);
        m.ItemPhase = m.ItemPhase!.Value with
        {
            Selected = m.ItemPhase.Value.Selected with
            {
                ReleaseUseItem = true
            }
        };
        f.Move(2, true);
        f.Tick();
        f.Move(2, false);
        for (int i = 0; i < 55; i++)
            f.Tick();
        var before = f.Buffs(2);
        var shortBudget = before.Clone();
        Durations(shortBudget)[0] = 1;
        Assert.True(Profiles(f.Players).TryAdoptBuffState(c, before, shortBudget));
        // Another joined actor's unsupported equipment retires the whole shared phase.
        var other = f.Connections[0];
        f.Apply(new PlayerEquipmentRuntimeCommand(other, new(other.Player.Slot, 59, 1, 0, 1, 0)));
        var healthBefore = m.NpcHealth;
        bool invoked = false;
        f.Players.SetNpcHealthGrapplingFacts(player =>
        {
            if (player == c.Player && !invoked && f.Buffs(2).GetDuration(new(207)) == 0)
            {
                invoked = true;
                Profiles(f.Players).RestoreBuffState(c, f.Buffs(2));
            }
            return false;
        });
        f.Tick();
        Assert.True(invoked);
        Assert.Equal(0, f.Buffs(2).GetDuration(new(207)));
        Console.WriteLine($"EXPIRED_PROVIDER before={healthBefore} after={m.NpcHealth}");
        Assert.Equal(healthBefore, m.NpcHealth);
    }
    public static void Lifetime(bool failedPhase, bool death = false, bool outside = false)
    {
        using var f = new Fixture("inactive-first", runtimeTick: true);
        var c = f.Connections[2];
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
        f.Tick();
        f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 0, 3, 0, 4022, 0)));
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
        var inv = Inventory(f.Players);
        short[] q = new short[59];
        q[0] = 1;
        inv.AdoptFundedQuantities(c, q);
        var m = f.Member(2);
        m.ItemPhase = m.ItemPhase!.Value with
        {
            Selected = m.ItemPhase.Value.Selected with
            {
                ReleaseUseItem = true
            }
        };
        f.Move(2, true);
        f.Tick();
        Assert.Equal(172800, f.Buffs(2).GetDuration(new(207)));
        var before = f.Buffs(2);
        var shortBudget = before.Clone();
        Durations(shortBudget)[0] = 1;
        Assert.True(Profiles(f.Players).TryAdoptBuffState(c, before, shortBudget));
        f.Move(2, false);
        if (failedPhase)
            f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 59, 1, 0, 1, 0)));
        if (death)
            m.IsDead = true;
        if (outside)
        {
            m.PositionX = 0;
            m.PositionY = 0;
        }
        f.Tick();
        Assert.Equal(0, f.Buffs(2).GetDuration(new(207)));
        if (!failedPhase && !death)
            Assert.True(m.ItemPhase!.Value.DerivedCombat!.Value.WellFed);
        else if (failedPhase)
            Assert.Null(m.ItemPhase);
        Assert.False(f.Buffs(2).HasOwnedFood);
        f.Tick();
        if (!failedPhase && !death)
            Assert.False(m.ItemPhase!.Value.DerivedCombat!.Value.WellFed);
        Assert.Equal((short)0, inv.CaptureFundedQuantities(c)[0]);
        Console.WriteLine($"LIFETIME failedPhase={failedPhase} death={death} outside={outside} passed");
    }
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "durations")]
    private static extern ref int[] Durations(PlayerBuffState owner);
    public static void TransferClock()
    {
        using var source = new Fixture("inactive-first", runtimeTick: true);
        var c = source.Connections[2];
        source.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
        source.Tick();
        source.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 0, 3, 0, 4022, 0)));
        source.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
        var inv = Inventory(source.Players);
        short[] q = new short[59];
        q[0] = 1;
        inv.AdoptFundedQuantities(c, q);
        var member = source.Member(2);
        member.ItemPhase = member.ItemPhase!.Value with
        {
            Selected = member.ItemPhase.Value.Selected with
            {
                ReleaseUseItem = true
            }
        };
        source.Move(2, true);
        source.Tick();
        source.Move(2, false);
        for (int i = 0; i < 55; i++)
            source.Tick();
        var before = source.Buffs(2);
        var shortBudget = before.Clone();
        Durations(shortBudget)[0] = 2;
        Assert.True(Profiles(source.Players).TryAdoptBuffState(c, before, shortBudget));
        var completion = new TaskCompletionSource<RuntimePlayerTransferState?>();
        source.Apply(new PlayerTransferDetachRuntimeCommand(c, completion));
        Assert.True(completion.Task.IsCompletedSuccessfully);
        Assert.NotNull(completion.Task.Result);
        // A distinct world clock starts near zero. Transfer copies remaining budget, not source epoch.
        using var target = new Fixture("inactive-first", runtimeTick: true);
        target.Apply(new PlayerDisconnectRuntimeCommand(target.Connections[2]));
        var next = new ConnectionHandle(GameCommandSourceId.FromConnection(99122), new(c.Player.Slot, new(c.Player.Generation.Value + 1)));
        var attached = new TaskCompletionSource<bool>();
        target.Apply(new PlayerTransferAttachRuntimeCommand(next, completion.Task.Result!, 100, 103, true, false, attached));
        Assert.True(attached.Task.IsCompletedSuccessfully && attached.Task.Result);
        target.Connections[2] = next;
        Assert.Equal(2, target.Buffs(2).GetDuration(new(207)));
        Assert.True(target.Buffs(2).HasOwnedFood);
        Assert.Null(target.Member(2).CursorMove);
        target.Apply(new PlayerBuffTypesRuntimeCommand(c, new(c.Player.Slot, new[] { new BuffTypeId(207) })));
        Assert.Equal(2, target.Buffs(2).GetDuration(new(207)));
        target.Tick();
        Assert.Equal(1, target.Buffs(2).GetDuration(new(207)));
        target.Tick();
        Assert.Equal(0, target.Buffs(2).GetDuration(new(207)));
        target.Tick();
        Assert.False(target.Buffs(2).HasOwnedFood);
        Console.WriteLine("TRANSFER high-low clock preserved exact2 ticks, old generation inert");
    }

    public static void RepeatedCompaction()
    {
        var types = new BuffTypeId[44];
        var times = new int[44];
        types[0] = new(207);
        times[0] = 1;
        types[1] = new(207);
        times[1] = 2;
        types[2] = new(5);
        times[2] = 28800;
        types[43] = new(207);
        times[43] = 3;
        var initial = PlayerBuffState.FromSlots(types, times);
        OwnedFood(initial)[0] = OwnedFood(initial)[1] = OwnedFood(initial)[43] = true;
        initial.StampOwnedFoodGrant(100);
        var current = initial.Clone();
        for (long tick = 101; tick <= 105; tick++)
        {
            var sameTick = current.Clone();
            current.AdvanceOwnedFoodClock(tick, true);
            Assert.True(initial.IsFoodClockContinuation(current));
            var once = current.Clone();
            current.AdvanceOwnedFoodClock(tick, true);
            Assert.True(once.HasSameSlots(current));
            Assert.Equal(28800, current.GetDuration(new(5)));
        }
        Assert.False(current.HasOwnedFood);
        Assert.Equal(new[] { new BuffTypeId(5) }, current.CaptureTypes());
        var rewritten = current.Clone();
        rewritten.ResetOwnedFoodClockForTransfer();
        Assert.False(initial.IsFoodClockContinuation(rewritten));
        Console.WriteLine("COMPACTION origins/slot43/once per tick passed");
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "ownedFood")]
    private static extern ref bool[] OwnedFood(PlayerBuffState state);

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
            for (int x = 0; x < 400; x++)
                Tiles.Set(x, 103, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            var projectiles = new RuntimeProjectileStore();
            if (runtimeTick)
            {
                state = new(playerEvents: Events, worldTiles: Tiles, projectiles: projectiles,
                    townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with
                    {
                        WorldSurface = 80
                    },
                    playerUpdateRandomSeed: new(0));
                Players = Composition(state).Players;
                Random = UpdateRandom(Players)!;
            }
            else
            {
                Players = new(Events, Tiles);
                Random = new(0);
            }
            // These prefix-none references have identical clocks on both source platforms.
            Players.SetPlayerUpdateWorldFacts(new(400, 300, 80, false, false)
            {
                WindowsItemPrefixArithmetic = OperatingSystem.IsWindows()
            });
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
                if (slot == 0 && mode == "inactive-first")
                    continue;
                var session = sessions[slot];
                session.ObserveWorldRequest();
                session.ObserveSectionRequest();
                var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(99000 + slot), session.Handle);
                Connections.Add(slot, connection);
                short id = 0;
                if (slot == 2 && mode == "potion-first")
                    id = 0;
                ItemIds.Add(slot, id);
                Apply(new PlayerHealthRuntimeCommand(connection, new(session.Slot, 50, 400)));
                Apply(new PlayerManaRuntimeCommand(connection, new(session.Slot, 10, 200)));
                Apply(new PlayerEquipmentRuntimeCommand(connection, new(session.Slot, 0, id == 0 ? (short)0 : (short)3, 0, id, 0)));
                Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
                Move(slot, false);
                // Explicit source-configured imported lifecycle branches, after genuine membership creation.
                // Packet movement admission is not weakened to inject outside coordinates.
                if (slot == 0 && mode == "outside-first")
                {
                    Member(slot).PositionX = 0;
                    Member(slot).PositionY = 0;
                }
                if (slot == 0 && mode == "dead-first")
                    Member(slot).IsDead = true;
                if (slot == 0 && mode == "ghost-first")
                    Member(slot).MovementFlags |= VanillaPlayerHealthContext1458.GhostMovementFlag;
            }
        }
        internal RuntimePlayerMember Member(byte slot)
        {
            Assert.True(Players.TryGet(slot, out var member));
            return member;
        }
        internal void Apply(RuntimeCommand command)
        {
            if (state is null)
                Assert.True(Players.TryApply(command));
            else
                state.Apply(command);
        }
        internal void Tick()
        {
            Assert.NotNull(state);
            state.Tick();
        }
        internal bool Step()
        {
            if (state is null)
                return Players.TryTickRemotePlayerPhase();
            state.Tick();
            return Member(2).ItemPhase is not null;
        }
        internal void ReconnectFirst()
        {
            Apply(new PlayerDisconnectRuntimeCommand(Connections[0]));
            sessions[0].Dispose();
            Assert.True(pool.TryAcquireConnection(out var lease));
            var session = new PlayerJoinSession(lease!);
            sessions.Add(session);
            Assert.Equal(new PlayerSlotId(0), session.Slot);
            session.ObserveWorldRequest();
            session.ObserveSectionRequest();
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
        public void Dispose()
        {
            foreach (var session in sessions)
                session.Dispose();
        }
    }
    private sealed class EventSink : IRuntimePlayerEventSink
    {
        internal int Count;
        internal readonly List<byte> Seen = [];
        internal Action<ConnectionHandle, PlayerEquipmentCommitRequest>? OnEquipment = null;
        public void PlayerAppearanceUpdated(ConnectionHandle c, in PlayerAppearanceCommitRequest r) => Count++;
        public void PlayerEquipmentUpdated(ConnectionHandle c, in PlayerEquipmentCommitRequest r)
        {
            Count++;
            Seen.Add(5);
            OnEquipment?.Invoke(c, r);
        }
        public void PlayerHealthUpdated(ConnectionHandle c, in PlayerHealthCommitRequest r)
        {
            Count++;
            Seen.Add(16);
        }
        public void PlayerManaUpdated(ConnectionHandle c, in PlayerManaCommitRequest r)
        {
            Count++;
            Seen.Add(42);
        }
        public void PlayerItemAnimationUpdated(ConnectionHandle c, float rotation, short animation) => Count++;
        public void PlayerBuffTypesUpdated(ConnectionHandle c, in PlayerBuffTypesCommitRequest r)
        {
            Count++;
            Seen.Add(50);
        }
        public void PlayerSpawned(ConnectionHandle c, in PlayerSpawnCommitRequest r) => Count++;
        public void PlayerMoved(ConnectionHandle c, in PlayerMovementCommitRequest r) => Count++;
        public void PlayerDisconnected(ConnectionHandle c) => Count++;
    }
}
