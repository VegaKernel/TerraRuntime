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
    { internal Action? Callback; internal WorldItemStateCommitKind LastKind; public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot item) { LastKind = kind; Callback?.Invoke(); } }
}
