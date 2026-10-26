using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class FishingCrateAuthority1458Tests
{
    [Fact]
    public void All_crates_match_original_sequential_QuickSpawn_storage_prefixes_and_next_random()
    {
        using var resource = typeof(FishingCrateAuthority1458Tests).Assembly.GetManifestResourceStream("OriginalFishingCrate1458");
        Assert.NotNull(resource);
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var source = JsonDocument.Parse(gzip);
        int compared = 0;
        foreach (var row in source.RootElement.GetProperty("rows").EnumerateArray())
        {
            bool remix = row.GetProperty("remix").GetBoolean(); int id = row.GetProperty("id").GetInt32();
            using var f = new Fixture(remix);
            f.Players.BindFishingRewards(f.Runtime.WorldItems);
            var random = ((SystemWorldItemSpawnRandom)f.Runtime.WorldItems.SpawnRandom).SourceRandom;
            random.CopyStateFrom(new(row.GetProperty("seed").GetInt32()));
            Assert.True(f.Players.TryFishingRewards(f.Connection, -1, 0, false,
                rewards => VanillaFishingCrate1458.TryOpen(id, remix, false, rewards.Random.Next, rewards.QuickSpawn)), $"crate{id}/{row.GetProperty("seed")}/{remix}");
            var actual = f.ItemImage(); int slot = 0;
            foreach (var item in row.GetProperty("official").GetProperty("inventory").EnumerateArray())
            {
                Assert.Equal(item.GetProperty("type").GetInt32(), actual[slot].ItemType.Value);
                Assert.Equal(item.GetProperty("stack").GetInt32(), actual[slot].IsEmpty ? 0 : actual[slot].Stack);
                Assert.Equal(item.GetProperty("prefix").GetInt32(), actual[slot].Prefix.Value);
                slot++;
            }
            Assert.Equal(row.GetProperty("official").GetProperty("next").GetInt32(), random.Next());
            compared++;
        }
        Assert.Equal(416, compared);
    }

    [Fact]
    public void Paid_open_rejects_unfunded_and_stale_intents_and_grants_before_last_crate_debit()
    {
        using (var unowned = new Fixture())
        {
            unowned.Players.BindFishingRewards(unowned.Runtime.WorldItems);
            unowned.Set(0, 2334, 1);
            Assert.False(unowned.Players.TryOpenFishingCrate(unowned.Connection, 0, Store(unowned.Players).Serial));
        }
        using var f = new Fixture();
        f.Players.BindFishingRewards(f.Runtime.WorldItems); f.Players.BindFishingCrateWorld(false);
        var inventory = Store(f.Players);
        var ground = f.Drop(2334, 1);
        f.State.Apply(new WorldItemRemoveRuntimeCommand(f.Connection, ground.Handle));
        Assert.True(inventory.TryGetFundedQuantity(f.Connection, 0, out short funded)); Assert.Equal(1, funded);
        Assert.Equal(1, f.ItemImage()[0].Stack);
        // All main slots are occupied. The source crate remains in slot0 while
        // rewards are assigned; spending its last unit must not make a reward fit.
        for (short slot = 1; slot < 50; slot++) f.Set(slot, 3, 9999);
        ulong serial = inventory.Serial;
        ((SystemWorldItemSpawnRandom)f.Runtime.WorldItems.SpawnRandom).SourceRandom.CopyStateFrom(new(0));
        var beforeRandom = ((SystemWorldItemSpawnRandom)f.Runtime.WorldItems.SpawnRandom).SourceRandom.Clone();
        Assert.False(f.Players.TryOpenFishingCrate(f.Connection, 0, serial - 1));
        Assert.True(beforeRandom.HasSameState(((SystemWorldItemSpawnRandom)f.Runtime.WorldItems.SpawnRandom).SourceRandom));
        int observed = 0;
        f.WorldEvents.Callback = () =>
        {
            observed++;
            Assert.True(f.ItemImage()[0].IsEmpty);
            Assert.True(inventory.TryGetFundedQuantity(f.Connection, 0, out short remaining)); Assert.Equal(0, remaining);
        };
        Assert.True(f.Players.TryOpenFishingCrate(f.Connection, 0, serial));
        Assert.True(f.ItemImage()[0].IsEmpty); Assert.True(observed > 0);
        Assert.False(f.Players.TryOpenFishingCrate(f.Connection, 0, serial));
        Assert.False(f.Players.TryOpenFishingCrate(f.Connection, 0, inventory.Serial));
    }

    [Fact]
    public void Stale_and_failed_reward_plans_publish_nothing_and_keep_rng_and_paid_stock()
    {
        using var f = new Fixture();
        f.Players.BindFishingRewards(f.Runtime.WorldItems);
        var ground = f.Drop(2335, 1); f.State.Apply(new WorldItemRemoveRuntimeCommand(f.Connection, ground.Handle));
        var before = f.ItemImage();
        var random = ((SystemWorldItemSpawnRandom)f.Runtime.WorldItems.SpawnRandom).SourceRandom;
        var beforeRandom = random.Clone();
        f.Events.Seen.Clear();
        Assert.False(f.Players.TryFishingRewards(f.Connection, 0, 2335, true, rewards =>
        {
            Assert.True(rewards.Grant(23, 3));
            return false;
        }));
        Assert.Equal(before, f.ItemImage()); Assert.True(beforeRandom.HasSameState(random)); Assert.Empty(f.Events.Seen);
        Assert.False(f.Players.TryFishingRewards(f.Connection, 0, 2335, false, rewards =>
        {
            Assert.True(rewards.Grant(23, 3));
            f.Set(2, 9, 1); // A newer packet-5 image invalidates the prepared reward.
            return true;
        }));
        Assert.Equal(before[0], f.ItemImage()[0]); Assert.True(beforeRandom.HasSameState(random));
        Assert.True(Store(f.Players).TryGetFundedQuantity(f.Connection, 0, out short funded)); Assert.Equal(1, funded);
    }

    [Fact]
    public async Task Actual_packet5_decrement_opens_a_paid_crate_once_and_cannot_restore_its_last_unit()
    {
        using var f = new Fixture();
        f.Players.BindFishingRewards(f.Runtime.WorldItems); f.Players.BindFishingCrateWorld(false);
        var ground = f.Drop(2334, 1);
        f.State.Apply(new WorldItemRemoveRuntimeCommand(f.Connection, ground.Handle));
        Assert.Equal(2334, f.ItemImage()[0].ItemType.Value);
        var random = ((SystemWorldItemSpawnRandom)f.Runtime.WorldItems.SpawnRandom).SourceRandom;
        random.CopyStateFrom(new(0));
        Assert.True(f.Players.HasOwnedPickupContext);
        Assert.True(Store(f.Players).TryCaptureShopJournal(f.Connection, out _));
        f.Set(0, 0, 0); // Alone this is ambiguous with a legal cursor pickup.
        f.Set(1, 2323, 3); // Original seed0 grant while the crate still occupies0.
        f.Set(49, 2674, 4); // Original seed0 also grants Apprentice Bait.
        f.State.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection));
        Assert.True(f.ItemImage()[0].IsEmpty);
        Assert.Equal(2323, f.ItemImage()[1].ItemType.Value);
        Assert.Equal(3, f.ItemImage()[1].Stack);
        Assert.True(Store(f.Players).TryGetFundedQuantity(f.Connection, 1, out short funded)); Assert.Equal(3, funded);
        var accepted = f.ItemImage(); var acceptedRandom = random.Clone();
        f.Set(0, 0, 0); // Duplicate does not replay the opening or RNG.
        Assert.Equal(accepted, f.ItemImage()); Assert.True(acceptedRandom.HasSameState(random));
        f.Set(0, 2334, 1); // Forged old last-stack report is capped by the existing owner.
        Assert.True(f.ItemImage()[0].IsEmpty); Assert.True(acceptedRandom.HasSameState(random));
        Assert.True(Store(f.Players).TryGetFundedQuantity(f.Connection, 0, out funded)); Assert.Equal(0, funded);
        var detached = new TaskCompletionSource<RuntimePlayerTransferState?>();
        f.State.Apply(new PlayerTransferDetachRuntimeCommand(f.Connection, detached));
        var transfer = await detached.Task; Assert.NotNull(transfer);
        Assert.Contains(transfer.OwnedInventoryQuantities, cap => cap.Slot == 0 && cap.ItemType.Value == 2334 && cap.Maximum == 0);
        Assert.Equal(3, transfer.FundedInventoryQuantities[1]);
        var attached = new TaskCompletionSource<bool>();
        f.State.Apply(new PlayerTransferAttachRuntimeCommand(f.Connection, transfer, 100, 103, true, false, attached));
        Assert.True(await attached.Task);
        f.Set(0, 2334, 1);
        Assert.True(f.ItemImage()[0].IsEmpty);
        Assert.True(Store(f.Players).TryGetFundedQuantity(f.Connection, 1, out funded)); Assert.Equal(3, funded);
    }

    [Fact]
    public void Last_crate_matches_original_ItemSlot_grants_before_debit_for_every_identity()
    {
        using var resource = typeof(FishingCrateAuthority1458Tests).Assembly.GetManifestResourceStream("OriginalFishingCrate1458");
        Assert.NotNull(resource);
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var source = JsonDocument.Parse(gzip);
        int compared = 0;
        foreach (var row in source.RootElement.GetProperty("lastRows").EnumerateArray())
        {
            using var f = new Fixture(row.GetProperty("remix").GetBoolean());
            f.Players.BindFishingRewards(f.Runtime.WorldItems); f.Players.BindFishingCrateWorld(false);
            int id = row.GetProperty("id").GetInt32();
            var ground = f.Drop(checked((short)id), 1);
            f.State.Apply(new WorldItemRemoveRuntimeCommand(f.Connection, ground.Handle));
            var random = ((SystemWorldItemSpawnRandom)f.Runtime.WorldItems.SpawnRandom).SourceRandom;
            random.CopyStateFrom(new(row.GetProperty("seed").GetInt32()));
            Assert.True(f.Players.TryOpenFishingCrate(f.Connection, 0, Store(f.Players).Serial), $"crate{id}/{row.GetProperty("remix")}");
            var actual = f.ItemImage(); int slot = 0;
            foreach (var item in row.GetProperty("inventory").EnumerateArray())
            {
                Assert.Equal(item.GetProperty("type").GetInt32(), actual[slot].ItemType.Value);
                Assert.Equal(item.GetProperty("stack").GetInt32(), actual[slot].IsEmpty ? 0 : actual[slot].Stack);
                Assert.Equal(item.GetProperty("prefix").GetInt32(), actual[slot].Prefix.Value);
                slot++;
            }
            Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());
            compared++;
        }
        Assert.Equal(52, compared);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reward_first_packet5_cohort_pays_only_after_exact_source138_and_rejects_forged_rewards(bool forged)
    {
        using var f = new Fixture();
        f.Players.BindFishingRewards(f.Runtime.WorldItems); f.Players.BindFishingCrateWorld(false);
        // Fill all main slots, remove49, grant the crate into that sole empty
        // slot, then remove0. GetItem's hotbar/reverse order is not bypassed.
        Assert.True(f.Players.TryFishingRewards(f.Connection, -1, 0, false, rewards =>
        {
            for (int i = 0; i < 50; i++) if (!rewards.Grant(3, 9999)) return false;
            return true;
        }));
        Assert.True(Store(f.Players).TryPrepareFundedDrop(f.Connection, 49, out var last)); Assert.NotNull(last);
        Assert.True(last.TryAdoptUnpublished());
        Assert.True(f.Players.TryFishingRewards(f.Connection, -1, 0, false, rewards => rewards.Grant(2334)));
        Assert.True(Store(f.Players).TryPrepareFundedDrop(f.Connection, 0, out var removed)); Assert.NotNull(removed);
        Assert.True(removed.TryAdoptUnpublished());
        Assert.Equal(2334, f.ItemImage()[49].ItemType.Value);
        var random = ((SystemWorldItemSpawnRandom)f.Runtime.WorldItems.SpawnRandom).SourceRandom;
        random.CopyStateFrom(new(0)); var beforeRandom = random.Clone();
        Assert.True(f.Players.HasOwnedPickupContext);
        Assert.True(Store(f.Players).TryCaptureShopJournal(f.Connection, out _));
        f.Set(0, 2323, forged ? (short)4 : (short)3);
        f.Set(49, 0, 0); // Reports ascend: the real reward precedes the crate debit.
        Assert.Equal(2334, f.ItemImage()[49].ItemType.Value);
        Assert.True(Store(f.Players).TryGetFundedQuantity(f.Connection, 49, out short funded)); Assert.Equal(1, funded);
        f.State.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection));
        if (forged)
        {
            Assert.Equal(2334, f.ItemImage()[49].ItemType.Value);
            Assert.True(Store(f.Players).TryGetFundedQuantity(f.Connection, 49, out funded)); Assert.Equal(1, funded);
            Assert.True(Store(f.Players).TryGetFundedQuantity(f.Connection, 0, out funded)); Assert.Equal(0, funded);
            Assert.True(beforeRandom.HasSameState(random));
        }
        else
        {
            Assert.True(f.ItemImage()[49].IsEmpty);
            Assert.Equal(3, f.ItemImage()[0].Stack);
            Assert.True(Store(f.Players).TryGetFundedQuantity(f.Connection, 0, out funded)); Assert.Equal(3, funded);
            var ground = new WorldItemSnapshot[400];
            int count = f.Items.CopyActive(ground);
            var bait = Assert.Single(ground.Take(count), item => item.ItemNetId == 2674);
            Assert.Equal(4, bait.Stack);
            Assert.True(f.Items.TryGetFundedQuantity(bait, out int baitFunding)); Assert.Equal(4, baitFunding);
            var accepted = random.Clone();
            f.State.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection));
            Assert.True(accepted.HasSameState(random));
            Assert.Equal(3, f.ItemImage()[0].Stack);
        }
    }

    [Fact]
    public void Cursor_move_and_main_sort_conserve_paid_crates_without_opening_them()
    {
        using var f = new Fixture();
        var ground = f.Drop(2334, 3); f.State.Apply(new WorldItemRemoveRuntimeCommand(f.Connection, ground.Handle));
        var random = ((SystemWorldItemSpawnRandom)f.Runtime.WorldItems.SpawnRandom).SourceRandom;
        var beforeRandom = random.Clone();
        f.Set(0, 0, 0); f.Set(58, 2334, 3); Complete(f);
        Assert.True(Store(f.Players).TryGetFundedQuantity(f.Connection, 58, out short funded)); Assert.Equal(3, funded);
        f.Set(49, 2334, 3); f.Set(58, 0, 0); Complete(f);
        Assert.True(Store(f.Players).TryGetFundedQuantity(f.Connection, 49, out funded)); Assert.Equal(3, funded);
        var before = f.ItemImage();
        var slots = before.Select(i => new WorldItemPickupSlot1458(i.ItemType.Value, i.Stack, i.Prefix.Value, false)).ToArray();
        Assert.True(VanillaWorldItemOwner1458.TryPlanSort(slots, true, 0, 228, false, false, new(false, false, false), out var sorted));
        Assert.NotNull(sorted);
        for (short slot = 0; slot < before.Length; slot++)
            if (sorted.Slots[slot] != slots[slot]) f.Set(slot, checked((short)sorted.Slots[slot].Type), checked((short)sorted.Slots[slot].Stack));
        Complete(f);
        var after = f.ItemImage();
        Assert.Equal(3, after.Where(i => i.ItemType.Value == 2334).Sum(i => i.Stack));
        short destination = checked((short)Array.FindIndex(after, i => i.ItemType.Value == 2334));
        Assert.True(Store(f.Players).TryGetFundedQuantity(f.Connection, destination, out funded)); Assert.Equal(3, funded);
        Assert.True(beforeRandom.HasSameState(random));
        Assert.Equal(0, f.Items.CopyActive(new WorldItemSnapshot[400]));
    }

    [Fact]
    public void Coalesced_multiple_decrement_restores_the_owned_source_image_and_foreign_capture_is_refused()
    {
        using var f = new Fixture();
        var ground = f.Drop(2334, 3); f.State.Apply(new WorldItemRemoveRuntimeCommand(f.Connection, ground.Handle));
        var random = ((SystemWorldItemSpawnRandom)f.Runtime.WorldItems.SpawnRandom).SourceRandom;
        var beforeRandom = random.Clone(); var before = f.ItemImage();
        f.Set(0, 2334, 1); Complete(f);
        Assert.Equal(before, f.ItemImage());
        Assert.True(Store(f.Players).TryGetFundedQuantity(f.Connection, 0, out short funded)); Assert.Equal(3, funded);
        Assert.True(beforeRandom.HasSameState(random));
        Assert.Equal(0, f.Items.CopyActive(new WorldItemSnapshot[400]));
        using var otherWorld = new Fixture();
        Assert.True(Store(otherWorld.Players).TryCaptureShopJournal(otherWorld.Connection, out var foreign)); Assert.NotNull(foreign);
        Assert.False(Store(f.Players).TryPrepareFishingRewards(f.Connection, -1, 0, out _, foreign));
    }

    private static void Complete(Fixture f)
    {
        f.State.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection));
        int slices = 0;
        while (!f.Players.CanApplyShopSource(f.Connection.Source))
        {
            Assert.True(slices++ < 100_000);
            f.Players.TickShopJournalWork();
        }
    }

    internal static RuntimePlayerInventoryStore Store(PlayerAuthority players) =>
        (RuntimePlayerInventoryStore)typeof(PlayerAuthority).GetField("inventory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(players)!;

    internal sealed class Fixture : IDisposable
    {
        private readonly PlayerJoinSession session;
        internal readonly ServerRuntimeState State;
        internal readonly ServerRuntimeComposition Runtime;
        internal PlayerAuthority Players => Runtime.Players;
        internal readonly RuntimeWorldItemStore Items;
        internal readonly OwnedHumanPickupAuthority1458Tests.Events Events = new();
        internal readonly OwnedHumanPickupAuthority1458Tests.WorldEvents WorldEvents = new();
        internal readonly ConnectionHandle Connection;
        internal Fixture(bool remix = false)
        {
            var tiles = new WorldTileStore(new(400, 300));
            Items = new(WorldEvents);
            State = new(playerEvents: Events, worldItems: Items, worldTiles: tiles, playerUpdateRandomSeed: new(0),
                itemPrefixWorld: new(remix, false, false));
            Runtime = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(State)!;
            Players.SetPlayerUpdateWorldFacts(new(400, 300, 80, remix, false) { WindowsItemPrefixArithmetic = false });
            var pool = new PlayerSlotPool(3); Assert.True(pool.TryAcquireConnection(out var lease)); session = new(lease!);
            session.ObserveWorldRequest(); session.ObserveSectionRequest(); Connection = new(GameCommandSourceId.FromConnection(98902), session.Handle);
            State.Apply(new PlayerHealthRuntimeCommand(Connection, new(session.Slot, 400, 400)));
            State.Apply(new PlayerManaRuntimeCommand(Connection, new(session.Slot, 50, 200)));
            State.Apply(new PlayerSpawnRuntimeCommand(Connection, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
            State.Apply(new PlayerMovementRuntimeCommand(Connection, new(session.Slot, 64, 16, 0, 0, 0, 1600, 1606, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
            State.Apply(new PlayerBuffTypesRuntimeCommand(Connection, new(Connection.Player.Slot, Array.Empty<BuffTypeId>())));
        }
        internal void Set(short slot, short type, short stack) => State.Apply(new PlayerEquipmentRuntimeCommand(Connection,
            new(Connection.Player.Slot, slot, stack, 0, type, 0)));
        internal WorldItemSnapshot Drop(short type, short stack)
        {
            Assert.True(Items.TryAllocateSourceDrop(new(1600, 1606, 0, 0, stack, 0, WorldItemOwnershipMode.None, type, false, 0, 0),
                default, out var item, out _));
            Assert.True(Items.TryApplyOwner(item.Handle.Slot, new(0, 15, 1, 0, 1600, 1606), out item));
            return item;
        }
        internal RuntimePlayerInventoryItem[] ItemImage()
        {
            var image = new RuntimePlayerInventoryItem[59];
            for (short i = 0; i < image.Length; i++) Assert.True(Players.TryGetInventoryItem(Connection, i, out image[i]));
            return image;
        }
        public void Dispose() => session.Dispose();
    }
}
