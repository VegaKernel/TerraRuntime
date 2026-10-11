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

public static class QuickBuffOwnedBoundary1458Checks
{
    // Configured membership and custody; unchanged original local sync bytes drive State.Apply.
    // Source captures main59 + bank4forty. Other three banks are configured Air, not source facts.
    public static void Run()
    {
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using Stream stream = typeof(QuickBuffOwnedBoundary1458Checks).Assembly
                .GetManifestResourceStream($"QuickBuffSix{platform}First1458")!;
            Assert.NotNull(stream);
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var document = JsonDocument.Parse(gzip);
            foreach (var row in document.RootElement.EnumerateArray())
            {
                if (!row.GetProperty("local").GetBoolean() ||
                    row.GetProperty("syncFrames").GetArrayLength() == 0) continue;
                SourceBatch(row, funded: true, actualTick: false);
                SourceBatch(row, funded: false, actualTick: false);
                SourceBatch(row, funded: false, actualTick: true);
                SourceBatch(row, funded: true, actualTick: true);
                if (row.GetProperty("mode").GetString() != "duplicate")
                    SourceBatch(row, funded: true, actualTick: false, oneBudget: true);
                if (platform == "Linux" && row.GetProperty("mode").GetString() == "six-slots")
                {
                    SourceBatch(row, funded: true, actualTick: false, oneBudget: true, publication: 1);
                    SourceBatch(row, funded: true, actualTick: false, oneBudget: true, publication: 2);
                    SourceBatch(row, funded: true, actualTick: false, oneBudget: true, publication: 3);
                    SourceBatch(row, funded: true, actualTick: false, oneBudget: true, publication: 4);
                    SourceBatch(row, funded: true, actualTick: false, oneBudget: true, retire: true);
                    SourceBatch(row, funded: true, actualTick: false, oneBudget: true, transfer: true);
                    SourceBatch(row, funded: true, actualTick: true, oneBudget: true, provider: true);
                }
                if (platform == "Linux" && row.GetProperty("mode").GetString() is "six-slots" or "voidbag")
                    SourceBatch(row, funded: false, actualTick: true, otherActor: true);
            }
        }
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using Stream stream = typeof(QuickBuffOwnedBoundary1458Checks).Assembly
                .GetManifestResourceStream($"QuickBuffClosed{platform}First1458")!;
            Assert.NotNull(stream);
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var document = JsonDocument.Parse(gzip);
            foreach (var row in document.RootElement.EnumerateArray())
            {
                if (row.GetProperty("cycle").GetBoolean()) continue;
                SourceBatch(row, funded: true, actualTick: true);
                SourceBatch(row, funded: true, actualTick: true, ambiguous: true);
            }
        }
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using Stream stream = typeof(QuickBuffOwnedBoundary1458Checks).Assembly
                .GetManifestResourceStream($"QuickBuffBankCycle{platform}First1458")!;
            Assert.NotNull(stream);
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var document = JsonDocument.Parse(gzip);
            foreach (var row in document.RootElement.EnumerateArray())
            {
                if (row.GetProperty("cycle").GetBoolean() || row.GetProperty("syncFrames").GetArrayLength() == 0) continue;
                SourceBatch(row, funded: true, actualTick: true);
                SourceBatch(row, funded: true, actualTick: true, ambiguous: true);
            }
        }
    }

    private static void SourceBatch(JsonElement row, bool funded, bool actualTick, bool oneBudget = false, int publication = 0, bool retire = false, bool ambiguous = false, bool transfer = false, bool provider = false, bool otherActor = false)
    {
        using var fixture = new Fixture(otherActor ? "empty-first" : "inactive-first", runtimeTick: true);
        var connection = fixture.Connections[2];
        var store = Inventory(fixture.Players);
        Assert.True(fixture.Players.TryTickRemotePlayerPhase());
        var before = row.GetProperty("before");
        var beforeItems = before.GetProperty("inventory");
        for (short slot = 0; slot < 59; slot++)
        {
            var item = ReadItem(beforeItems[slot]);
            fixture.Apply(new PlayerEquipmentRuntimeCommand(connection,
                new(connection.Player.Slot, slot, item.Stack, (byte)item.Prefix.Value,
                    (short)item.ItemType.Value, item.ItemFlags)));
        }
        fixture.Apply(new PlayerInventorySyncCompleteRuntimeCommand(connection));
        int setupSlices = 0;
        while (!fixture.Players.CanApplyShopSource(connection.Source))
        {
            Assert.True(++setupSlices <= 10000);
            fixture.Players.TickShopJournalWork();
        }
        Assert.True(fixture.Players.TryTickRemotePlayerPhase());
        short[] mainFunding = new short[59];
        if (funded)
            for (int i = 0; i < 59; i++) mainFunding[i] = oneBudget ? (ReadItem(beforeItems[i]).IsEmpty ? (short)0 : (short)1) : ReadItem(beforeItems[i]).Stack;
        if (ambiguous)
        {
            Array.Clear(mainFunding);
            if (!ReadItem(beforeItems[0]).IsEmpty) mainFunding[0] = 1;
        }
        store.AdoptFundedQuantities(connection, mainFunding);
        var bank = new RuntimePlayerInventoryItem[160];
        short[] bankFunding = new short[160];
        bool allBanks = row.TryGetProperty("beforeBanks", out var sourceBanks);
        for (int i = 0; i < 160; i++)
        {
            bank[i] = allBanks ? ReadItem(sourceBanks[i / 40][i % 40])
                : i >= 120 ? ReadItem(row.GetProperty("beforeBank4")[i - 120]) : default;
            bankFunding[i] = !funded ? (short)0 : oneBudget ? (bank[i].IsEmpty ? (short)0 : (short)1) : bank[i].Stack;
        }
        if (ambiguous && allBanks)
        {
            Array.Clear(bankFunding);
            if (row.GetProperty("family").GetString() == "bank-bank")
                bankFunding[row.GetProperty("bankIndex").GetInt32() * 40] = 1;
        }
        var beforeBankItems = (RuntimePlayerInventoryItem[])bank.Clone();
        Assert.True(store.TryAdoptShopBanks(connection, bank, new long[160], bankFunding));
        var oldBuffs = fixture.Buffs(2);
        var initialBuffs = PlayerBuffState.FromSlots(
            before.GetProperty("buffType").EnumerateArray()
                .Select(value => new BuffTypeId((ushort)value.GetInt32())).ToArray(),
            before.GetProperty("buffTime").EnumerateArray().Select(value => value.GetInt32()).ToArray());
        Assert.True(Profiles(fixture.Players).TryAdoptBuffState(connection, oldBuffs, initialBuffs));
        var member = fixture.Member(2);
        Assert.NotNull(member.ItemPhase);
        member.ItemPhase = member.ItemPhase!.Value with
        {
            Selected = member.ItemPhase.Value.Selected with
            {
                ItemTime = before.GetProperty("itemTime").GetInt32(),
                ItemTimeMax = before.GetProperty("itemTimeMax").GetInt32(),
                Animation = before.GetProperty("itemAnimation").GetInt32(),
                AnimationMax = before.GetProperty("itemAnimationMax").GetInt32()
            }
        };
        member.ItemAnimation = (short)before.GetProperty("itemAnimation").GetInt32();
        // QuickBuff does not change controls: configure matching direction/movement before the measured batch.
        Replay(fixture, row, movementOnly: true);
        var clocks = member.ItemPhase.Value.Selected;

        // Configure the independently captured direct-use RNG checkpoint after phase setup.
        // State.Tick siblings subsequently exercise their own phase cursor, not whole Main parity.
        fixture.Random.SetSeed(row.GetProperty("seed").GetInt32());
        AssertSourceRandom(row.GetProperty("beforeRandom"), fixture.Random);
        var random = fixture.Random.Clone();
        var caps = store.CaptureOwnedQuantities(connection);
        var mainCopper = store.CaptureFundedCoinValues(connection);
        Assert.True(store.TryCaptureShopBankOwnership(connection, out _, out var bankCaps));
        Replay(fixture, row);
        // No economic effect may escape before this sealed journal's owned work completes.
        Assert.True(initialBuffs.HasSameSlots(fixture.Buffs(2)));
        int otherBefore = otherActor ? fixture.Member(0).ItemPhase!.Value.Selected.RevolverCritBonus : 0;
        bool providerInvoked = false;
        if (provider)
        {
            fixture.Players.SetNpcHealthGrapplingFacts(_ =>
            {
                if (!providerInvoked)
                {
                    providerInvoked = true;
                    fixture.Apply(new PlayerEquipmentRuntimeCommand(connection,
                        new(connection.Player.Slot, 30, 1, 0, 3, 0)));
                }
                return false;
            });
        }
        if (transfer)
        {
            var oldConnection = connection;
            var detach = new TaskCompletionSource<RuntimePlayerTransferState?>();
            fixture.Apply(new PlayerTransferDetachRuntimeCommand(connection, detach));
            Assert.True(detach.Task.IsCompletedSuccessfully);
            var detached = detach.Task.Result;
            Assert.NotNull(detached);
            connection = new(GameCommandSourceId.FromConnection(99122),
                new(connection.Player.Slot, new(connection.Player.Generation.Value + 1)));
            var attach = new TaskCompletionSource<bool>();
            fixture.Apply(new PlayerTransferAttachRuntimeCommand(connection, detached!, 100, 103, true, false, attach));
            Assert.True(attach.Task.IsCompletedSuccessfully && attach.Task.Result);
            fixture.Connections[2] = connection;
            Assert.Null(fixture.Member(2).CursorMove);
            var current = fixture.Member(2).CaptureSnapshot();
            fixture.Apply(new PlayerBuffTypesRuntimeCommand(oldConnection,
                new(oldConnection.Player.Slot, new BuffTypeId[] { new(5), new(16), new(112), new(114), new(115), new(117) })));
            Assert.Equal(current, fixture.Member(2).CaptureSnapshot());
        }
        if (retire)
        {
            fixture.Apply(new PlayerMovementRuntimeCommand(connection,
                new(connection.Player.Slot, member.ControlFlags, member.MovementFlags,
                    member.MiscFlags1, member.MiscFlags2, 1, member.PositionX, member.PositionY,
                    false, 0, 0, false, 0,
                    false, 0, 0, 0, 0, false, 0, 0)));
        }
        int completedObservers = 0;
        var observerFailure = new InvalidOperationException("configured completion observer failure");
        bool sawObserverFailure = false;
        int buffEventsAfterRemoval = -1;
        fixture.Events.Seen.Clear();
        fixture.Events.BuffConnections.Clear();
        var originalPublicationConnection = connection;
        if (publication != 0)
        {
            var publicationConnection = connection;
            fixture.Events.OnEquipment = (publishedConnection, _) =>
            {
                if (publication == 3 && publishedConnection != publicationConnection) return;
                completedObservers++;
                if (publication is 3 or 4 && completedObservers > 1) return;
                var sourceAfter = row.GetProperty("afterQuick");
                fixture.Buffs(2).CaptureSlots(out var observedTypes, out var observedTimes);
                Assert.Equal(sourceAfter.GetProperty("buffType").EnumerateArray()
                    .Select(value => new BuffTypeId((ushort)value.GetInt32())).ToArray(), observedTypes);
                Assert.Equal(sourceAfter.GetProperty("buffTime").EnumerateArray()
                    .Select(value => value.GetInt32()).ToArray(), observedTimes);
                for (short slot = 0; slot < 59; slot++)
                {
                    Assert.True(store.TryGet(connection, slot, out var item));
                    Assert.Equal(ReadItem(sourceAfter.GetProperty("inventory")[slot]), item);
                    int consumed = ReadItem(beforeItems[slot]).Stack - item.Stack;
                    Assert.Equal(mainFunding[slot] - consumed,
                        store.CaptureFundedQuantities(connection)[slot]);
                }
                if (completedObservers != 1) return;
                if (publication == 1) throw observerFailure;
                if (publication == 4)
                {
                    fixture.Apply(new PlayerBuffTypesRuntimeCommand(connection,
                        new(connection.Player.Slot, Array.Empty<BuffTypeId>())));
                    buffEventsAfterRemoval = fixture.Events.BuffConnections.Count;
                    return;
                }
                if (publication == 3)
                {
                    var detach = new TaskCompletionSource<RuntimePlayerTransferState?>();
                    fixture.Apply(new PlayerTransferDetachRuntimeCommand(connection, detach));
                    Assert.True(detach.Task.IsCompletedSuccessfully);
                    Assert.NotNull(detach.Task.Result);
                    connection = new(GameCommandSourceId.FromConnection(99123),
                        new(connection.Player.Slot, new(connection.Player.Generation.Value + 1)));
                    var attach = new TaskCompletionSource<bool>();
                    fixture.Apply(new PlayerTransferAttachRuntimeCommand(connection,
                        detach.Task.Result!, 100, 103, true, false, attach));
                    Assert.True(attach.Task.IsCompletedSuccessfully && attach.Task.Result);
                    fixture.Connections[2] = connection;
                    return;
                }
                fixture.Apply(new PlayerBuffTypesRuntimeCommand(connection,
                    new(connection.Player.Slot, observedTypes.Where(type => type.Value != 0).ToArray())));
            };
        }
        int slices = 0;
        while (!fixture.Players.CanApplyShopSource(connection.Source))
        {
            Assert.True(++slices <= 10000, "bounded diagnostic guard, not a production search cutoff");
            try
            {
                if (actualTick) fixture.Tick();
                else fixture.Players.TickShopJournalWork();
            }
            catch (InvalidOperationException error) when (publication == 1 && ReferenceEquals(error, observerFailure))
            {
                sawObserverFailure = true;
            }
            Assert.InRange(fixture.Players.LastShopWorkSteps, 0, 64);
        }
        if (provider) Assert.True(providerInvoked);
        if (otherActor)
        {
            Assert.NotNull(fixture.Member(0).ItemPhase);
            Assert.True(fixture.Member(0).ItemPhase!.Value.Selected.RevolverCritBonus < otherBefore,
                "unfunded sibling does not freeze the other owned phase");
        }
        if (publication != 0)
        {
            Assert.Equal(publication == 3 ? 1 : 6, completedObservers);
            if (publication == 3) Assert.DoesNotContain(originalPublicationConnection, fixture.Events.BuffConnections);
            else if (publication == 4) Assert.Equal(buffEventsAfterRemoval, fixture.Events.BuffConnections.Count);
            else Assert.Contains((byte)50, fixture.Events.Seen);
            Assert.Equal(publication == 1, sawObserverFailure);
        }
        Assert.Equal(caps, store.CaptureOwnedQuantities(connection));
        Assert.Equal(mainCopper, store.CaptureFundedCoinValues(connection));
        Assert.True(store.TryCaptureShopBankOwnership(connection, out var afterBankFunding, out var afterBankCaps));
        Assert.Equal(bankCaps, afterBankCaps);
        var afterBankCopper = new long[160];
        Assert.True(store.TryCaptureShopBanks(connection, bank, afterBankCopper));
        Assert.All(afterBankCopper, value => Assert.Equal(0L, value));
        var after = row.GetProperty("afterQuick");
        for (short slot = 0; slot < 59; slot++)
        {
            Assert.True(store.TryGet(connection, slot, out var physical));
            Assert.Equal(provider && slot == 30 ? new RuntimePlayerInventoryItem(new(3), 1, default, 0)
                : ReadItem(after.GetProperty("inventory")[slot]), physical);
            int consumed = ReadItem(beforeItems[slot]).Stack - physical.Stack;
            Assert.Equal(!funded ? 0 : retire || ambiguous || transfer || provider ? Math.Min(mainFunding[slot], physical.Stack) : mainFunding[slot] - consumed,
                store.CaptureFundedQuantities(connection)[slot]);
        }
        var afterBank = allBanks ? row.GetProperty("afterQuickBanks")
            : row.TryGetProperty("afterBank4", out var directBank) ? directBank : row.GetProperty("afterSyncBank4");
        for (int i = 0; i < 160; i++)
        {
            Assert.Equal(allBanks ? ReadItem(afterBank[i / 40][i % 40])
                : i < 120 ? default : ReadItem(afterBank[i - 120]), bank[i]);
            int consumed = beforeBankItems[i].Stack - bank[i].Stack;
            Assert.Equal(!funded ? 0 : retire || ambiguous || transfer || provider ? Math.Min(bankFunding[i], bank[i].Stack) : bankFunding[i] - consumed, afterBankFunding[i]);
        }
        fixture.Buffs(2).CaptureSlots(out var types, out var times);
        var expected = funded && !retire && !ambiguous && !transfer && !provider ? after : before;
        if (publication == 4)
        {
            Assert.All(types, type => Assert.Equal((ushort)0, type.Value));
            Assert.All(times, time => Assert.Equal(0, time));
            fixture.Apply(new PlayerBuffTypesRuntimeCommand(connection,
                new(connection.Player.Slot, after.GetProperty("buffType").EnumerateArray()
                    .Select(value => new BuffTypeId((ushort)value.GetInt32())).Where(type => type.Value != 0).ToArray())));
            fixture.Buffs(2).CaptureSlots(out var replayTypes, out var replayTimes);
            Assert.Equal(types, replayTypes);
            Assert.Equal(times, replayTimes);
        }
        else
        {
        Assert.Equal(expected.GetProperty("buffType").EnumerateArray()
            .Select(value => new BuffTypeId((ushort)value.GetInt32())).ToArray(), types);
        Assert.Equal(expected.GetProperty("buffTime").EnumerateArray()
            .Select(value => value.GetInt32()).ToArray(), times);
        }
        if (!actualTick)
        {
            Assert.Equal(clocks, fixture.Member(2).ItemPhase!.Value.Selected);
            Assert.True(fixture.Random.HasSameState(random));
            AssertSourceRandom(row.GetProperty("afterRandom"), fixture.Random);
            Assert.Equal(row.GetProperty("afterNext").GetInt32(), fixture.Random.Clone().Next());
        }
        if (funded && !retire && !ambiguous && !transfer && !provider && publication == 0)
        {
            var retainedTypes = types.Where(type => type.Value != 0).Skip(1).ToArray();
            fixture.Apply(new PlayerBuffTypesRuntimeCommand(connection,
                new(connection.Player.Slot, retainedTypes)));
            fixture.Buffs(2).CaptureSlots(out var remainingTypes, out var remainingTimes);
            Assert.Equal(retainedTypes, fixture.Buffs(2).CaptureTypes());
            for (int i = 0; i < retainedTypes.Length; i++)
                Assert.Equal(times[i + 1], remainingTimes[i]);
            fixture.Apply(new PlayerBuffTypesRuntimeCommand(connection,
                new(connection.Player.Slot, types.Where(type => type.Value != 0).ToArray())));
            fixture.Buffs(2).CaptureSlots(out var replayTypes, out var replayTimes);
            Assert.Equal(remainingTypes, replayTypes);
            Assert.Equal(remainingTimes, replayTimes);
        }
        if (publication is 3 or 4)
        {
            fixture.Events.OnEquipment = null;
            fixture.Apply(new PlayerEquipmentRuntimeCommand(connection,
                new(connection.Player.Slot, 31, 1, 0, 3, 0)));
            fixture.Apply(new PlayerInventorySyncCompleteRuntimeCommand(connection));
            int followingSlices = 0;
            while (!fixture.Players.CanApplyShopSource(connection.Source))
            {
                Assert.True(++followingSlices <= 10000);
                fixture.Players.TickShopJournalWork();
            }
            Assert.True(store.TryGet(connection, 31, out var followingItem));
            Assert.Equal(new RuntimePlayerInventoryItem(new(3), 1, default, 0), followingItem);
            Assert.Equal(0, store.CaptureFundedQuantities(connection)[31]);
            fixture.Buffs(2).CaptureSlots(out var followingTypes, out var followingTimes);
            Assert.Equal(types, followingTypes);
            Assert.Equal(times, followingTimes);
            Assert.Equal(caps, store.CaptureOwnedQuantities(connection));
            Assert.Equal(mainCopper, store.CaptureFundedCoinValues(connection));
        }
        // Actual State.Tick sibling intentionally permits its independently owned phase clocks/RNG.
    }

    private static RuntimePlayerInventoryItem ReadItem(JsonElement item)
    {
        short stack = (short)item.GetProperty("stack").GetInt32();
        ushort type = (ushort)item.GetProperty("type").GetInt32();
        return stack <= 0 || type == 0 ? default : new(new(type), stack,
            new((byte)item.GetProperty("prefix").GetInt32()),
            item.GetProperty("favorite").GetBoolean() ? (byte)1 : (byte)0);
    }

    private static void AssertSourceRandom(JsonElement expected, VanillaUnifiedRandom1458 random)
    {
        Assert.Equal(expected.GetProperty("cursor").GetUInt32(), RandomCursor(random));
        Assert.Equal(expected.GetProperty("state").EnumerateArray().Select(value => value.GetInt32()).ToArray(),
            RandomState(random));
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "seedArray")]
    private static extern ref int[] RandomState(VanillaUnifiedRandom1458 random);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inext")]
    private static extern ref uint RandomCursor(VanillaUnifiedRandom1458 random);

    private static void Replay(Fixture fixture, JsonElement row, bool movementOnly = false)
    {
        var connection = fixture.Connections[2];
        var member = fixture.Member(2);
        foreach (var hex in row.GetProperty("syncFrames").EnumerateArray())
        {
            byte[] bytes = Convert.FromHexString(hex.GetString()!);
            if (movementOnly && bytes[2] != 13) continue;
            var frame = new TerrariaFrame((ushort)bytes.Length, bytes[2],
                new ReadOnlySequence<byte>(bytes), new ReadOnlySequence<byte>(bytes.AsMemory(3)));
            switch (bytes[2])
            {
                case 5:
                    Assert.Equal(TerrariaPlayerEquipmentDecodeResult.Decoded,
                        TerrariaPlayerEquipmentCodec.TryDecode(in frame, out var equipment));
                    fixture.Apply(new PlayerEquipmentRuntimeCommand(connection,
                        new(connection.Player.Slot, equipment.SlotId, equipment.Stack,
                            equipment.Prefix, equipment.ItemNetId, equipment.ItemFlags)));
                    break;
                case 50:
                    Assert.Equal(TerrariaPlayerBuffDecodeResult.Decoded,
                        TerrariaPlayerBuffCodec1458.TryDecode(in frame, out _, out var types));
                    fixture.Apply(new PlayerBuffTypesRuntimeCommand(connection, new(connection.Player.Slot, types)));
                    break;
                case 13:
                    Assert.Equal(TerrariaPlayerMovementDecodeResult.Decoded,
                        TerrariaPlayerMovementDecoder.TryDecode(in frame, out var move));
                    fixture.Apply(new PlayerMovementRuntimeCommand(connection,
                        new(connection.Player.Slot, move.ControlFlags, move.MovementFlags,
                            move.MiscFlags1, move.MiscFlags2, move.SelectedItem,
                            member.PositionX, member.PositionY, move.HasVelocity, move.VelocityX,
                            move.VelocityY, move.HasMount, move.MountType, false, 0, 0, 0, 0, false, 0, 0)));
                    break;
                case 138:
                    fixture.Apply(new PlayerInventorySyncCompleteRuntimeCommand(connection));
                    break;
                default:
                    throw new InvalidOperationException("Unaccounted original QuickBuff packet " + bytes[2]);
            }
        }
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
