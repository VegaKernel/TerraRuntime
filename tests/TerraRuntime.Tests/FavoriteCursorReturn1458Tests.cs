using System.IO.Compression;
using System.Text.Json;
using System.Reflection;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Worlds;
using Xunit;
using Fixture = TerraRuntime.Tests.OwnedHumanPickupAuthority1458Tests.Fixture;

namespace TerraRuntime.Tests;

public sealed class FavoriteCursorReturn1458Tests
{
    [Fact]
    public async Task Original_favorite_returns_match_full_images_and_funded_residual_before_observers()
    {
        using var stream = typeof(FavoriteCursorReturn1458Tests).Assembly.GetManifestResourceStream("FavoriteReturn1458");
        Assert.NotNull(stream);
        using var gzip = new GZipStream(stream!, CompressionMode.Decompress);
        using var source = JsonDocument.Parse(gzip);
        using var ambiguityStream = typeof(FavoriteCursorReturn1458Tests).Assembly.GetManifestResourceStream("FavoriteReturnAmbiguity1458");
        Assert.NotNull(ambiguityStream);
        using var ambiguityGzip = new GZipStream(ambiguityStream!, CompressionMode.Decompress);
        using var ambiguity = JsonDocument.Parse(ambiguityGzip);
        int compared = 0;
        foreach (string platform in new[] { "linux", "windowsClr4X86" })
        foreach (var row in source.RootElement.GetProperty(platform).GetProperty("rows").EnumerateArray()
                     .Concat(ambiguity.RootElement.GetProperty(platform).GetProperty("rows").EnumerateArray()))
        {
            if (!row.GetProperty("profile").GetString()!.StartsWith("favorite_mouse58_", StringComparison.Ordinal)) continue;
            using var f = new Fixture(platform != "linux");
            Seed(f, row);
            var expected = row.GetProperty("after").EnumerateArray().Select(Read).ToArray();
            bool ground = row.GetProperty("ground").GetProperty("active").GetBoolean();
            int observations = 0;
            f.Events.Seen.Clear();
            f.Events.Callback = () => { observations++; Assert.Equal(expected, f.ItemImage()); AssertFunding(f, expected, ground ? row.GetProperty("ground").GetProperty("item").GetProperty("stack").GetInt32() : 0); };
            f.WorldEvents.Callback = () => { observations++; Assert.Equal(expected, f.ItemImage()); AssertFunding(f, expected, row.GetProperty("ground").GetProperty("item").GetProperty("stack").GetInt32()); };
            if (ground)
            {
                var body = row.GetProperty("ground"); var item = body.GetProperty("item");
                var position = body.GetProperty("position"); var velocity = body.GetProperty("velocity");
                var proposal = new WorldItemDropStateUpdate(position.GetProperty("x").GetSingle() + 1200,
                    position.GetProperty("y").GetSingle() + 1206, velocity.GetProperty("x").GetSingle(), velocity.GetProperty("y").GetSingle(),
                    item.GetProperty("stack").GetInt16(), item.GetProperty("prefix").GetByte(), WorldItemOwnershipMode.GrabDelayForLocalPlayer,
                    item.GetProperty("type").GetInt16(), false, 0, body.GetProperty("enemyGrabDelay").GetByte());
                var completion = new TaskCompletionSource<WorldItemSnapshot?>();
                f.State.Apply(new WorldItemAllocateRuntimeCommand(f.Connection, proposal, completion));
                var accepted = await completion.Task;
                Assert.NotNull(accepted);
                Assert.Equal(proposal.Stack, accepted!.Value.Stack);
                Assert.True(f.Items.TryGetFundedQuantity(accepted.Value, out var funded));
                Assert.Equal(proposal.Stack, funded);
            }
            else
            {
                var before = f.ItemImage();
                short[] images = Enumerable.Range(0, 59).Where(i => before[i] != expected[i]).Select(i => checked((short)i)).ToArray();
                foreach (short slot in images)
                {
                    Send(f, slot, expected[slot]);
                    if (slot != 58) Assert.Equal(before, f.ItemImage());
                }
            }
            Assert.Equal(expected, f.ItemImage());
            Assert.Equal(row.GetProperty("afterClocks").GetProperty("animation").GetInt32(), f.Member.ItemAnimation);
            Assert.True(observations > 0);
            compared++;
        }
        Assert.Equal(76, compared);
    }

    [Fact]
    public async Task Full_fit_transfer_merges_cursor_and_partial_transfer_keeps_actor_attached()
    {
        foreach (bool fits in new[] { true, false })
        {
            using var f = new Fixture();
            OwnedHumanPickupAuthority1458Tests.SeedFundedCursorTuple(f, new(new(110), 20, new(0), 0), 58);
            for (short slot = 0; slot < 50; slot++) f.Set(slot, 3, 9999);
            f.Set(1, 110, fits ? (short)9979 : (short)9992);
            f.Set(58, 110, 20, flags: 1);
            var before = f.ItemImage();
            var completion = new TaskCompletionSource<RuntimePlayerTransferState?>();
            f.State.Apply(new PlayerTransferDetachRuntimeCommand(f.Connection, completion));
            var transfer = await completion.Task;
            if (fits)
            {
                Assert.NotNull(transfer);
                Assert.Equal(9999, transfer!.Inventory[1].Stack);
                Assert.True(transfer.Inventory[58].IsEmpty);
                Assert.Equal(20, transfer.FundedInventoryQuantities![1]);
                Assert.Equal(0, transfer.FundedInventoryQuantities[58]);
            }
            else
            {
                Assert.Null(transfer);
                Assert.Equal(before, f.ItemImage());
                Assert.True(f.Players.TryGet(f.Connection, out _));
            }
        }
        using (var f = new Fixture())
        {
            f.Set(50, 71, 99);
            f.Drop(71, 1);
            f.State.Tick();
            Assert.True(Store(f).TryGetFundedCoinValue(f.Connection, 50, out long value));
            Assert.Equal(1, value);
            OwnedHumanPickupAuthority1458Tests.SeedFundedCursorTuple(f, new(new(110), 20, new(0), 0), 58);
            for (short slot = 0; slot < 50; slot++) f.Set(slot, 3, 9999);
            f.Set(1, 110, 9979);
            f.Set(58, 110, 20, flags: 1);
            var detach = new TaskCompletionSource<RuntimePlayerTransferState?>();
            f.State.Apply(new PlayerTransferDetachRuntimeCommand(f.Connection, detach));
            var transfer = await detach.Task;
            Assert.NotNull(transfer);
            Assert.Equal(9999, transfer!.Inventory[1].Stack);
            Assert.True(transfer.Inventory[58].IsEmpty);
            Assert.Equal(20, transfer.FundedInventoryQuantities![1]);
            Assert.Equal(72, transfer.Inventory[50].ItemType.Value);
            Assert.Equal(1, transfer.FundedInventoryCoinValues[50]);
        }
        using (var f = new Fixture())
        {
            // A fractional copper budget survives cursor transfer without becoming
            // a fully funded silver unit or being erased by noncoin normalization.
            for (short slot = 50; slot < 54; slot++) f.Set(slot, 3, 9999);
            f.Set(0, 71, 99);
            f.Drop(71, 1);
            f.State.Tick();
            Assert.Equal(72, f.ItemImage()[0].ItemType.Value);
            Assert.True(Store(f).TryGetFundedCoinValue(f.Connection, 0, out long value));
            Assert.Equal(1, value);
            f.Set(0, 0, 0);
            f.Set(58, 72, 1);
            Assert.True(Store(f).TryGetFundedCoinValue(f.Connection, 58, out value));
            Assert.Equal(1, value);
            var detach = new TaskCompletionSource<RuntimePlayerTransferState?>();
            f.State.Apply(new PlayerTransferDetachRuntimeCommand(f.Connection, detach));
            var transfer = await detach.Task;
            Assert.NotNull(transfer);
            Assert.Equal(72, transfer!.Inventory[0].ItemType.Value);
            Assert.True(transfer.Inventory[58].IsEmpty);
            Assert.Equal(1, transfer.FundedInventoryCoinValues[0]);
            Assert.Equal(0, transfer.FundedInventoryCoinValues[58]);
            f.Connection = new(GameCommandSourceId.FromConnection(98904),
                new(f.Connection.Player.Slot, new(f.Connection.Player.Generation.Value + 1)));
            var attach = new TaskCompletionSource<bool>();
            f.State.Apply(new PlayerTransferAttachRuntimeCommand(f.Connection, transfer, 100, 103, true, false, attach));
            Assert.True(await attach.Task);
            Assert.True(Store(f).TryGetFundedCoinValue(f.Connection, 0, out value));
            Assert.Equal(1, value);
            var state = f.Member.CaptureSnapshot();
            var body = new WorldItemDropStateUpdate(state.PositionX + 2, state.PositionY + 13, 4, -2,
                1, 0, WorldItemOwnershipMode.GrabDelayForLocalPlayer, 72, false, 0, 100);
            var drop = new TaskCompletionSource<WorldItemSnapshot?>();
            f.State.Apply(new WorldItemAllocateRuntimeCommand(f.Connection, body, drop));
            Assert.Null(await drop.Task);
            Assert.Equal(0, f.Items.ActiveCount);
            f.Set(0, 72, 2);
            Assert.True(Store(f).TryGetFundedCoinValue(f.Connection, 0, out value));
            Assert.Equal(1, value);
            f.Set(0, 73, 1);
            Assert.True(Store(f).TryGetFundedCoinValue(f.Connection, 0, out value));
            Assert.Equal(0, value);
        }
        using (var f = NewFavorite())
        {
            Assert.True(f.Players.TryPrepareCursorReturn(f.Connection, false, out var prepared));
            Assert.NotNull(prepared);
            Assert.True(Store(f).TrySet(f.Connection, new(f.Connection.Player.Slot, 2, 1, 0, 9, 0)));
            Assert.False(prepared!.TryAdoptUnpublished());
            Assert.Equal(20, f.ItemImage()[58].Stack);
        }
        using (var f = NewFavorite())
        {
            Assert.True(f.Players.TryPrepareCursorReturn(f.Connection, false, out var prepared));
            f.State.Apply(new PlayerHealthRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, 77, 400)));
            Assert.False(prepared!.TryAdoptUnpublished());
            Assert.Equal(20, f.ItemImage()[58].Stack);
            Assert.Equal(77, f.Member.Life);
        }
        using (var f = NewFavorite())
        {
            f.Set(0, 110, 20, flags: 1);
            Assert.True(f.ItemImage()[0].IsEmpty);
            Assert.Equal(20, f.ItemImage()[58].Stack);
            for (int tick = 0; tick < 3; tick++) f.State.Tick();
            f.Set(58, 0, 0);
            Assert.Equal(20, f.ItemImage()[0].Stack);
            Assert.True(f.ItemImage()[58].IsEmpty);
            Assert.True(Store(f).TryGetFundedQuantity(f.Connection, 0, out var funding));
            Assert.Equal(20, funding);
        }
        using (var f = NewFavorite())
        {
            f.Set(0, 110, 20, flags: 1);
            var other = new ConnectionHandle(GameCommandSourceId.FromConnection(98903), new(new PlayerSlotId(1), new(1)));
            f.State.Apply(new PlayerEquipmentRuntimeCommand(other, new(other.Player.Slot, 0, 7, 0, 3, 0)));
            f.Set(58, 0, 0);
            Assert.Equal(20, f.ItemImage()[0].Stack);
            Assert.True(Store(f).TryGetFundedQuantity(f.Connection, 0, out var funding));
            Assert.Equal(20, funding);
            Assert.True(Store(f).TryGet(other, 0, out var disjoint));
            Assert.Equal(7, disjoint.Stack);
        }
        using (var f = new Fixture())
        {
            OwnedHumanPickupAuthority1458Tests.SeedFundedCursorTuple(f, new(new(3), 3, new(0), 0), 58);
            f.Set(58, 3, 20, flags: 1);
            for (short slot = 0; slot < 50; slot++) f.Set(slot, 9, 9999);
            Assert.False(f.Players.TryPrepareCursorReturn(f.Connection, true, out _));
            Assert.Equal(20, f.ItemImage()[58].Stack);
            Assert.True(Store(f).TryGetFundedQuantity(f.Connection, 58, out var funded));
            Assert.Equal(3, funded);
        }
        using (var f = NewFavorite())
        {
            Assert.True(f.Players.TryPrepareCursorReturn(f.Connection, false, out var prepared));
            Assert.True(prepared!.TryAdoptUnpublished());
            int observed = 0;
            f.Events.Seen.Clear();
            f.Events.Callback = () =>
            {
                if (++observed == 1)
                {
                    Assert.Equal(20, f.ItemImage()[0].Stack);
                    Assert.True(f.ItemImage()[58].IsEmpty);
                    f.Set(58, 110, 20); // Reentry cannot reopen the accepted return.
                    throw new InvalidOperationException("first-return-observer");
                }
            };
            Assert.Throws<InvalidOperationException>(prepared.Publish);
            Assert.Equal(new short[] { 0, 58 }, f.Events.Seen);
            Assert.True(f.ItemImage()[58].IsEmpty);
            prepared.Publish();
            Assert.Equal(2, observed);
        }
        using (var f = NewFavorite())
        {
            Assert.True(f.Players.TryPrepareCursorReturn(f.Connection, false, out var prepared));
            Assert.True(prepared!.TryAdoptUnpublished());
            f.Events.Seen.Clear();
            f.Events.Callback = () =>
            {
                f.Events.Callback = null;
                Assert.True(Store(f).TrySet(f.Connection, new(f.Connection.Player.Slot, 58, 7, 0, 9, 0)));
            };
            prepared.Publish();
            Assert.Equal(new short[] { 0 }, f.Events.Seen);
            Assert.Equal(9, f.ItemImage()[58].ItemType.Value);
            Assert.Equal(7, f.ItemImage()[58].Stack);
        }
        using (var f = NewFavorite())
        {
            for (short slot = 0; slot < 50; slot++) f.Set(slot, 3, 9999);
            f.Set(1, 110, 9992);
            var world = new RuntimeWorldItemStore();
            world.AttachOwnerFactsProvider(new OwnedHumanPickupAuthority1458Tests.LateCurrentnessProvider(() => f.Set(2, 9, 1)));
            var authority = new WorldItemAuthority(f.Players, world, new SystemWorldItemSpawnRandom(0), null);
            var completion = new TaskCompletionSource<WorldItemSnapshot?>();
            authority.TryApply(new WorldItemAllocateRuntimeCommand(f.Connection,
                new(1602, 1619, 4, -2, 13, 0, WorldItemOwnershipMode.GrabDelayForLocalPlayer, 110, false, 0, 100), completion));
            Assert.Null(await completion.Task);
            Assert.Equal(0, world.ActiveCount);
            Assert.Equal(20, f.ItemImage()[58].Stack);
            Assert.Equal(9992, f.ItemImage()[1].Stack);
        }
        using (var f = new Fixture())
        {
            OwnedHumanPickupAuthority1458Tests.SeedFundedCursorTuple(f, new(new(3), 20, new(0), 0), 58);
            for (short slot = 0; slot < 50; slot++) f.Set(slot, 9, 9999);
            f.Set(0, 3, 9992);
            f.Set(1, 3, 9986);
            f.Set(58, 3, 20, flags: 1);
            f.Set(0, 3, 9999); // Also the first image of a full multi-slot GetItem return.
            Assert.Equal(9986, f.ItemImage()[1].Stack);
            f.Set(58, 3, 13, flags: 1); // Original LeftClick completion, not full return.
            Assert.Equal(9986, f.ItemImage()[1].Stack);
            Assert.Equal(13, f.ItemImage()[58].Stack);
            Assert.True(Store(f).TryGetFundedQuantity(f.Connection, 58, out var funding));
            // Source Left swaps the complete old cursor into the slot before merging.
            // Preserve the existing pair funding policy instead of turning it into GetItem.
            Assert.Equal(0, funding);
            Assert.True(Store(f).TryGetFundedQuantity(f.Connection, 0, out var destinationFunding));
            Assert.Equal(20, destinationFunding);
        }
        foreach (short budget in new short[] { 12, 13, 20 })
        {
            using var f = new Fixture();
            OwnedHumanPickupAuthority1458Tests.SeedFundedCursorTuple(f, new(new(3), budget, new(0), 0), 58);
            f.Set(58, 3, 20, flags: 1);
            for (short slot = 0; slot < 50; slot++) f.Set(slot, 9, 9999);
            f.Set(1, 3, 9992);
            bool prepared = f.Players.TryPrepareCursorReturn(f.Connection, true, out var returning);
            Assert.Equal(budget >= 13, prepared);
            if (!prepared) { Assert.Equal(20, f.ItemImage()[58].Stack); continue; }
            Assert.Equal(13, returning!.Residual.Stack);
            Assert.True(returning.TryAdoptUnpublished());
            Assert.True(f.ItemImage()[58].IsEmpty);
            Assert.Equal(9999, f.ItemImage()[1].Stack);
            Assert.True(Store(f).TryGetFundedQuantity(f.Connection, 1, out var receivedFunding));
            Assert.Equal(budget - 13, receivedFunding);
            Assert.Equal(budget, receivedFunding + returning.Residual.Stack);
        }
        foreach (string caseName in new[] { "duplicate", "early58", "early1", "input", "phase", "sender-write", "valid" })
        {
            using var f = NewMultiFavorite();
            var before = f.ItemImage();
            if (caseName == "early1") f.Set(1, 3, 9999);
            else
            {
                f.Set(0, 3, 9999);
                Assert.Equal(before, f.ItemImage());
                if (caseName == "duplicate") { f.Set(0, 3, 9999); Assert.Equal(before, f.ItemImage()); }
                if (caseName == "input") f.State.Apply(new PlayerHealthRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, 77, 400)));
                if (caseName == "phase") { f.Member.ItemPhase = null; f.Member.ItemPhase = RuntimePlayerItemPhase1458.Constructor; }
                if (caseName == "sender-write") Assert.True(Store(f).TrySet(f.Connection, new(f.Connection.Player.Slot, 2, 1, 0, 9, 0)));
                if (caseName != "early58") f.Set(1, 3, 9999);
                f.Set(58, 0, 0);
            }
            bool accepts = caseName is "valid" or "duplicate";
            if (accepts)
            {
                Assert.Equal(9999, f.ItemImage()[0].Stack);
                Assert.Equal(9999, f.ItemImage()[1].Stack);
                Assert.True(f.ItemImage()[58].IsEmpty);
            }
            else
            {
                Assert.True(Store(f).TryGetFundedQuantity(f.Connection, 58, out var remaining));
                Assert.InRange((int)remaining, 0, 20);
                // After receipt retirement, a later legacy raw Air image may destroy
                // cursor funding. It cannot adopt the rejected Return credits or mint.
                Assert.True(Store(f).TryGetFundedQuantity(f.Connection, 0, out var firstFunding));
                Assert.True(Store(f).TryGetFundedQuantity(f.Connection, 1, out var secondFunding));
                Assert.Equal(0, firstFunding);
                Assert.Equal(0, secondFunding);
                Assert.NotEqual(9999, f.ItemImage()[0].Stack);
            }
        }
        foreach (short budget in new short[] { 12, 13, 20 })
        {
            using var f = new Fixture();
            OwnedHumanPickupAuthority1458Tests.SeedFundedCursorTuple(f, new(new(3), budget, new(0), 0), 58);
            f.Set(58, 3, 20, flags: 1);
            for (short slot = 0; slot < 50; slot++) f.Set(slot, 9, 9999);
            f.Set(1, 3, 9992);
            var before = f.ItemImage();
            int observed = 0;
            f.WorldEvents.Callback = () =>
            {
                observed++;
                Assert.True(f.ItemImage()[58].IsEmpty);
                Assert.Equal(9999, f.ItemImage()[1].Stack);
                Assert.True(Store(f).TryGetFundedQuantity(f.Connection, 1, out var returnedFunding));
                Assert.Equal(budget - 13, returnedFunding);
            };
            var completion = new TaskCompletionSource<WorldItemSnapshot?>();
            f.State.Apply(new WorldItemAllocateRuntimeCommand(f.Connection,
                new(1602, 1619, 4, -2, 13, 0, WorldItemOwnershipMode.GrabDelayForLocalPlayer, 3, false, 0, 100), completion));
            var accepted = await completion.Task;
            if (budget == 12)
            {
                Assert.Null(accepted);
                Assert.Equal(before, f.ItemImage());
                Assert.Equal(0, f.Items.ActiveCount);
                Assert.Equal(0, observed);
            }
            else
            {
                Assert.NotNull(accepted);
                Assert.True(f.Items.TryGetFundedQuantity(accepted!.Value, out var groundFunding));
                Assert.Equal(13, groundFunding);
                Assert.True(Store(f).TryGetFundedQuantity(f.Connection, 1, out var returnedFunding));
                Assert.Equal(budget, groundFunding + returnedFunding);
                Assert.True(observed > 0);
            }
        }
    }

    private static Fixture NewFavorite()
    {
        var f = new Fixture();
        OwnedHumanPickupAuthority1458Tests.SeedFundedCursorTuple(f, new(new(110), 20, new(0), 0), 58);
        f.Set(58, 110, 20, flags: 1);
        return f;
    }
    private static Fixture NewMultiFavorite()
    {
        var f = new Fixture();
        OwnedHumanPickupAuthority1458Tests.SeedFundedCursorTuple(f, new(new(3), 20, new(0), 0), 58);
        for (short slot = 0; slot < 50; slot++) f.Set(slot, 9, 9999);
        f.Set(0, 3, 9992); f.Set(1, 3, 9986); f.Set(58, 3, 20, flags: 1);
        return f;
    }
    private static RuntimePlayerInventoryStore Store(Fixture f) => (RuntimePlayerInventoryStore)typeof(PlayerAuthority)
        .GetField("inventory", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Players)!;
    private static void AssertFunding(Fixture f, RuntimePlayerInventoryItem[] expected, int residual)
    {
        int total = 0;
        for (short slot = 0; slot < 59; slot++)
        {
            Assert.True(Store(f).TryGetFundedQuantity(f.Connection, slot, out var funded));
            Assert.InRange((int)funded, 0, expected[slot].Stack);
            total += funded;
        }
        Assert.Equal(20, total + residual);
    }

    private static void Seed(Fixture f, JsonElement row)
    {
        short id = row.GetProperty("id").GetInt16();
        OwnedHumanPickupAuthority1458Tests.SeedFundedCursorTuple(f, new(new(id), 20, new(0), 0), 58);
        short slot = 0;
        foreach (var item in row.GetProperty("before").EnumerateArray())
        {
            if (slot != 58) Send(f, slot, Read(item));
            slot++;
        }
        Send(f, 58, Read(row.GetProperty("before")[58]));
        Assert.Equal(row.GetProperty("before").EnumerateArray().Select(Read).ToArray(), f.ItemImage());
        f.Events.Seen.Clear();
    }
    private static RuntimePlayerInventoryItem Read(JsonElement item) => item.GetProperty("type").GetInt32() == 0 ? default :
        new(new(item.GetProperty("type").GetInt32()), item.GetProperty("stack").GetInt16(), new(item.GetProperty("prefix").GetInt32()),
            item.GetProperty("favorite").GetBoolean() ? (byte)1 : (byte)0);
    private static void Send(Fixture f, short slot, RuntimePlayerInventoryItem item) =>
        f.Set(slot, checked((short)item.ItemType.Value), item.Stack, checked((byte)item.Prefix.Value), item.ItemFlags);
}
