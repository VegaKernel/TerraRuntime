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
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public static class FoodAuthorityBoundaryChecks
{



    public static void IndependentCases()
    {
        foreach (bool rawFirst in new[] { false, true })
        foreach (short budget in new short[] { 0, 1 })
        {
            using var f = new Fixture("inactive-first", runtimeTick: true);
            var c = f.Connections[2];
            var inventory = Inventory(f.Players);
            f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
            f.Tick();
            f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 0, 3, 0, 4022, 0)));
            f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
            short[] q = new short[59];
            q[0] = budget;
            inventory.AdoptFundedQuantities(c, q);
            if (rawFirst)
                f.Apply(new PlayerBuffTypesRuntimeCommand(c, new(c.Player.Slot, new[] { new BuffTypeId(207) })));
            var member = f.Member(2);
            member.ItemPhase = member.ItemPhase!.Value with
            {
                Selected = member.ItemPhase.Value.Selected with
                {
                    ReleaseUseItem = true
                }
            };
            f.Move(2, true);
            f.Tick();
            Assert.True(inventory.TryGet(c, 0, out var physical));
            Assert.Equal((short)(budget == 0 ? 3 : 2), physical.Stack);
            Assert.Equal((short)0, inventory.CaptureFundedQuantities(c)[0]);
            Assert.Equal(budget != 0, f.Buffs(2).HasOwnedFood);
            f.Apply(new PlayerBuffTypesRuntimeCommand(c, new(c.Player.Slot, new[] { new BuffTypeId(207) })));
            Assert.Equal(budget != 0, f.Buffs(2).HasOwnedFood);
            f.Tick();
            Assert.Equal(budget != 0, f.Member(2).ItemPhase!.Value.DerivedCombat!.Value.WellFed);
            if (budget != 0)
            {
                var before = f.Buffs(2);
                var terminal = before.Clone();
                terminal.CaptureSlots(out var types, out _);
                int index = Array.FindIndex(types, x => x.Value == 207);
                BuffDurations(terminal)[index] = 2;
                Assert.True(Profiles(f.Players).TryAdoptBuffState(c, before, terminal));
                var image = f.Buffs(2).Clone();
                f.Apply(new PlayerBuffTypesRuntimeCommand(c, new(c.Player.Slot, new[] { new BuffTypeId(207) })));
                Assert.True(image.HasSameSlots(f.Buffs(2)));
                Assert.Equal(2, f.Buffs(2).GetDuration(new(207)));
                f.Apply(new PlayerBuffTypesRuntimeCommand(c, new(c.Player.Slot, new[] { new BuffTypeId(207), new BuffTypeId(207) })));
                Assert.True(image.HasSameSlots(f.Buffs(2)));
            }
            f.Apply(new PlayerBuffTypesRuntimeCommand(c, new(c.Player.Slot, Array.Empty<BuffTypeId>())));
            f.Move(2, false);
            for (int i = 0; i < 55; i++)
                f.Tick();
            f.Apply(new PlayerBuffTypesRuntimeCommand(c, new(c.Player.Slot, new[] { new BuffTypeId(207) })));
            f.Tick();
            Assert.False(f.Buffs(2).HasOwnedFood);
            Assert.False(f.Member(2).ItemPhase!.Value.DerivedCombat!.Value.WellFed);
            Console.WriteLine($"PASS provenance rawFirst={rawFirst} q={budget}");
        }
        using (var f = new Fixture("inactive-first", runtimeTick: true))
        {
            var c = f.Connections[2];
            var inventory = Inventory(f.Players);
            f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
            f.Tick();
            f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 0, 3, 0, 4022, 0)));
            f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
            short[] q = new short[59];
            q[0] = 2;
            inventory.AdoptFundedQuantities(c, q);
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
            Assert.Equal((short)1, inventory.CaptureFundedQuantities(c)[0]);
            f.Move(2, false);
            for (int i = 0; i < 55; i++)
                f.Tick();
            var before = f.Buffs(2);
            var terminal = before.Clone();
            terminal.CaptureSlots(out var types, out _);
            int index = Array.FindIndex(types, x => x.Value == 207);
            BuffDurations(terminal)[index] = 2;
            Assert.True(Profiles(f.Players).TryAdoptBuffState(c, before, terminal));
            f.Apply(new PlayerBuffTypesRuntimeCommand(c, new(c.Player.Slot, new[] { new BuffTypeId(207) })));
            Assert.Equal(2, f.Buffs(2).GetDuration(new(207)));
            f.Move(2, true);
            f.Tick();
            Assert.True(inventory.TryGet(c, 0, out var physical));
            Assert.Equal((short)1, physical.Stack);
            Assert.Equal((short)0, inventory.CaptureFundedQuantities(c)[0]);
            Assert.Equal(172800, f.Buffs(2).GetDuration(new(207)));
            Console.WriteLine("PASS samefood ACK cannot renew, second paid unit resets timer");
        }
        using (var f = new Fixture("inactive-first", runtimeTick: true))
        {
            var c = f.Connections[2];
            var inventory = Inventory(f.Players);
            f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
            f.Tick();
            f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 0, 3, 0, 4022, 0)));
            f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
            f.Apply(new PlayerBuffTypesRuntimeCommand(c, new(c.Player.Slot, Enumerable.Repeat(new BuffTypeId(5), 44).ToArray())));
            short[] q = new short[59];
            q[0] = 1;
            inventory.AdoptFundedQuantities(c, q);
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
            Assert.True(inventory.TryGet(c, 0, out var physical));
            Assert.Equal((short)2, physical.Stack);
            Assert.Equal((short)0, inventory.CaptureFundedQuantities(c)[0]);
            f.Buffs(2).CaptureSlots(out var types, out var durations);
            Assert.Equal(44, types.Length);
            Assert.Equal((ushort)207, types[42].Value);
            Assert.Equal(172800, durations[42]);
            Assert.Equal((ushort)5, types[43].Value);
            Assert.True(f.Buffs(2).HasOwnedFood);
            Console.WriteLine("PASS selected full44 consumes before source insertion42, preserves43");
        }
        foreach (short replacement in new short[] { 1911, 967 })
        {
            using var f = new Fixture("inactive-first", runtimeTick: true);
            var c = f.Connections[2];
            var inventory = Inventory(f.Players);
            f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
            f.Tick();
            void Seed(short type)
            {
                f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 0, 3, 0, type, 0)));
                f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
                short[] q = new short[59];
                q[0] = 1;
                inventory.AdoptFundedQuantities(c, q);
            }
            Seed(4022);
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
            Seed(replacement);
            f.Move(2, true);
            f.Tick();
            Assert.True(inventory.TryGet(c, 0, out var physical));
            Assert.Equal((short)2, physical.Stack);
            Assert.Equal((short)0, inventory.CaptureFundedQuantities(c)[0]);
            ushort type = replacement == 1911 ? (ushort)207 : (ushort)26;
            Assert.True(VanillaSelectedConsumableCatalog1458.TryGet(new(replacement), out var metadata));
            Assert.Equal(metadata.BuffTime, f.Buffs(2).GetDuration(new(type)));
            Assert.True(f.Buffs(2).HasOwnedFood);
            if (type == 26)
                Assert.Equal(0, f.Buffs(2).GetDuration(new(207)));
            Console.WriteLine($"PASS paid replacement {replacement}");
        }
    }
    public static void SevenFoodBatch()
    {
        using Stream resource = typeof(FoodAuthorityBoundaryChecks).Assembly.GetManifestResourceStream("FoodSevenLinux1458")!;
        Assert.NotNull(resource);
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var data = JsonDocument.Parse(gzip);
        var row = data.RootElement[0];
        using var f = new Fixture("empty-selected", runtimeTick: true);
        var c = f.Connections[2];
        var inventory = Inventory(f.Players);
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
        f.Tick();
        short[] ids = { 292, 303, 2344, 2346, 2347, 2349, 4022 };
        for (short i = 0; i < ids.Length; i++)
            f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, i, 3, 0, ids[i], 0)));
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
        f.Move(2, false);
        f.Tick();
        f.Apply(new PlayerBuffTypesRuntimeCommand(c, new(c.Player.Slot, Array.Empty<BuffTypeId>())));
        // Match the source's unchanged direction/movement flags before the measured QuickBuff batch,
        // as the existing source-bound QuickBuff fixture does. Position remains configured server geometry.
        var movementHex = row.GetProperty("syncFrames").EnumerateArray().Single(x => Convert.FromHexString(x.GetString()!)[2] == 13);
        var movementBytes = Convert.FromHexString(movementHex.GetString()!);
        var movementFrame = new TerrariaFrame((ushort)movementBytes.Length, 13, new ReadOnlySequence<byte>(movementBytes), new ReadOnlySequence<byte>(movementBytes.AsMemory(3)));
        Assert.Equal(TerrariaPlayerMovementDecodeResult.Decoded, TerrariaPlayerMovementDecoder.TryDecode(in movementFrame, out var initialMovement));
        var initialMember = f.Member(2);
        f.Apply(new PlayerMovementRuntimeCommand(c, new(c.Player.Slot, initialMovement.ControlFlags, initialMovement.MovementFlags, initialMovement.MiscFlags1, initialMovement.MiscFlags2, initialMovement.SelectedItem, initialMember.PositionX, initialMember.PositionY, initialMovement.HasVelocity, initialMovement.VelocityX, initialMovement.VelocityY, initialMovement.HasMount, initialMovement.MountType, false, 0, 0, 0, 0, false, 0, 0)));
        short[] q = new short[59];
        for (int i = 0; i < 7; i++)
            q[i] = 1;
        inventory.AdoptFundedQuantities(c, q);
        var beforeCaps = inventory.CaptureOwnedQuantities(c);
        var beforeCopper = inventory.CaptureFundedCoinValues(c);
        var beforeBank = new RuntimePlayerInventoryItem[160];
        var beforeBankCopper = new long[160];
        Assert.True(inventory.TryCaptureShopBanks(c, beforeBank, beforeBankCopper));
        Assert.True(inventory.TryCaptureShopBankOwnership(c, out var beforeBankQuantity, out var beforeBankCaps));
        Console.WriteLine($"SEVEN pre context={f.Players.HasOwnedPickupContext} phase={f.Member(2).ItemPhase is not null} shop={f.Member(2).CursorMove?.Shop is not null} q={string.Join(',', inventory.CaptureFundedQuantities(c).Take(7))} physical={string.Join(',', Enumerable.Range(0, 7).Select(i => inventory.TryGet(c, (short)i, out var value) ? value.ItemType.Value + "/" + value.Stack : "missing"))}");
        foreach (var hex in row.GetProperty("syncFrames").EnumerateArray())
        {
            var bytes = Convert.FromHexString(hex.GetString()!);
            var frame = new TerrariaFrame((ushort)bytes.Length, bytes[2], new ReadOnlySequence<byte>(bytes), new ReadOnlySequence<byte>(bytes.AsMemory(3)));
            switch (bytes[2])
            {
                case 5:
                    Assert.Equal(TerrariaPlayerEquipmentDecodeResult.Decoded, TerrariaPlayerEquipmentCodec.TryDecode(in frame, out var e));
                    f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, e.SlotId, e.Stack, e.Prefix, e.ItemNetId, e.ItemFlags)));
                    break;
                case 50:
                    Assert.Equal(TerrariaPlayerBuffDecodeResult.Decoded, TerrariaPlayerBuffCodec1458.TryDecode(in frame, out _, out var types));
                    f.Apply(new PlayerBuffTypesRuntimeCommand(c, new(c.Player.Slot, types)));
                    break;
                case 13:
                    Assert.Equal(TerrariaPlayerMovementDecodeResult.Decoded, TerrariaPlayerMovementDecoder.TryDecode(in frame, out var move));
                    var m = f.Member(2);
                    f.Apply(new PlayerMovementRuntimeCommand(c, new(c.Player.Slot, move.ControlFlags, move.MovementFlags, move.MiscFlags1, move.MiscFlags2, move.SelectedItem, m.PositionX, m.PositionY, move.HasVelocity, move.VelocityX, move.VelocityY, move.HasMount, move.MountType, false, 0, 0, 0, 0, false, 0, 0)));
                    break;
                case 138:
                    f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
                    break;
                default:
                    throw new InvalidOperationException("Unexpected original frame");
            }
            Console.WriteLine($"SEVEN frame={bytes[2]} shop={f.Member(2).CursorMove?.Shop is not null} incoming={f.Member(2).CursorMove?.Shop?.QuickBuffIncoming?.Length} sealed={f.Member(2).CursorMove?.Shop?.IsSealed}");
        }
        int steps = 0;
        while (!f.Players.CanApplyShopSource(c.Source))
        {
            Assert.True(++steps < 10000);
            f.Tick();
            Assert.InRange(f.Players.LastShopWorkSteps, 0, 64);
        }
        Console.WriteLine($"SEVEN actual ownedfood={f.Buffs(2).HasOwnedFood} duration207={f.Buffs(2).GetDuration(new(207))} q6={inventory.CaptureFundedQuantities(c)[6]} steps={steps}");
        Assert.True(f.Buffs(2).HasOwnedFood);
        for (short i = 0; i < 59; i++)
        {
            Assert.True(inventory.TryGet(c, i, out var physical));
            Assert.Equal(i < 7 ? new RuntimePlayerInventoryItem(new(ids[i]), 2, default, 0) : default, physical);
            Assert.Equal((short)0, inventory.CaptureFundedQuantities(c)[i]);
        }
        Assert.Equal(beforeCaps, inventory.CaptureOwnedQuantities(c));
        Assert.Equal(beforeCopper, inventory.CaptureFundedCoinValues(c));
        var afterBank = new RuntimePlayerInventoryItem[160];
        var afterBankCopper = new long[160];
        Assert.True(inventory.TryCaptureShopBanks(c, afterBank, afterBankCopper));
        Assert.Equal(beforeBank, afterBank);
        Assert.Equal(beforeBankCopper, afterBankCopper);
        Assert.True(inventory.TryCaptureShopBankOwnership(c, out var afterBankQuantity, out var afterBankCaps));
        Assert.Equal(beforeBankQuantity, afterBankQuantity);
        Assert.Equal(beforeBankCaps, afterBankCaps);
        Assert.Equal(172800, f.Buffs(2).GetDuration(new(207)));
    }
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "durations")]
    private static extern ref int[] BuffDurations(PlayerBuffState state);

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
                if (slot == 0 && mode is "inactive-first" or "empty-selected")
                    continue;
                var session = sessions[slot];
                session.ObserveWorldRequest();
                session.ObserveSectionRequest();
                var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(99000 + slot), session.Handle);
                Connections.Add(slot, connection);
                short id = slot == 2 ? (mode == "empty-selected" ? (short)0 : (short)28) : slot == 0 && mode == "potion-first" ? (short)28 : slot == 0 && mode == "weapon98-first" ? (short)98 : (short)0;
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
