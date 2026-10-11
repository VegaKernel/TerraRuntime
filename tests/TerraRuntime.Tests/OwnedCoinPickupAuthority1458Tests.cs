using System.IO.Compression;
using System.Text.Json;
using System.Reflection;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Application;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class OwnedCoinPickupAuthority1458Tests
{
    [Fact]
    public void Source_coin_storage_is_adopted_by_actual_owned_tick()
    {
        using var input = typeof(OwnedCoinPickupAuthority1458Tests).Assembly.GetManifestResourceStream("OwnedCoinPickupSource1458");
        Assert.NotNull(input);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var source = JsonDocument.Parse(gzip);
        int compared = 0, excludedFavorite = 0;
        foreach (string platform in new[] { "linux", "windows", "linuxTupleSupplement", "windowsTupleSupplement" })
            foreach (var row in source.RootElement.GetProperty(platform).GetProperty("rows").EnumerateArray())
            {
                var incoming = row.GetProperty("beforeIncoming");
                if (incoming.GetProperty("favorite").GetBoolean()) { excludedFavorite++; continue; }
                using var f = new OwnedHumanPickupAuthority1458Tests.Fixture(platform.StartsWith("windows"));
                short slot = 0;
                foreach (var item in row.GetProperty("before").EnumerateArray())
                    f.Set(slot++, (short)I(item, "type"), (short)I(item, "stack"), (byte)I(item, "prefix"), item.GetProperty("favorite").GetBoolean() ? (byte)1 : (byte)0);
                if (platform.EndsWith("TupleSupplement"))
                {
                    // Original supplement assigns raw prefix after SetDefaults. It proves GetItem
                    // tuple compatibility, not receive-5 or creation-prefix admission.
                    var store = (RuntimePlayerInventoryStore)typeof(PlayerAuthority).GetField("inventory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Players)!;
                    slot = 0;
                    foreach (var item in row.GetProperty("before").EnumerateArray())
                        Assert.True(store.TrySet(f.Connection, new(f.Connection.Player.Slot, slot++, (short)I(item, "stack"), (byte)I(item, "prefix"), (short)I(item, "type"), item.GetProperty("favorite").GetBoolean() ? (byte)1 : (byte)0)));
                }
                var ground = f.Drop((short)I(row, "id"), (short)I(incoming, "stack"), prefix: (byte)I(incoming, "prefix"));
                f.Events.Seen.Clear();
                bool observed = false;
                var publication = new List<int>();
                int residual = I(row.GetProperty("residual"), "stack");
                f.Events.Callback = () => publication.Add(5);
                f.WorldEvents.Callback = () =>
                {
                    bool active = f.Items.TryGetActive(ground.Handle.Slot, out var current);
                    if (active && current.Stack == ground.Stack) return;
                    observed = true;
                    int packet = f.WorldEvents.LastKind == WorldItemStateCommitKind.Remove ? 151 : 21;
                    publication.Add(packet);
                    Assert.Equal(JsonFramePacket(row.GetProperty("pickupFrames")[0]), packet);
                    AssertImage(f, row.GetProperty("after"));
                    Assert.Equal(residual > 0, active);
                    if (residual > 0) { Assert.Equal(residual, (int)current.Stack); Assert.Equal(ground.Handle, current.Handle); }
                };
                f.State.Tick();
                try { AssertImage(f, row.GetProperty("after")); }
                catch (Exception error) { throw new InvalidOperationException($"{platform}/{I(row, "id")}/{row.GetProperty("profile").GetString()}/phase={f.Member.ItemPhase.HasValue}", error); }
                Assert.Equal(residual > 0, f.Items.TryGetActive(ground.Handle.Slot, out var after));
                if (residual > 0) Assert.Equal(residual, (int)after.Stack);
                // State.Tick also owns physical motion, unlike the configured source PickupItem call.
                // Only accepted pickup observers are compared here, not incidental motion notifications.
                Assert.Equal(residual < ground.Stack, observed);
                if (observed) { Assert.True(publication[0] is 21 or 151); Assert.All(publication.Skip(1), packet => Assert.Equal(5, packet)); }
                else Assert.Empty(publication);
                compared++;
            }
        Assert.Equal(152, compared);
        Assert.Equal(16, excludedFavorite);
    }

    [Fact]
    public void Coin_funding_and_exact_journal_refuse_unowned_or_forged_conversion()
    {
        using (var f = new OwnedHumanPickupAuthority1458Tests.Fixture())
        {
            f.Set(50, 71, 99);
            Assert.True(f.Items.TryAllocateSourceDrop(new WorldItemDropStateUpdate(1600, 1606, 0, 0, 1, 0, WorldItemOwnershipMode.None, 71, false, 0, 0), default, out var item, out _));
            Assert.True(f.Items.TryApplyDrop(item.Handle.Slot, new(1600, 1606, 0, 0, 2, 0, WorldItemOwnershipMode.None, 71, false, 0, 0), out item));
            Assert.True(f.Items.TryApplyOwner(item.Handle.Slot, new(0, 15, 1, 0, 1600, 1606), out item));
            f.Events.Seen.Clear(); f.State.Tick();
            Assert.True(f.Players.TryGetInventoryItem(f.Connection, 50, out var untouched));
            Assert.Equal(99, (int)untouched.Stack); Assert.Empty(f.Events.Seen);
            Assert.True(f.Items.TryGetActive(item.Handle.Slot, out _));
        }
        using (var f = new OwnedHumanPickupAuthority1458Tests.Fixture())
        {
            // Original selected neutral coins retain their real style1 remote phase.
            // Pickup uses the unchanged source-selected0 rather than a neutral-slot translation.
            f.Set(0, 71, 1); var item = f.Drop(71, 1);
            f.State.Tick(); Assert.NotNull(f.Member.ItemPhase);
            Assert.False(f.Items.TryGetActive(item.Handle.Slot, out _));
            Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out var held)); Assert.Equal(2, (int)held.Stack);
        }
        using (var f = new OwnedHumanPickupAuthority1458Tests.Fixture())
        {
            var store = (RuntimePlayerInventoryStore)typeof(PlayerAuthority).GetField("inventory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Players)!;
            var before = f.ItemImage(); var incoming = new RuntimePlayerInventoryItem(new(71), 1, default, 0);
            var forged = new RemotePhaseInventoryMutation(f.Connection, 0, before[0], incoming);
            // Value-balanced but wrong destination: original nonfavorite empty profile uses slot53.
            Assert.False(store.TryPreparePickup(f.Connection, before, new[] { forged }, incoming, 1, out _));
            var correct = new RemotePhaseInventoryMutation(f.Connection, 53, before[53], incoming);
            Assert.True(store.TryPreparePickup(f.Connection, before, new[] { correct }, incoming, 1, out var prepared));
            Assert.NotNull(prepared); f.Set(58, 3, 1); Assert.False(prepared.IsCurrent);
            Assert.False(prepared.TryAdoptUnpublished());
            Assert.True(f.Players.TryGetInventoryItem(f.Connection, 50, out var untouched)); Assert.True(untouched.IsEmpty);
        }
        foreach (bool replaceWorld in new[] { false, true })
            using (var f = new OwnedHumanPickupAuthority1458Tests.Fixture())
            {
                f.Set(50, 71, 99); f.Set(51, 72, 99); var item = f.Drop(71, 1);
                f.Events.Seen.Clear(); bool invoked = false;
                f.WorldEvents.Callback = () =>
                {
                    if (invoked || f.Items.TryGetActive(item.Handle.Slot, out _)) return;
                    invoked = true;
                    Assert.True(f.Players.TryGetInventoryItem(f.Connection, 50, out var silver));
                    Assert.True(silver.IsEmpty);
                    Assert.True(f.Players.TryGetInventoryItem(f.Connection, 51, out var gold));
                    Assert.Equal(73, gold.ItemType.Value); Assert.Equal(1, (int)gold.Stack);
                    if (replaceWorld) f.Drop(74, 1);
                    f.Set(51, 3, 77);
                    f.Events.Seen.Clear();
                    throw new InvalidOperationException("accepted coin observer");
                };
                Assert.Throws<InvalidOperationException>(() => f.State.Tick()); Assert.True(invoked);
                Assert.True(f.Players.TryGetInventoryItem(f.Connection, 51, out var newer));
                Assert.Equal(3, newer.ItemType.Value); Assert.Equal(77, (int)newer.Stack);
                Assert.DoesNotContain((short)51, f.Events.Seen);
                Assert.Contains((short)50, f.Events.Seen);
                Assert.Equal(replaceWorld, f.Items.TryGetActive(item.Handle.Slot, out var replacement));
                if (replaceWorld)
                {
                    Assert.NotEqual(item.Handle, replacement.Handle);
                    Assert.Equal(74, replacement.ItemNetId); Assert.Equal(1, (int)replacement.Stack);
                }
            }
    }

    private static int JsonFramePacket(JsonElement frame) => Convert.FromHexString(frame.GetString()!)[2];
    private static int I(JsonElement value, string name) => value.GetProperty(name).GetInt32();
    private static void AssertImage(OwnedHumanPickupAuthority1458Tests.Fixture f, JsonElement expected)
    {
        int slot = 0;
        foreach (var item in expected.EnumerateArray())
        {
            Assert.True(f.Players.TryGetInventoryItem(f.Connection, (short)slot, out var actual));
            Assert.True(I(item, "type") == actual.ItemType.Value, $"slot {slot}: wanted {item}, actual {actual}");
            Assert.Equal(I(item, "stack"), (int)actual.Stack);
            Assert.Equal(I(item, "prefix"), actual.Prefix.Value);
            Assert.Equal(item.GetProperty("favorite").GetBoolean(), actual.ItemFlags == 1);
            slot++;
        }
        Assert.Equal(59, slot);
    }
}
