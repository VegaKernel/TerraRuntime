using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.World;
using TerraRuntime.Protocol;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class OwnedHumanPickupAuthority1458Tests
{
    [Fact]
    public void Original_contact_profiles_preserve_storage_and_refused_geometry()
    {
        using var stream = typeof(OwnedHumanPickupAuthority1458Tests).Assembly.GetManifestResourceStream("OwnedPickupContact1458");
        Assert.NotNull(stream); using var gzip = new GZipStream(stream!, CompressionMode.Decompress);
        using var source = JsonDocument.Parse(gzip);
        int compared = 0;
        foreach (string platform in new[] { "linux", "windowsClr4X86" })
            foreach (var row in source.RootElement.GetProperty(platform).GetProperty("rows").EnumerateArray())
            {
                using var f = new Fixture(platform == "windowsClr4X86");
                var config = row.GetProperty("configured");
                short slot = 0;
                foreach (var sourceItem in row.GetProperty("before").EnumerateArray())
                {
                    f.Set(slot++, (short)I(sourceItem, "type"), (short)I(sourceItem, "stack"), (byte)I(sourceItem, "prefix"), sourceItem.GetProperty("favorite").GetBoolean() ? (byte)1 : (byte)0);
                }
                f.Member.ItemPhase = f.Member.ItemPhase!.Value with { Selected = f.Member.ItemPhase.Value.Selected with { Animation = I(config, "animation") } };
                f.Member.IsDead = config.GetProperty("dead").GetBoolean();
                if (config.GetProperty("preventAllItemPickups").GetBoolean()) f.Set(57, 4346, 1);
                var item = f.Drop((short)I(row, "id"), 20, 1600 + F(config, "groundX") - 400,
                    (byte)I(config, "reserved"), F(config, "shimmerTime"), config.GetProperty("shimmered").GetBoolean(),
                    F(config, "velocityX"), I(config, "grabDelayTime"), (byte)I(config, "grabDelayPlayer"));
                var before = f.ItemImage();
                f.Runtime.WorldItems.TickOwnedHumanPickups();
                // Original local GrabItems is the oracle except explicit authority policy:
                // source direct dead caller can pickup, source remote my255 does not invoke pickup.
                string profile = row.GetProperty("profile").GetString()!;
                bool policyDead = profile == "dead_directcaller";
                var expected = profile == "remote_my255" ? source.RootElement.GetProperty(platform).GetProperty("rows").EnumerateArray().Single(r => I(r, "id") == I(row, "id") && r.GetProperty("profile").GetString() == "contact").GetProperty("after") : row.GetProperty("after");
                int index = 0;
                foreach (var wanted in expected.EnumerateArray())
                {
                    Assert.True(f.Players.TryGetInventoryItem(f.Connection, (short)index, out var actual));
                    if (policyDead || profile == "prevent_all") Assert.Equal(before[index], actual);
                    else { Assert.True(I(wanted, "type") == actual.ItemType.Value, $"{platform}/{I(row, "id")}/{profile}/slot{index} expected{I(wanted, "type")} actual{actual}"); Assert.Equal(I(wanted, "stack"), (int)actual.Stack); Assert.Equal(I(wanted, "prefix"), actual.Prefix.Value); }
                    index++;
                }
                bool taken = !policyDead && profile != "prevent_all" && (profile == "remote_my255" || I(row.GetProperty("residual"), "stack") == 0);
                Assert.Equal(!taken, f.Items.TryGetActive(item.Handle.Slot, out _));
                compared++;
            }
        Assert.Equal(304, compared);
        using var storageStream = typeof(OwnedHumanPickupAuthority1458Tests).Assembly.GetManifestResourceStream("OwnedPickupStorage1458");
        Assert.NotNull(storageStream); using var storageGzip = new GZipStream(storageStream!, CompressionMode.Decompress); using var storageSource = JsonDocument.Parse(storageGzip);
        int storageComparisons = 0; int omittedFavoriteProfiles = 0;
        foreach (string platform in new[] { "linux", "windowsClr4X86" })
            foreach (var row in storageSource.RootElement.GetProperty(platform).GetProperty("rows").EnumerateArray())
            {
                // Packet21/current WorldItemSnapshot owns no incoming Favorite field. These
                // ten configured favorite-world-item rows remain pure GetItem references;
                // no integration assertion or numeric expected tuple is rewritten.
                if (row.GetProperty("beforeIncoming").GetProperty("favorite").GetBoolean())
                { Assert.Equal("favorite_empty", row.GetProperty("profile").GetString()); Assert.True(row.GetProperty("beforeIncoming").GetProperty("favorite").GetBoolean()); omittedFavoriteProfiles++; continue; }
                using var f = new Fixture(platform == "windowsClr4X86"); short slot = 0;
                foreach (var before in row.GetProperty("before").EnumerateArray()) f.Set(slot++, (short)I(before, "type"), (short)I(before, "stack"), (byte)I(before, "prefix"), before.GetProperty("favorite").GetBoolean() ? (byte)1 : (byte)0);
                var incoming = row.GetProperty("beforeIncoming"); var item = f.Drop((short)I(incoming, "type"), (short)I(incoming, "stack"));
                f.State.Tick(); slot = 0;
                foreach (var expected in row.GetProperty("after").EnumerateArray())
                { Assert.True(f.Players.TryGetInventoryItem(f.Connection, slot++, out var actual)); Assert.Equal(I(expected, "type"), actual.ItemType.Value); Assert.Equal(I(expected, "stack"), (int)actual.Stack); Assert.Equal(I(expected, "prefix"), actual.Prefix.Value); Assert.Equal(expected.GetProperty("favorite").GetBoolean(), (actual.ItemFlags & 1) != 0); }
                int remaining = I(row.GetProperty("residual"), "stack"); Assert.Equal(remaining > 0, f.Items.TryGetActive(item.Handle.Slot, out var after)); if (remaining > 0) Assert.Equal(remaining, (int)after.Stack);
                storageComparisons++;
            }
        Assert.Equal(50, storageComparisons); Assert.Equal(10, omittedFavoriteProfiles);
        using var diagonalStream = typeof(OwnedHumanPickupAuthority1458Tests).Assembly.GetManifestResourceStream("OwnedPickupShimmer1458");
        Assert.NotNull(diagonalStream); using var diagonalGzip = new GZipStream(diagonalStream!, CompressionMode.Decompress); using var diagonal = JsonDocument.Parse(diagonalGzip);
        foreach (bool windows in new[] { false, true })
        {
            using var f = new Fixture(windows); var item = f.Drop(109, 20, shimmered: true, vx: 0.12f, vy: 0.16f);
            f.Runtime.WorldItems.TickOwnedHumanPickups();
            int remaining = diagonal.RootElement.GetProperty(windows ? "windows" : "linux").GetProperty("residualStack").GetInt32();
            Assert.Equal(remaining > 0, f.Items.TryGetActive(item.Handle.Slot, out var after)); if (remaining > 0) Assert.Equal(remaining, (int)after.Stack);
            Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out var owned)); Assert.Equal(20 - remaining, (int)owned.Stack);
        }
    }

    [Fact]
    public async Task Actual_loop_and_world_commands_adopt_partial_full_and_caps_before_observers()
    {
        foreach (bool commandFirst in new[] { false, true })
            foreach (bool partial in new[] { false, true })
            {
                using var f = new Fixture();
                if (partial) { for (short slot = 0; slot < 50; slot++) f.Set(slot, 3, 9999); f.Set(0, 110, 9990); }
                var drop = f.Drop(110, 20);
                int after = partial ? 9999 : 20; short destination = 0;
                bool saw = false;
                f.WorldEvents.Callback = () => { saw = true; Assert.True(f.Players.TryGetInventoryItem(f.Connection, destination, out var item)); Assert.Equal(after, (int)item.Stack); Assert.Equal(partial, f.Items.TryGetActive(drop.Handle.Slot, out var residual)); if (partial) Assert.Equal(11, (int)residual.Stack); };
                if (commandFirst)
                {
                    if (partial) f.State.Apply(new WorldItemDropRuntimeCommand(f.Connection, drop.Handle, new(drop.PositionX, drop.PositionY, drop.VelocityX, drop.VelocityY, 11, drop.Prefix, drop.Ownership, drop.ItemNetId, drop.Shimmered, drop.ShimmerTime, drop.EnemyGrabDelayTime)));
                    else f.State.Apply(new WorldItemRemoveRuntimeCommand(f.Connection, drop.Handle));
                }
                else f.State.Tick();
                Assert.True(saw, $"command={commandFirst} partial={partial} phase={f.Member.ItemPhase} position={f.Member.PositionX},{f.Member.PositionY} ground={f.Items.TryGetActive(drop.Handle.Slot, out var diagnostic)}:{diagnostic}"); Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out var received)); Assert.Equal(after, (int)received.Stack);
                f.Set(0, 110, (short)after); // Exact source sync5 acknowledgement, no second grant.
                Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out received)); Assert.Equal(after, (int)received.Stack);
                f.Set(0, 110, (short)(after + 1)); Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out received)); Assert.Equal(after, (int)received.Stack);
            }
        using (var f = new Fixture())
        {
            var item = f.Drop(40, 20); f.State.Apply(new WorldItemRemoveRuntimeCommand(f.Connection, item.Handle));
            Assert.False(f.Items.TryGetActive(item.Handle.Slot, out _)); Assert.True(f.Players.TryGetInventoryItem(f.Connection, 54, out var arrow)); Assert.Equal(40, arrow.ItemType.Value); Assert.Equal(20, (int)arrow.Stack);
        }
        using (var f = new Fixture())
        {
            var item = f.Drop(24, 1, prefix: 18); f.State.Apply(new WorldItemRemoveRuntimeCommand(f.Connection, item.Handle));
            Assert.False(f.Items.TryGetActive(item.Handle.Slot, out _)); Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out var sword)); Assert.Equal(24, sword.ItemType.Value); Assert.Equal(18, sword.Prefix.Value);
        }
        using (var f = new Fixture())
        {
            f.Set(0, 110, 3);
            Assert.True(f.Runtime.Players.TryGetInventoryItem(f.Connection, 0, out var initial));
            var inventory = (RuntimePlayerInventoryStore)typeof(PlayerAuthority).GetField("inventory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Players)!;
            Assert.True(inventory.TryPrepareRemotePhase(new RemotePhaseInventoryMutation[] { new(f.Connection, 0, initial, initial with { Stack = 2 }) }, inventory.Serial, out var consume));
            Assert.True(consume!.TryAdoptUnpublished());
            var drop = f.Drop(110, 4); f.State.Apply(new WorldItemRemoveRuntimeCommand(f.Connection, drop.Handle));
            Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out var received)); Assert.Equal(6, (int)received.Stack);
            f.Set(0, 0, 0); f.Set(0, 110, 7); Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out received)); Assert.True(received.IsEmpty);
            f.Set(0, 110, 6); Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out received)); Assert.Equal(6, (int)received.Stack);
            Assert.True(inventory.TryPrepareRemotePhase(new RemotePhaseInventoryMutation[] { new(f.Connection, 0, received, received with { Stack = 5 }) }, inventory.Serial, out var secondConsume)); Assert.True(secondConsume!.TryAdoptUnpublished());
            var detach = new TaskCompletionSource<RuntimePlayerTransferState?>(); f.State.Apply(new PlayerTransferDetachRuntimeCommand(f.Connection, detach)); var transfer = await detach.Task; Assert.NotNull(transfer);
            f.Connection = new(GameCommandSourceId.FromConnection(98902), new(f.Connection.Player.Slot, new(f.Connection.Player.Generation.Value + 1)));
            var attach = new TaskCompletionSource<bool>(); f.State.Apply(new PlayerTransferAttachRuntimeCommand(f.Connection, transfer!, 100, 103, true, false, attach)); Assert.True(await attach.Task);
            f.Set(0, 110, 6); Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out received)); Assert.Equal(5, (int)received.Stack);
        }
    }

    [Fact]
    public async Task Observer_throw_reentry_and_wrong_contact_do_not_publish_stale_inventory()
    {
        VerifyTickContactBeforeItemDelayAndOwnerDiscovery();
        VerifyBindingAndRetiredConstructorBoundary();
        using (var f = new Fixture())
        {
            for (short slot = 0; slot < 50; slot++) f.Set(slot, 3, 9999);
            f.Set(0, 110, 9990); f.Set(1, 110, 9990);
            var item = f.Drop(110, 20); f.Events.Seen.Clear();
            f.WorldEvents.Callback = () => { Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out var a)); Assert.Equal(9999, (int)a.Stack); Assert.True(f.Players.TryGetInventoryItem(f.Connection, 1, out var b)); Assert.Equal(9999, (int)b.Stack); throw new InvalidOperationException("first world observer"); };
            var failure = Assert.Throws<InvalidOperationException>(() => f.State.Apply(new WorldItemDropRuntimeCommand(f.Connection, item.Handle, new(item.PositionX, item.PositionY, 0, 0, 2, 0, item.Ownership, item.ItemNetId, false, 0, 0))));
            Assert.Equal("first world observer", failure.Message); Assert.Equal(new short[] { 0, 1 }, f.Events.Seen);
        }
        using (var f = new Fixture())
        {
            var item = f.Drop(110, 20); f.Events.Seen.Clear();
            f.WorldEvents.Callback = () => { f.WorldEvents.Callback = null; f.Set(0, 110, 19); f.State.Apply(new WorldItemRemoveRuntimeCommand(f.Connection, item.Handle)); };
            f.State.Apply(new WorldItemRemoveRuntimeCommand(f.Connection, item.Handle));
            Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out var after)); Assert.Equal(19, (int)after.Stack); Assert.Equal(new short[] { 0 }, f.Events.Seen);
        }
        using (var f = new Fixture())
        {
            var item = f.Drop(110, 20, x: 1620); f.State.Apply(new WorldItemRemoveRuntimeCommand(f.Connection, item.Handle)); Assert.True(f.Items.TryGetActive(item.Handle.Slot, out _)); Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out var empty)); Assert.True(empty.IsEmpty);
        }
        using (var f = new Fixture())
        {
            f.Set(0, 110, 3);
            var inventory = (RuntimePlayerInventoryStore)typeof(PlayerAuthority).GetField("inventory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Players)!;
            Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out var before));
            Assert.True(inventory.TryPrepareRemotePhase(new RemotePhaseInventoryMutation[] { new(f.Connection, 0, before, before with { Stack = 2 }) }, inventory.Serial, out var consumed)); Assert.True(consumed!.TryAdoptUnpublished());
            // Existing trusted generic setter is not permission to launder an unexplained
            // image above the consumed cap through a later tiny ground grant.
            Assert.True(inventory.TryApplyAtomic(f.Connection, new RuntimePlayerInventoryMutation[] { new(0, before) }));
            var item = f.Drop(110, 1); var serial = inventory.Serial; f.State.Apply(new WorldItemRemoveRuntimeCommand(f.Connection, item.Handle));
            Assert.True(f.Items.TryGetActive(item.Handle.Slot, out var residual)); Assert.Equal(1, (int)residual.Stack); Assert.Equal(serial, inventory.Serial);
            Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out var retained)); Assert.Equal(before, retained);
        }
        using (var f = new Fixture())
        {
            f.State.Tick(); var emptyPhase = f.Member.ItemPhase; Assert.NotNull(emptyPhase);
            var item = f.Drop(113, 1); f.State.Apply(new WorldItemRemoveRuntimeCommand(f.Connection, item.Handle));
            Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out var held)); Assert.Equal(113, held.ItemType.Value);
            f.State.Apply(new PlayerMovementRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, 96, 16, 0, 64, 0, 1600, 1606, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
            f.State.Apply(new ClientProjectileUpdateRuntimeCommand(f.Connection, new TerrariaProjectileUpdateState(new(0, 991, 1), 16, 1594, 1611, 6, 0, 0, 0, 0, 0, 35, 7.5f, 0)));
            Assert.Equal(0, f.Projectiles.ActiveCount);
            f.State.Tick(); Assert.NotNull(f.Member.ItemPhase);
        }
        using (var f = new Fixture())
        {
            var old = f.Drop(110, 1); Assert.True(f.Items.TryRemove(old.Handle.Slot, out _)); var replacement = f.Drop(110, 2);
            Assert.Equal(old.Handle.Slot, replacement.Handle.Slot); Assert.NotEqual(old.Handle, replacement.Handle);
            f.State.Apply(new WorldItemRemoveRuntimeCommand(f.Connection, old.Handle));
            Assert.True(f.Items.TryGetActive(replacement.Handle.Slot, out var retained)); Assert.Equal(replacement, retained);
            Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out var empty)); Assert.True(empty.IsEmpty);
        }
        using (var f = new Fixture())
        {
            var one = f.Drop(110, 1); var two = f.Drop(109, 1); int observed = 0; bool reentered = false;
            f.WorldEvents.Callback = () => { observed++; if (!reentered) { reentered = true; f.State.Tick(); } };
            f.Runtime.WorldItems.TickOwnedHumanPickups(); Assert.Equal(1, observed);
            Assert.False(f.Items.TryGetActive(one.Handle.Slot, out _)); Assert.True(f.Items.TryGetActive(two.Handle.Slot, out _));
            // Reentrant Tick changed the second source reservation snapshot; defer it to a fresh scan.
            f.Runtime.WorldItems.TickOwnedHumanPickups(); Assert.Equal(2, observed); Assert.False(f.Items.TryGetActive(two.Handle.Slot, out _));
            Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out var first)); Assert.Equal(110, first.ItemType.Value);
            Assert.True(f.Players.TryGetInventoryItem(f.Connection, 1, out var second)); Assert.Equal(109, second.ItemType.Value);
        }
        using (var f = new Fixture())
        {
            f.Set(0, 110, 1);
            var inventory = (RuntimePlayerInventoryStore)typeof(PlayerAuthority).GetField("inventory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Players)!;
            Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out var before));
            Assert.True(inventory.TryPrepareRemotePhase(new RemotePhaseInventoryMutation[] { new(f.Connection, 0, before, default) }, inventory.Serial, out var consumed)); Assert.True(consumed!.TryAdoptUnpublished());
            var completion = new TaskCompletionSource<WorldItemSnapshot?>();
            f.State.Apply(new WorldItemAllocateRuntimeCommand(f.Connection, new(1600, 1606, 0, 0, 1, 0, WorldItemOwnershipMode.ReserveForLocalPlayer, 71, false, 0, 0), completion));
            Assert.True(completion.Task.IsCompletedSuccessfully); var forgedCoin = await completion.Task; Assert.NotNull(forgedCoin);
            f.State.Apply(new WorldItemDropRuntimeCommand(f.Connection, forgedCoin!.Value.Handle, new(1600, 1606, 0, 0, 1, 0, WorldItemOwnershipMode.None, 110, false, 0, 0)));
            f.State.Tick();
            Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out var retained));
            Assert.True(retained.IsEmpty, "Unfunded client slot400 must not reopen consumed quantity through an owned pickup grant.");
        }
        using (var f = new Fixture())
        {
            var item = f.Drop(110, 3);
            Assert.True(f.Items.TryApplyDrop(item.Handle.Slot, new(item.PositionX, item.PositionY, 0, 0, 30, 0, WorldItemOwnershipMode.None, 110, false, 0, 0), out var inflated));
            Assert.True(f.Items.TryGetFundedQuantity(in inflated, out var funded)); Assert.Equal(3, funded);
            f.Runtime.WorldItems.TickOwnedHumanPickups();
            Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out var granted)); Assert.Equal(3, (int)granted.Stack);
            Assert.True(f.Items.TryGetActive(item.Handle.Slot, out var residue)); Assert.Equal(27, (int)residue.Stack);
            Assert.True(f.Items.TryGetFundedQuantity(in residue, out funded)); Assert.Equal(0, funded);
            f.Runtime.WorldItems.TickOwnedHumanPickups(); f.Set(0, 110, 4);
            Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out granted)); Assert.Equal(3, (int)granted.Stack);
        }
        using (var f = new Fixture())
        {
            var item = f.Drop(110, 3);
            Assert.True(f.Items.TryApplyDrop(item.Handle.Slot, new(item.PositionX, item.PositionY, 0, 0, 1, 0, WorldItemOwnershipMode.None, 110, false, 0, 0), out var reduced));
            Assert.True(f.Items.TryGetFundedQuantity(in reduced, out var funded)); Assert.Equal(1, funded);
            Assert.True(f.Items.TryApplyDrop(item.Handle.Slot, new(item.PositionX, item.PositionY, 0, 0, 3, 0, WorldItemOwnershipMode.None, 110, false, 0, 0), out var restored));
            Assert.True(f.Items.TryGetFundedQuantity(in restored, out funded)); Assert.Equal(1, funded);
            Assert.True(f.Items.TryApplyDrop(item.Handle.Slot, new(item.PositionX, item.PositionY, 0, 0, 3, 0, WorldItemOwnershipMode.None, 189, false, 0, 0), out _));
            Assert.True(f.Items.TryApplyDrop(item.Handle.Slot, new(item.PositionX, item.PositionY, 0, 0, 3, 0, WorldItemOwnershipMode.None, 110, false, 0, 0), out restored));
            Assert.True(f.Items.TryGetFundedQuantity(in restored, out funded)); Assert.Equal(0, funded);
            Assert.True(f.Items.TryRemove(item.Handle.Slot, out _));
            Assert.True(f.Items.TryAllocate(new WorldItemStateUpdate(1600, 1606, 0, 0, 3, 0, WorldItemOwnershipMode.None, 110, false, 0, 0, 0, 0, 1, 0), out var reused));
            Assert.Equal(item.Handle.Slot, reused.Handle.Slot); Assert.NotEqual(item.Handle, reused.Handle);
            Assert.True(f.Items.TryGetFundedQuantity(in reused, out funded)); Assert.Equal(0, funded);
        }
        {
            var store = new RuntimeWorldItemStore();
            Assert.True(store.TryReserveDrop(new WorldItemDropStateUpdate(1000, 1000, 0, 0, 3, 0, WorldItemOwnershipMode.None, 110, false, 0, 0), out var lease));
            Assert.True(store.TryCommitReservedDrop(lease, out var trusted)); Assert.True(store.TryGetFundedQuantity(in trusted, out var funded)); Assert.Equal(3, funded);
            Assert.True(store.TryUpsert(1, new WorldItemStateUpdate(1000, 1000, 0, 0, 7, 0, WorldItemOwnershipMode.None, 110, false, 0, 0, 255, 0, 255, 0), out _));
            for (short slot = 2; slot < 400; slot++) Assert.True(store.TryUpsert(slot, new WorldItemStateUpdate(5000, 1000, 0, 0, 9999, 0, WorldItemOwnershipMode.None, 1, false, 0, 0, 255, 0, 255, 0), out _));
            Assert.True(store.TryAllocateSourceDrop(new WorldItemDropStateUpdate(5000, 1000, 0, 0, 1, 0, WorldItemOwnershipMode.None, 38, false, 0, 0), default, out _, out _));
            var image = new WorldItemSnapshot[400]; int count = store.CopyActive(image); var mixed = Assert.Single(image.Take(count), i => i.ItemNetId == 110);
            Assert.Equal(10, (int)mixed.Stack); Assert.True(store.TryGetFundedQuantity(in mixed, out funded)); Assert.Equal(3, funded);
            Assert.True(store.TryApplyOwner(mixed.Handle.Slot, new(0, 15, 1, 0, 1600, 1606), out mixed));
            using (var receiver = new Fixture(providedItems: store))
            {
                receiver.State.Tick(); Assert.True(receiver.Players.TryGetInventoryItem(receiver.Connection, 0, out var granted)); Assert.Equal(3, (int)granted.Stack);
                Assert.True(store.TryGetActive(mixed.Handle.Slot, out var residue)); Assert.Equal(7, (int)residue.Stack); Assert.True(store.TryGetFundedQuantity(in residue, out funded)); Assert.Equal(0, funded);
                receiver.State.Tick(); receiver.Set(0, 110, 4); Assert.True(receiver.Players.TryGetInventoryItem(receiver.Connection, 0, out granted)); Assert.Equal(3, (int)granted.Stack);
            }
        }
        {
            var store = new RuntimeWorldItemStore();
            Assert.True(store.TryAllocateSourceDrop(new WorldItemDropStateUpdate(1000, 1000, 0, 0, 3, 0, WorldItemOwnershipMode.None, 110, false, 0, 0), default, out var mixed, out _));
            Assert.True(store.TryApplyDrop(mixed.Handle.Slot, new(1000, 1000, 0, 0, 10, 0, WorldItemOwnershipMode.None, 110, false, 0, 0), out mixed));
            int funded;
            using var take = store.CreateAllocationPreview(); Assert.True(take.TryTakeSource(in mixed, 2, requireFundedQuantity: true)); Assert.True(take.TryClaim()); Assert.True(take.TryAdoptUnpublished(out _));
            Assert.True(store.TryGetActive(mixed.Handle.Slot, out var remainder)); Assert.Equal(8, (int)remainder.Stack); Assert.True(store.TryGetFundedQuantity(in remainder, out funded)); Assert.Equal(1, funded);
        }
        using (var f = new Fixture())
        {
            var completion = new TaskCompletionSource<WorldItemSnapshot?>();
            f.State.Apply(new WorldItemAllocateRuntimeCommand(f.Connection, new(1600, 1606, 0, 0, 2, 0, WorldItemOwnershipMode.ReserveForLocalPlayer, 23, false, 0, 0), completion));
            Assert.True(completion.Task.IsCompletedSuccessfully); Assert.NotNull(await completion.Task);
            f.State.Tick(); Assert.All(f.ItemImage(), item => Assert.True(item.IsEmpty));
        }
        foreach (bool fullyTrusted in new[] { false, true })
        {
            var store = new RuntimeWorldItemStore();
            Assert.True(store.TryReserveDrop(new WorldItemDropStateUpdate(1000, 1000, 0, 0, 99, 0, WorldItemOwnershipMode.None, 71, false, 0, 0), out var first)); Assert.True(store.TryCommitReservedDrop(first, out _));
            if (fullyTrusted) { Assert.True(store.TryReserveDrop(new WorldItemDropStateUpdate(1000, 1000, 0, 0, 1, 0, WorldItemOwnershipMode.None, 71, false, 0, 0), out var second)); Assert.True(store.TryCommitReservedDrop(second, out _)); }
            else Assert.True(store.TryUpsert(1, new WorldItemStateUpdate(1000, 1000, 0, 0, 1, 0, WorldItemOwnershipMode.None, 71, false, 0, 0, 255, 0, 255, 0), out _));
            for (short slot = 2; slot < 400; slot++) Assert.True(store.TryUpsert(slot, new WorldItemStateUpdate(5000, 1000, 0, 0, 9999, 0, WorldItemOwnershipMode.None, 1, false, 0, 0, 255, 0, 255, 0), out _));
            Assert.True(store.TryAllocateSourceDrop(new WorldItemDropStateUpdate(5000, 1000, 0, 0, 1, 0, WorldItemOwnershipMode.None, 38, false, 0, 0), default, out _, out _));
            var image = new WorldItemSnapshot[400]; int count = store.CopyActive(image); var coin = Assert.Single(image.Take(count), i => i.ItemNetId == 72);
            Assert.Equal(1, (int)coin.Stack); Assert.True(store.TryGetFundedQuantity(in coin, out var funded)); Assert.Equal(fullyTrusted ? 1 : 0, funded);
        }
        {
            var sink = new WorldEvents(); var store = new RuntimeWorldItemStore(sink); int observed = 0;
            sink.Callback = () => { observed++; Assert.True(store.TryGetActive(0, out var item)); Assert.True(store.TryGetFundedQuantity(in item, out var funded)); Assert.Equal(3, funded); };
            Assert.True(store.TryAllocateSourceDrop(new WorldItemDropStateUpdate(1000, 1000, 0, 0, 3, 0, WorldItemOwnershipMode.None, 110, false, 0, 0), default, out _, out _)); Assert.Equal(1, observed);
        }
        using (var f = new Fixture())
        {
            // Source Player.Update returns before GrabItems outside the known world.
            f.Member.PositionX = 7000; // Trusted configured retained outside body; ingress rejects this teleport.
            Assert.Equal(7000f, f.Member.PositionX); var item = f.Drop(109, 1, 7000); f.Runtime.WorldItems.TickOwnedHumanPickups();
            Assert.True(f.Items.TryGetActive(item.Handle.Slot, out var retained)); Assert.Equal(item, retained);
            Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out var empty)); Assert.True(empty.IsEmpty);
        }
        using (var f = new Fixture())
        {
            var store = new RuntimeWorldItemStore();
            Assert.True(store.TryAllocateSourceDrop(new WorldItemDropStateUpdate(1600, 1606, 0, 0, 20, 0, WorldItemOwnershipMode.None, 110, false, 0, 0), default, out var item, out _));
            Assert.True(store.TryApplyOwner(item.Handle.Slot, new(0, 15, 1, 0, 1600, 1606), out item));
            store.AttachOwnerFactsProvider(new LateCurrentnessProvider(() => f.Set(2, 9, 1)));
            var authority = new WorldItemAuthority(f.Players, store, new SystemWorldItemSpawnRandom(0), null);
            var failure = Record.Exception(() => { authority.TryApply(new WorldItemRemoveRuntimeCommand(f.Connection, item.Handle)); });
            Assert.True(store.TryGetActive(item.Handle.Slot, out var retained)); Assert.Equal(item, retained); Assert.Null(failure);
            Assert.True(store.TryGetFundedQuantity(in retained, out var funded)); Assert.Equal(20, funded);
            Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out var empty)); Assert.True(empty.IsEmpty);
            Assert.True(f.Players.TryGetInventoryItem(f.Connection, 2, out var newer)); Assert.Equal(9, newer.ItemType.Value);
        }
        using (var f = new Fixture())
        {
            var store = new RuntimeWorldItemStore(); var authority = new WorldItemAuthority(f.Players, store, new SystemWorldItemSpawnRandom(0), null);
            store.AttachOwnerFactsProvider(new MutatingProvider(() => f.Set(2, 9, 1)));
            Assert.True(store.TryAllocateSourceDrop(new WorldItemDropStateUpdate(1600, 1606, 0, 0, 20, 0, WorldItemOwnershipMode.None, 110, false, 0, 0), default, out var item, out _));
            Assert.True(store.TryApplyOwner(item.Handle.Slot, new(0, 15, 1, 0, 1600, 1606), out item));
            authority.TryApply(new WorldItemRemoveRuntimeCommand(f.Connection, item.Handle));
            Assert.True(store.TryGetActive(item.Handle.Slot, out var residual)); Assert.Equal(item, residual);
            Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out var empty)); Assert.True(empty.IsEmpty);
            Assert.True(f.Players.TryGetInventoryItem(f.Connection, 2, out var newer)); Assert.Equal(9, newer.ItemType.Value);
        }
    }

    private static int I(JsonElement e, string n) => e.GetProperty(n).GetInt32();
    private static float F(JsonElement e, string n) => e.GetProperty(n).GetSingle();
    private static void VerifyTickContactBeforeItemDelayAndOwnerDiscovery()
    {
        foreach (byte grab in new byte[] { 0, 255 })
        {
            using var f = new Fixture();
            var item = f.Drop(110, 1, delay: 1, delayPlayer: grab);
            f.State.Tick();
            Assert.True(f.Items.TryGetActive(item.Handle.Slot, out var retained)); Assert.Equal(0, retained.GrabDelayTime);
            Assert.True(f.ItemImage()[0].IsEmpty);
            f.State.Tick();
            Assert.False(f.Items.TryGetActive(item.Handle.Slot, out _)); Assert.Equal(1, f.ItemImage()[0].Stack);
        }
        using (var f = new Fixture())
        {
            var item = f.Drop(110, 1, owner: 255);
            Assert.True(f.Items.TryApplyOwner(item.Handle.Slot, new(255, 0, 1, 0, 1600, 1606), out _));
            f.State.Tick();
            Assert.True(f.ItemImage()[0].IsEmpty); Assert.True(f.Items.TryGetActive(item.Handle.Slot, out var reserved)); Assert.Equal(0, reserved.OwnerPlayerId);
            f.State.Tick();
            Assert.Equal(1, f.ItemImage()[0].Stack); Assert.False(f.Items.TryGetActive(item.Handle.Slot, out _));
        }
    }

    private static void VerifyBindingAndRetiredConstructorBoundary()
    {
        using (var f = new Fixture(bindPhase: false))
        {
            var item = f.Drop(110, 1);
            f.State.Tick();
            Assert.True(f.Items.TryGetActive(item.Handle.Slot, out _));
            Assert.True(f.ItemImage()[0].IsEmpty);
            f.State.Apply(new WorldItemRemoveRuntimeCommand(f.Connection, item.Handle));
            Assert.False(f.Items.TryGetActive(item.Handle.Slot, out _));
            Assert.True(f.ItemImage()[0].IsEmpty);
        }
        using (var f = new Fixture())
        {
            f.Set(0, 758, 1); // Unsupported selected launch family retires the shared source cursor.
            f.State.Tick();
            Assert.True(f.Players.HasRemotePlayerPhaseBinding);
            Assert.False(f.Players.HasOwnedPickupContext);
            f.Reconnect();
            Assert.NotNull(f.Member.ItemPhase);
            var item = f.Drop(110, 1);
            f.State.Apply(new WorldItemRemoveRuntimeCommand(f.Connection, item.Handle));
            Assert.True(f.Items.TryGetActive(item.Handle.Slot, out _));
            Assert.True(f.ItemImage()[0].IsEmpty);
        }
    }

    [Fact]
    public async Task Funded_pickup_drop_and_report_images_preserve_quantity_custody()
    {
        await VerifyOriginalSelectedFundedDrops();
        using var f = new Fixture();
        var inventory = (RuntimePlayerInventoryStore)typeof(PlayerAuthority).GetField("inventory", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Players)!;
        f.Drop(110, 3); f.State.Tick();
        Assert.True(inventory.TryGetFundedQuantity(f.Connection, 0, out var funded)); Assert.Equal(3, funded);
        var beforeDropRandom = ((SystemWorldItemSpawnRandom)f.Runtime.WorldItems.SpawnRandom).SourceRandom.Clone();
        var proposed = new WorldItemDropStateUpdate(1602, 1619, 4, -2, 3, 0, WorldItemOwnershipMode.GrabDelayForLocalPlayer, 110, false, 0, 100);
        int observed = 0;
        f.WorldEvents.Callback = () =>
        {
            observed++;
            Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out var empty)); Assert.True(empty.IsEmpty);
            Assert.True(inventory.TryGetFundedQuantity(f.Connection, 0, out var spent)); Assert.Equal(0, spent);
            var image = new WorldItemSnapshot[400]; int count = f.Items.CopyActive(image);
            var ground = Assert.Single(image.Take(count));
            Assert.True(f.Items.TryGetFundedQuantity(in ground, out var quantity)); Assert.Equal(3, quantity);
        };
        var completion = new TaskCompletionSource<WorldItemSnapshot?>();
        f.State.Apply(new WorldItemAllocateRuntimeCommand(f.Connection, proposed, completion));
        Assert.NotNull(await completion.Task); Assert.True(observed > 0); Assert.Equal(10, f.Member.ItemAnimation);
        Assert.True(((SystemWorldItemSpawnRandom)f.Runtime.WorldItems.SpawnRandom).SourceRandom.HasSameState(beforeDropRandom));
        f.WorldEvents.Callback = null;
        f.Set(0, 0, 0); // Exact later source5 is an acknowledgement, not another debit.
        Assert.True(inventory.TryGetFundedQuantity(f.Connection, 0, out funded)); Assert.Equal(0, funded);
        f.Set(0, 110, 3); // Reported restore cannot reconstruct funding or reopen the consumed/drop ceiling.
        Assert.True(f.ItemImage()[0].IsEmpty);
        Assert.True(inventory.TryGetFundedQuantity(f.Connection, 0, out funded)); Assert.Equal(0, funded);

        var coinImage = new RuntimePlayerInventoryItem[59]; coinImage[0] = new(new(71), 1, new(0), 0);
        var coinFunding = new short[59]; coinFunding[0] = 1;
        Assert.False(RuntimePlayerInventoryStore.AreValidFundedQuantities(coinFunding, coinImage)); // Physical coin count cannot represent funded value.

        using var mixed = new Fixture();
        mixed.Set(0, 110, 7); mixed.Drop(110, 3); mixed.State.Tick();
        var mixedInventory = (RuntimePlayerInventoryStore)typeof(PlayerAuthority).GetField("inventory", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(mixed.Players)!;
        Assert.True(mixedInventory.TryGetFundedQuantity(mixed.Connection, 0, out funded)); Assert.Equal(3, funded);
        Assert.True(mixed.Players.TryGetInventoryItem(mixed.Connection, 0, out var ten)); Assert.Equal(10, ten.Stack);
        var refused = new TaskCompletionSource<WorldItemSnapshot?>();
        mixed.State.Apply(new WorldItemAllocateRuntimeCommand(mixed.Connection, proposed with { Stack = 10 }, refused));
        Assert.Null(await refused.Task); Assert.Equal(ten, mixed.ItemImage()[0]);
        Assert.Equal(0, mixed.Items.ActiveCount);
        mixed.Set(0, 110, 1); mixed.Set(0, 110, 10);
        Assert.True(mixedInventory.TryGetFundedQuantity(mixed.Connection, 0, out funded)); Assert.Equal(1, funded);
        mixed.Set(0, 9, 10); mixed.Set(0, 110, 10);
        Assert.True(mixedInventory.TryGetFundedQuantity(mixed.Connection, 0, out funded)); Assert.Equal(0, funded);
    }

    [Fact]
    public async Task Funded_drop_transfer_provider_and_observer_guards_preserve_joint_owners()
    {
        using (var f = new Fixture())
        {
            f.Drop(110, 3); f.State.Tick();
            var detach = new TaskCompletionSource<RuntimePlayerTransferState?>();
            f.State.Apply(new PlayerTransferDetachRuntimeCommand(f.Connection, detach)); var transfer = await detach.Task; Assert.NotNull(transfer);
            Assert.Equal(3, transfer!.FundedInventoryQuantities[0]);
            f.Connection = new(GameCommandSourceId.FromConnection(98902), new(f.Connection.Player.Slot, new(f.Connection.Player.Generation.Value + 1)));
            var attach = new TaskCompletionSource<bool>(); f.State.Apply(new PlayerTransferAttachRuntimeCommand(f.Connection, transfer, 100, 103, true, false, attach)); Assert.True(await attach.Task);
            var inventory = (RuntimePlayerInventoryStore)typeof(PlayerAuthority).GetField("inventory", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Players)!;
            Assert.True(inventory.TryGetFundedQuantity(f.Connection, 0, out var funded)); Assert.Equal(3, funded);
            var before = f.Member.CaptureSnapshot();
            var body = new WorldItemDropStateUpdate(before.PositionX + 2, before.PositionY + 13, 4, -2, 3, 0, WorldItemOwnershipMode.GrabDelayForLocalPlayer, 110, false, 0, 100);
            var completion = new TaskCompletionSource<WorldItemSnapshot?>(); f.State.Apply(new WorldItemAllocateRuntimeCommand(f.Connection, body, completion)); Assert.NotNull(await completion.Task);
        }
        using (var f = new Fixture())
        {
            f.Drop(110, 3); f.State.Tick(); f.Events.Seen.Clear();
            f.WorldEvents.Callback = () => throw new InvalidOperationException("drop-observer");
            var body = new WorldItemDropStateUpdate(1602, 1619, 4, -2, 3, 0, WorldItemOwnershipMode.GrabDelayForLocalPlayer, 110, false, 0, 100);
            var failure = Record.Exception(() => f.State.Apply(new WorldItemAllocateRuntimeCommand(f.Connection, body)));
            Assert.IsType<InvalidOperationException>(failure); Assert.Equal("drop-observer", failure!.Message);
            Assert.True(f.ItemImage()[0].IsEmpty); Assert.Contains((short)0, f.Events.Seen); Assert.Equal(1, f.Items.ActiveCount);
        }
        using (var f = new Fixture())
        {
            f.Drop(110, 3); f.State.Tick();
            var store = new RuntimeWorldItemStore();
            store.AttachOwnerFactsProvider(new LateCurrentnessProvider(() => f.Set(2, 9, 1)));
            var authority = new WorldItemAuthority(f.Players, store, new SystemWorldItemSpawnRandom(0), null);
            var body = new WorldItemDropStateUpdate(1602, 1619, 4, -2, 3, 0, WorldItemOwnershipMode.GrabDelayForLocalPlayer, 110, false, 0, 100);
            var completion = new TaskCompletionSource<WorldItemSnapshot?>();
            authority.TryApply(new WorldItemAllocateRuntimeCommand(f.Connection, body, completion)); Assert.Null(await completion.Task);
            Assert.Equal(0, store.ActiveCount); Assert.Equal(3, f.ItemImage()[0].Stack); Assert.Equal(9, f.ItemImage()[2].ItemType.Value);
        }
    }

    private static async Task VerifyOriginalSelectedFundedDrops()
    {
        using var resource = typeof(OwnedHumanPickupAuthority1458Tests).Assembly.GetManifestResourceStream("SourceHumanDrop1458");
        Assert.NotNull(resource); using var gzip = new GZipStream(resource!, CompressionMode.Decompress); using var original = JsonDocument.Parse(gzip);
        int compared = 0;
        foreach (string platform in new[] { "linux", "windowsClr4X86" })
            foreach (var row in original.RootElement.GetProperty(platform).GetProperty("rows").EnumerateArray())
            {
                int id = row.GetProperty("id").GetInt32(); string profile = row.GetProperty("profile").GetString()!;
                // These six source items reach selected0 through the actual existing hotbar pickup owner.
                // Ammo/cosmetic cursor and favorite-return profiles require the later owned move/UI producer.
                if (id is not (2 or 3 or 24 or 109 or 110 or 219) || profile is not ("selected0" or "last1" or "leftmoving")) continue;
                using var f = new Fixture(); var ground = row.GetProperty("ground"); var item = ground.GetProperty("item");
                short stack = item.GetProperty("stack").GetInt16(); byte prefix = item.GetProperty("prefix").GetByte();
                f.Drop((short)id, stack, prefix: prefix); f.State.Tick();
                Assert.Equal(id, f.ItemImage()[0].ItemType.Value); Assert.Equal(stack, f.ItemImage()[0].Stack);
                if (profile == "leftmoving") { f.Member.ControlFlags &= 191; f.Member.VelocityX = 1.25f; }
                var position = ground.GetProperty("position"); var velocity = ground.GetProperty("velocity");
                var proposal = new WorldItemDropStateUpdate(position.GetProperty("x").GetSingle() + 1200,
                    position.GetProperty("y").GetSingle() + 1206, velocity.GetProperty("x").GetSingle(), velocity.GetProperty("y").GetSingle(),
                    stack, prefix, WorldItemOwnershipMode.GrabDelayForLocalPlayer, (short)id, false, 0, ground.GetProperty("enemyGrabDelay").GetByte());
                var completion = new TaskCompletionSource<WorldItemSnapshot?>(); f.State.Apply(new WorldItemAllocateRuntimeCommand(f.Connection, proposal, completion));
                var accepted = await completion.Task; Assert.NotNull(accepted); Assert.Equal(id, accepted!.Value.ItemNetId); Assert.Equal(stack, accepted.Value.Stack);
                Assert.True(f.ItemImage()[0].IsEmpty); Assert.Equal(row.GetProperty("afterClocks").GetProperty("animation").GetInt32(), f.Member.ItemAnimation);
                Assert.True(f.Items.TryGetFundedQuantity(accepted.Value, out int quantity)); Assert.Equal(stack, quantity); compared++;
            }
        Assert.Equal(36, compared);
    }

    [Fact]
    public async Task Paired_cursor_images_move_funding_and_consumed_caps_before_observers_and_transfer()
    {
        VerifyOriginalCursorImages();
        using var f = new Fixture();
        var inventory = (RuntimePlayerInventoryStore)typeof(PlayerAuthority).GetField("inventory", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Players)!;
        f.Drop(110, 3); f.State.Tick(); f.Events.Seen.Clear();
        f.Set(0, 0, 0); // First ascending source5: slot debit, cursor credit has not arrived.
        Assert.True(f.ItemImage()[0].IsEmpty); Assert.True(f.ItemImage()[58].IsEmpty);
        int observed = 0;
        f.Events.Callback = () =>
        {
            observed++;
            Assert.True(f.ItemImage()[0].IsEmpty); Assert.Equal(3, f.ItemImage()[58].Stack);
            Assert.True(inventory.TryGetFundedQuantity(f.Connection, 0, out var sourceFunding)); Assert.Equal(0, sourceFunding);
            Assert.True(inventory.TryGetFundedQuantity(f.Connection, 58, out var cursorFunding)); Assert.Equal(3, cursorFunding);
            Assert.Contains(inventory.CaptureOwnedQuantities(f.Connection), c => c.Slot == 58 && c.ItemType.Value == 110 && c.Maximum == 3);
        };
        f.Set(58, 110, 3); Assert.Equal(1, observed);
        f.Events.Callback = null;
        // Deposit is the opposite source order: destination credit precedes cursor debit.
        f.Set(2, 110, 3); Assert.True(f.ItemImage()[2].IsEmpty); Assert.Equal(3, f.ItemImage()[58].Stack);
        f.Set(58, 0, 0); Assert.Equal(3, f.ItemImage()[2].Stack); Assert.True(f.ItemImage()[58].IsEmpty);
        Assert.True(inventory.TryGetFundedQuantity(f.Connection, 2, out var deposited)); Assert.Equal(3, deposited);
        f.Set(2, 0, 0); f.Set(58, 110, 3);
        Assert.True(f.ItemImage()[2].IsEmpty); Assert.Equal(3, f.ItemImage()[58].Stack);
        var detach = new TaskCompletionSource<RuntimePlayerTransferState?>();
        f.State.Apply(new PlayerTransferDetachRuntimeCommand(f.Connection, detach)); var transfer = await detach.Task;
        Assert.NotNull(transfer); Assert.True(transfer!.MouseItemNormalized); Assert.Equal(3, transfer.FundedInventoryQuantities[0]);
        Assert.Equal(3, transfer.Inventory[0].Stack); Assert.True(transfer.Inventory[58].IsEmpty);
        Assert.Contains(transfer.OwnedInventoryQuantities, c => c.Slot == 0 && c.ItemType.Value == 110 && c.Maximum == 3);
        Assert.Contains(transfer.OwnedInventoryQuantities, c => c.Slot == 58 && c.ItemType.Value == 110 && c.Maximum == 0);
        f.Connection = new(GameCommandSourceId.FromConnection(98902), new(f.Connection.Player.Slot, new(f.Connection.Player.Generation.Value + 1)));
        var attach = new TaskCompletionSource<bool>();
        f.State.Apply(new PlayerTransferAttachRuntimeCommand(f.Connection, transfer, 100, 103, true, false, attach)); Assert.True(await attach.Task);
        Assert.Equal(3, f.ItemImage()[0].Stack); Assert.True(inventory.TryGetFundedQuantity(f.Connection, 0, out var received)); Assert.Equal(3, received);
        Assert.Contains(inventory.CaptureOwnedQuantities(f.Connection), c => c.Slot == 0 && c.ItemType.Value == 110 && c.Maximum == 3);
        using (var late = new Fixture())
        {
            late.Drop(110, 3); late.State.Tick();
            late.Events.Callback = () => late.State.Apply(new PlayerHealthRuntimeCommand(late.Connection, new(late.Connection.Player.Slot, 399, 400)));
            late.Set(0, 110, 2); // Eager component debit remains visible even if the eventual pair is refused.
            late.Events.Callback = null;
            late.Set(58, 110, 1);
            Assert.Equal(2, late.ItemImage()[0].Stack); Assert.True(late.ItemImage()[58].IsEmpty);
        }
        using (var throwing = new Fixture())
        {
            throwing.Drop(110, 3); throwing.State.Tick(); throwing.Set(0, 0, 0); throwing.Set(58, 110, 3);
            throwing.Events.Seen.Clear(); throwing.Set(2, 110, 3); // Credit first is deferred.
            throwing.Events.Callback = () =>
            {
                Assert.Equal(3, throwing.ItemImage()[2].Stack); Assert.True(throwing.ItemImage()[58].IsEmpty);
                throw new InvalidOperationException("owned cursor observer");
            };
            Assert.Throws<InvalidOperationException>(() => throwing.Set(58, 0, 0));
            Assert.Equal(new short[] { 2, 58 }, throwing.Events.Seen); // Drain the independent accepted cursor image.
            throwing.Events.Callback = null;
            throwing.Set(2, 110, 3); throwing.Set(58, 0, 0); // Duplicate acknowledgements do not mint funding.
            var store = (RuntimePlayerInventoryStore)typeof(PlayerAuthority).GetField("inventory", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(throwing.Players)!;
            Assert.True(store.TryGetFundedQuantity(throwing.Connection, 2, out var retained)); Assert.Equal(3, retained);
            throwing.Set(58, 110, 3); Assert.True(throwing.ItemImage()[58].IsEmpty); // Emptied source cap persists.
        }
        using (var splitTick = new Fixture())
        {
            splitTick.Drop(110, 3); splitTick.State.Tick(); splitTick.Set(0, 0, 0);
            splitTick.State.Tick(); splitTick.State.Tick(); splitTick.State.Tick(); splitTick.Set(58, 110, 3);
            var store = (RuntimePlayerInventoryStore)typeof(PlayerAuthority).GetField("inventory", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(splitTick.Players)!;
            Assert.True(store.TryGetFundedQuantity(splitTick.Connection, 58, out var quantity)); Assert.Equal(3, quantity);
        }
        using (var leftTime = new Fixture())
        {
            leftTime.Drop(110, 3); leftTime.State.Tick(); leftTime.Set(0, 110, 3, flags: 1);
            leftTime.Member.ItemPhase = leftTime.Member.ItemPhase!.Value with
            { Selected = leftTime.Member.ItemPhase.Value.Selected with { ItemTime = 1 } };
            leftTime.Set(0, 0, 0); leftTime.Set(58, 110, 3, flags: 1);
            var store = (RuntimePlayerInventoryStore)typeof(PlayerAuthority).GetField("inventory", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(leftTime.Players)!;
            Assert.True(store.TryGetFundedQuantity(leftTime.Connection, 58, out var quantity)); Assert.Equal(0, quantity);
        }
        foreach (short sourceType in new short[] { 110, 109 })
        {
            using var mixedExchange = new Fixture();
            mixedExchange.Set(0, sourceType, 9996); mixedExchange.Set(2, 110, 3);
            mixedExchange.Drop(sourceType, 3); mixedExchange.State.Tick();
            mixedExchange.Drop(110, 7); mixedExchange.State.Tick();
            mixedExchange.Set(2, 0, 0); mixedExchange.Set(58, 110, 10);
            mixedExchange.Set(0, sourceType, 9999, flags: 1);
            mixedExchange.Set(0, 110, 10, flags: sourceType == 110 ? (byte)1 : (byte)0);
            mixedExchange.Set(58, sourceType, 9999, flags: sourceType == 110 ? (byte)0 : (byte)1);
            var store = (RuntimePlayerInventoryStore)typeof(PlayerAuthority).GetField("inventory", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(mixedExchange.Players)!;
            Assert.Equal(10, mixedExchange.ItemImage()[0].Stack); Assert.Equal(9999, mixedExchange.ItemImage()[58].Stack);
            Assert.True(store.TryGetFundedQuantity(mixedExchange.Connection, 0, out var slotFunding)); Assert.Equal(7, slotFunding);
            Assert.True(store.TryGetFundedQuantity(mixedExchange.Connection, 58, out var cursorFunding)); Assert.Equal(3, cursorFunding);
            Assert.Equal(10, slotFunding + cursorFunding);
        }
        using (var unchangedFull = new Fixture())
        {
            unchangedFull.Set(0, 110, 9996); unchangedFull.Set(2, 110, 9992);
            unchangedFull.Drop(110, 3); unchangedFull.State.Tick();
            unchangedFull.Drop(110, 7); unchangedFull.State.Tick();
            unchangedFull.Set(2, 0, 0); unchangedFull.Set(58, 110, 9999);
            unchangedFull.Set(0, 110, 9999); unchangedFull.Set(58, 110, 9999);
            var store = (RuntimePlayerInventoryStore)typeof(PlayerAuthority).GetField("inventory", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(unchangedFull.Players)!;
            Assert.True(store.TryGetFundedQuantity(unchangedFull.Connection, 0, out var slotFunding)); Assert.Equal(3, slotFunding);
            Assert.True(store.TryGetFundedQuantity(unchangedFull.Connection, 58, out var cursorFunding)); Assert.Equal(7, cursorFunding);
        }
        using (var otherPlayer = new Fixture())
        {
            otherPlayer.Drop(110, 3); otherPlayer.State.Tick(); otherPlayer.Set(0, 0, 0);
            var other = new ConnectionHandle(GameCommandSourceId.FromConnection(98903), new(new PlayerSlotId(1), new(1)));
            otherPlayer.State.Apply(new PlayerEquipmentRuntimeCommand(other, new(other.Player.Slot, 0, 7, 0, 3, 0)));
            var store = (RuntimePlayerInventoryStore)typeof(PlayerAuthority).GetField("inventory", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(otherPlayer.Players)!;
            Assert.True(store.TryGet(other, 0, out var disjoint)); Assert.Equal(7, disjoint.Stack);
            otherPlayer.Set(58, 110, 3);
            Assert.True(store.TryGetFundedQuantity(otherPlayer.Connection, 58, out var quantity)); Assert.Equal(3, quantity);
        }
        using (var directWriter = new Fixture())
        {
            directWriter.Drop(110, 3); directWriter.State.Tick(); directWriter.Set(0, 0, 0);
            var store = (RuntimePlayerInventoryStore)typeof(PlayerAuthority).GetField("inventory", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(directWriter.Players)!;
            Assert.True(store.TrySet(directWriter.Connection, new(directWriter.Connection.Player.Slot, 2, 7, 0, 3, 0)));
            Assert.True(store.TrySet(directWriter.Connection, new(directWriter.Connection.Player.Slot, 2, 0, 0, 0, 0)));
            directWriter.Set(58, 110, 3);
            Assert.True(store.TryGetFundedQuantity(directWriter.Connection, 58, out var quantity)); Assert.Equal(0, quantity);
        }
        using (var retired = new Fixture())
        {
            retired.Drop(110, 3); retired.State.Tick(); retired.Set(0, 0, 0);
            retired.Member.ItemPhase = null;
            retired.Member.ItemPhase = RuntimePlayerItemPhase1458.Constructor;
            retired.Set(58, 110, 3);
            var store = (RuntimePlayerInventoryStore)typeof(PlayerAuthority).GetField("inventory", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(retired.Players)!;
            Assert.True(store.TryGetFundedQuantity(retired.Connection, 58, out var quantity)); Assert.Equal(0, quantity);
        }
        using (var reentry = new Fixture())
        {
            reentry.Drop(110, 3); reentry.State.Tick();
            bool entered = false;
            reentry.Events.Callback = () => { if (entered) return; entered = true; reentry.Set(58, 110, 3); };
            reentry.Set(0, 0, 0); reentry.Events.Callback = null;
            Assert.True(reentry.ItemImage()[58].IsEmpty);
            reentry.Set(58, 110, 3);
            var store = (RuntimePlayerInventoryStore)typeof(PlayerAuthority).GetField("inventory", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(reentry.Players)!;
            Assert.True(store.TryGetFundedQuantity(reentry.Connection, 58, out var quantity)); Assert.Equal(0, quantity);
        }
        using (var wrongPlayer = new Fixture())
        {
            wrongPlayer.Drop(110, 3); wrongPlayer.State.Tick();
            wrongPlayer.State.Apply(new PlayerEquipmentRuntimeCommand(wrongPlayer.Connection, new(new PlayerSlotId(2), 0, 0, 0, 0, 0)));
            Assert.Equal(3, wrongPlayer.ItemImage()[0].Stack); Assert.True(wrongPlayer.ItemImage()[58].IsEmpty);
        }
        using (var unowned = new Fixture())
        {
            unowned.Set(0, 110, 3); unowned.Set(0, 0, 0); unowned.Set(58, 110, 3);
            var store = (RuntimePlayerInventoryStore)typeof(PlayerAuthority).GetField("inventory", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(unowned.Players)!;
            Assert.True(store.TryGetFundedQuantity(unowned.Connection, 58, out var quantity)); Assert.Equal(0, quantity);
        }
    }

    private static void VerifyOriginalCursorImages()
    {
        int compared = 0;
        foreach (string resourceName in new[] { "OwnedCursorMove1458", "OwnedCursorMoveSupplement1458", "OwnedCursorClock1458" })
        {
        using var resource = typeof(OwnedHumanPickupAuthority1458Tests).Assembly.GetManifestResourceStream(resourceName);
        Assert.NotNull(resource); using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (string platform in new[] { "linux", "windows" })
        foreach (var row in document.RootElement.GetProperty(platform).GetProperty("rows").EnumerateArray())
        {
            string name = row.GetProperty("spec").GetProperty("Name").GetString()!;
            // Configured original Item.Prefix retains82; this proves tuple movement, not GetData5 admission.
            short slot = checked((short)row.GetProperty("spec").GetProperty("Slot").GetInt32());
            var before = row.GetProperty("before"); var after = row.GetProperty("afterMirror");
            using var f = new Fixture(platform == "windows");
            SeedOriginalCursorBefore(f, ReadCursorTuple(before.GetProperty("inventory")[slot]), ReadCursorTuple(before.GetProperty("mouse")), slot);
            var spec = row.GetProperty("spec");
            f.Member.ItemAnimation = spec.GetProperty("Animation").GetInt32();
            if (f.Member.ItemPhase is { } sourcePhase)
                f.Member.ItemPhase = sourcePhase with { Selected = sourcePhase.Selected with
                { Animation = f.Member.ItemAnimation, ItemTime = spec.TryGetProperty("Time", out var time) ? time.GetInt32() : 0 } };
            var nextSlot = ReadCursorTuple(after.GetProperty("inventory")[slot]);
            var nextCursor = ReadCursorTuple(after.GetProperty("mouse"));
            f.Set(slot, checked((short)nextSlot.ItemType.Value), nextSlot.Stack, checked((byte)nextSlot.Prefix.Value), nextSlot.ItemFlags);
            f.Set(58, checked((short)nextCursor.ItemType.Value), nextCursor.Stack, checked((byte)nextCursor.Prefix.Value), nextCursor.ItemFlags);
            var image = f.ItemImage();
            for (int index = 0; index < image.Length; index++) Assert.True(ReadCursorTuple(after.GetProperty("inventory")[index]) == image[index], $"{platform}/{name}/slot{index}: wanted={ReadCursorTuple(after.GetProperty("inventory")[index])} actual={image[index]}");
            var store = (RuntimePlayerInventoryStore)typeof(PlayerAuthority).GetField("inventory", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Players)!;
            Assert.True(store.TryGetFundedQuantity(f.Connection, slot, out var slotFunding)); Assert.Equal(image[slot].Stack, slotFunding);
            Assert.True(store.TryGetFundedQuantity(f.Connection, 58, out var cursorFunding)); Assert.Equal(image[58].Stack, cursorFunding);
            compared++;
        }
        }
        Assert.Equal(122, compared); // Includes unchanged UI-gated source tuples as ordinary acknowledgements.
    }
    private static RuntimePlayerInventoryItem ReadCursorTuple(JsonElement item) => I(item, "type") == 0 ? default :
        new(new(I(item, "type")), checked((short)I(item, "stack")), new(I(item, "prefix")), item.GetProperty("favorite").GetBoolean() ? (byte)1 : (byte)0);
    private static void SeedOriginalCursorBefore(Fixture f, RuntimePlayerInventoryItem slotItem, RuntimePlayerInventoryItem cursorItem, short slot)
    {
        bool matching = !slotItem.IsEmpty && slotItem.ItemType == cursorItem.ItemType && slotItem.Prefix == cursorItem.Prefix;
        if (matching && slotItem.Stack + cursorItem.Stack <= 9999)
        {
            SeedFundedCursorTuple(f, slotItem with { Stack = checked((short)(slotItem.Stack + cursorItem.Stack)), ItemFlags = 0 }, slot);
            for (short count = 1; count <= cursorItem.Stack; count++)
            { f.Set(slot, checked((short)slotItem.ItemType.Value), checked((short)(slotItem.Stack + cursorItem.Stack - count))); f.Set(58, checked((short)cursorItem.ItemType.Value), count); }
        }
        else if (matching)
        {
            short excess = checked((short)(slotItem.Stack + cursorItem.Stack - 9999));
            SeedFundedCursorTuple(f, slotItem with { Stack = 9999, ItemFlags = 0 }, slot);
            SeedFundedCursorTuple(f, cursorItem with { Stack = excess, ItemFlags = 0 }, 58);
            if (cursorItem.Stack == 9999)
            { f.Set(slot, checked((short)slotItem.ItemType.Value), slotItem.Stack); f.Set(58, checked((short)cursorItem.ItemType.Value), cursorItem.Stack); }
            else if (cursorItem.Stack - excess <= 4)
            {
                for (short count = excess; count < cursorItem.Stack; count++)
                { f.Set(slot, checked((short)slotItem.ItemType.Value), checked((short)(9999 - (count - excess + 1)))); f.Set(58, checked((short)cursorItem.ItemType.Value), checked((short)(count + 1))); }
            }
            else
            {
                Assert.InRange(slotItem.Stack - excess, 0, 4);
                f.Set(slot, checked((short)slotItem.ItemType.Value), excess); f.Set(58, checked((short)cursorItem.ItemType.Value), 9999);
                short temp = slot == 49 ? (short)48 : (short)49;
                f.Set(temp, checked((short)slotItem.ItemType.Value), 9999); f.Set(58, 0, 0);
                short moved = checked((short)(slotItem.Stack - excess));
                for (short count = 1; count <= moved; count++)
                { f.Set(temp, checked((short)slotItem.ItemType.Value), checked((short)(9999 - count))); f.Set(58, checked((short)cursorItem.ItemType.Value), count); }
                f.Set(slot, checked((short)slotItem.ItemType.Value), slotItem.Stack); f.Set(58, 0, 0);
                f.Set(temp, 0, 0); f.Set(58, checked((short)cursorItem.ItemType.Value), cursorItem.Stack);
            }
        }
        else { SeedFundedCursorTuple(f, slotItem with { ItemFlags = 0 }, slot); SeedFundedCursorTuple(f, cursorItem with { ItemFlags = 0 }, 58); }
        if (!slotItem.IsEmpty && slotItem.ItemFlags != 0) f.Set(slot, checked((short)slotItem.ItemType.Value), slotItem.Stack, checked((byte)slotItem.Prefix.Value), slotItem.ItemFlags);
        if (!cursorItem.IsEmpty && cursorItem.ItemFlags != 0) f.Set(58, checked((short)cursorItem.ItemType.Value), cursorItem.Stack, checked((byte)cursorItem.Prefix.Value), cursorItem.ItemFlags);
    }
    internal static void SeedFundedCursorTuple(Fixture f, RuntimePlayerInventoryItem item, short wanted)
    {
        if (item.IsEmpty) return;
        var grant = f.Drop(checked((short)item.ItemType.Value), item.Stack, prefix: checked((byte)item.Prefix.Value));
        f.State.Apply(new WorldItemRemoveRuntimeCommand(f.Connection, grant.Handle));
        var image = f.ItemImage();
        short donor = checked((short)Array.FindIndex(image, value => value.ItemType == item.ItemType && value.Stack == item.Stack));
        Assert.True(donor >= 0, $"seed{item}/wanted{wanted}: {string.Join(";", image.Where(value => !value.IsEmpty))}");
        if (item.ItemFlags != 0) f.Set(donor, checked((short)item.ItemType.Value), item.Stack, checked((byte)item.Prefix.Value), item.ItemFlags);
        f.Set(donor, 0, 0); f.Set(58, checked((short)item.ItemType.Value), item.Stack, checked((byte)item.Prefix.Value), item.ItemFlags);
        Assert.Equal(item, f.ItemImage()[58]);
        if (wanted != 58) { f.Set(wanted, checked((short)item.ItemType.Value), item.Stack, checked((byte)item.Prefix.Value), item.ItemFlags); f.Set(58, 0, 0); Assert.Equal(item, f.ItemImage()[wanted]); Assert.True(f.ItemImage()[58].IsEmpty); }
    }

    internal sealed class Fixture : IDisposable
    {
        PlayerJoinSession session;
        internal readonly ServerRuntimeState State;
        internal readonly ServerRuntimeComposition Runtime;
        internal PlayerAuthority Players => Runtime.Players;
        internal readonly RuntimeWorldItemStore Items;
        internal readonly RuntimeProjectileStore Projectiles = new();
        internal readonly Events Events = new(); internal readonly WorldEvents WorldEvents = new();
        internal ConnectionHandle Connection;
        internal RuntimePlayerMember Member { get { Assert.True(Players.TryGet(Connection, out var m)); return m; } }
        internal Fixture(bool windows = false, RuntimeWorldItemStore? providedItems = null, bool bindPhase = true)
        {
            var tiles = new WorldTileStore(new(400, 300)); for (int x = 0; x < 400; x++) tiles.Set(x, 103, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            var projectiles = Projectiles; Items = providedItems ?? new(WorldEvents); State = new(playerEvents: Events, worldItems: Items, worldTiles: tiles, projectiles: projectiles, playerUpdateRandomSeed: bindPhase ? new(0) : null);
            Runtime = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(State)!;
            Players.SetPlayerUpdateWorldFacts(new RuntimePlayerUpdateWorld1458(400, 300, 80, false, false) { WindowsItemPrefixArithmetic = windows });
            Players.SetRemotePlayerEnvironment(new(false, false), projectiles);
            var pool = new PlayerSlotPool(3); Assert.True(pool.TryAcquireConnection(out var lease)); session = new(lease!); session.ObserveWorldRequest(); session.ObserveSectionRequest(); Connection = new(GameCommandSourceId.FromConnection(98901), session.Handle);
            State.Apply(new PlayerHealthRuntimeCommand(Connection, new(session.Slot, 400, 400))); State.Apply(new PlayerManaRuntimeCommand(Connection, new(session.Slot, 50, 200))); State.Apply(new PlayerSpawnRuntimeCommand(Connection, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
            State.Apply(new PlayerMovementRuntimeCommand(Connection, new(session.Slot, 64, 16, 0, 0, 0, 1600, 1606, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
            State.Apply(new PlayerBuffTypesRuntimeCommand(Connection, new(Connection.Player.Slot, Array.Empty<BuffTypeId>())));
            Events.Seen.Clear();
        }
        internal void Reconnect()
        {
            State.Apply(new PlayerDisconnectRuntimeCommand(Connection));
            session.Dispose();
            var pool = new PlayerSlotPool(3);
            Assert.True(pool.TryAcquireConnection(out var lease));
            session = new(lease!); session.ObserveWorldRequest(); session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(98903), session.Handle);
            State.Apply(new PlayerSpawnRuntimeCommand(Connection, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
            State.Apply(new PlayerMovementRuntimeCommand(Connection, new(session.Slot, 64, 16, 0, 0, 0, 1600, 1606, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        }
        internal void Set(short slot, short type, short stack, byte prefix = 0, byte flags = 0) => State.Apply(new PlayerEquipmentRuntimeCommand(Connection, new(Connection.Player.Slot, slot, stack, prefix, type, flags)));
        internal WorldItemSnapshot Drop(short type, short stack, float x = 1600, byte owner = 0, float shimmer = 0, bool shimmered = false, float vx = 0, int delay = 0, byte delayPlayer = 1, float vy = 0, byte prefix = 0)
        {
            // Explicit trusted server NewItem producer. Generic TryAllocate/upsert has unknown funding.
            WorldItemSnapshot item;
            if (shimmer < 0)
                Assert.True(Items.TryAllocate(new WorldItemStateUpdate(x, 1606, vx, vy, stack, prefix, WorldItemOwnershipMode.None, type, shimmered, shimmer, 0, owner, 0, delayPlayer, delay), out item)); // Malformed source-state refusal; unknown funding.
            else Assert.True(Items.TryAllocateSourceDrop(new WorldItemDropStateUpdate(x, 1606, vx, vy, stack, prefix, WorldItemOwnershipMode.None, type, shimmered, shimmer, 0), default, out item, out _), $"trusted source allocation type={type} shimmer={shimmer} x={x} owner={owner}");
            Assert.True(Items.TryApplyOwner(item.Handle.Slot, new(owner, 15, delayPlayer, delay, x, 1606), out item)); return item;
        }
        internal RuntimePlayerInventoryItem[] ItemImage() { var result = new RuntimePlayerInventoryItem[59]; for (short i = 0; i < 59; i++) { Assert.True(Players.TryGetInventoryItem(Connection, i, out result[i])); } return result; }
        public void Dispose() => session.Dispose();
    }
    internal sealed class Events : IRuntimePlayerEventSink
    {
        internal readonly List<short> Seen = new(); internal Action? Callback; public void PlayerEquipmentUpdated(ConnectionHandle c, in PlayerEquipmentCommitRequest r) { Seen.Add(r.SlotId); Callback?.Invoke(); }
        public void PlayerAppearanceUpdated(ConnectionHandle c, in PlayerAppearanceCommitRequest r) { }
        public void PlayerSpawned(ConnectionHandle c, in PlayerSpawnCommitRequest r) { }
        public void PlayerMoved(ConnectionHandle c, in PlayerMovementCommitRequest r) { }
        public void PlayerDisconnected(ConnectionHandle c) { }
    }
    internal sealed class MutatingProvider(Action mutation) : IWorldItemOwnerFactsProvider1458
    {
        public IWorldItemOwnerFactsSnapshot1458 Capture() { mutation(); return new Snapshot(); }
        private sealed class Snapshot : IWorldItemOwnerFactsSnapshot1458
        { public bool IsCurrent => true; public bool TrySelectOwner(in WorldItemDropStateUpdate drop, int delay, byte grab, out byte owner) { owner = 0; return true; } }
    }
    internal sealed class LateCurrentnessProvider(Action mutation) : IWorldItemOwnerFactsProvider1458
    {
        public IWorldItemOwnerFactsSnapshot1458 Capture() => new Snapshot(mutation);
        private sealed class Snapshot(Action mutation) : IWorldItemOwnerFactsSnapshot1458
        {
            private bool mutated;
            public bool IsCurrent { get { if (!mutated) { mutated = true; mutation(); } return true; } }
            public bool TrySelectOwner(in WorldItemDropStateUpdate drop, int delay, byte grab, out byte owner) { owner = 0; return true; }
        }
    }
    internal sealed class WorldEvents : IWorldItemStateCommitSink
    { internal Action? Callback; internal WorldItemStateCommitKind LastKind; public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot item, int? reservationOffer = null, GameCommandSourceId excludedSource = default) { LastKind = kind; Callback?.Invoke(); } }
}
