using System.Reflection;
using System.Runtime.CompilerServices;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class FishingCharacterStock1458Tests
{
    [Fact]
    public void Initial_character_image_seals_physical_stock_without_minting_private_payment()
    {
        using var f = new Character(new Dictionary<short, RuntimePlayerInventoryItem>
        {
            [0] = Item(2334, 2), [1] = Item(2354, 1), [54] = Item(2674, 3), [99] = Item(2450, 1)
        });
        foreach (var (slot, type) in new[] { (0, 2334), (1, 2354), (54, 2674) })
        {
            Assert.True(f.Inventory.CanSpendFishingStock(f.Connection, slot, type));
            Assert.True(f.Inventory.TryGetFundedQuantity(f.Connection, slot, out short quantity));
            Assert.Equal(0, quantity);
        }
        Assert.All(f.Inventory.CaptureFundedQuantities(f.Connection), quantity => Assert.Equal(0, quantity));
        Assert.All(f.Inventory.CaptureFundedCoinValues(f.Connection), value => Assert.Equal(0, value));
        Assert.True(f.Inventory.TryCaptureShopBankOwnership(f.Connection, out var bankQuantity, out var bankCaps));
        Assert.All(bankQuantity, quantity => Assert.Equal(0, quantity));
        Assert.DoesNotContain(f.Inventory.CaptureOwnedQuantities(f.Connection), cap => cap.ItemType.Value is 109 or 110 or 189 or 500 or 2209);
        Assert.All(Enumerable.Range(0, 160), slot =>
            Assert.All(bankCaps.AsSpan(slot * RuntimePlayerInventoryStore.QuantityLaneCount, 5).ToArray(), cap => Assert.Equal(-1, cap)));

        var before = f.Image();
        f.Set(0, 2334, 3); f.Set(2, 2334, 1); f.Set(54, 2674, 4); f.Set(3, 2356, 1);
        f.Set(99, 2450, 2); f.Set(299, 2674, 1); f.Set(500, 2334, 1); f.Set(700, 2354, 1);
        f.Complete();
        Assert.Equal(before, f.Image());
        Assert.True(f.Inventory.TryCaptureShopBanks(f.Connection, new RuntimePlayerInventoryItem[160], new long[160]));
        Assert.True(f.Inventory.TryCaptureShopBankOwnership(f.Connection, out var afterQuantity, out var afterCaps));
        Assert.Equal(bankQuantity, afterQuantity); Assert.Equal(bankCaps, afterCaps);
        Assert.False(f.Inventory.CanSpendFishingStock(f.Connection, 2, 2334));
    }

    [Fact]
    public void Imported_zero_budget_crates_survive_cursor_sort_and_bank_bait_withdrawal_without_opening()
    {
        using var f = new Character(new() { [0] = Item(2334, 3), [99] = Item(2674, 2) });
        var random = ((SystemWorldItemSpawnRandom)f.Runtime.WorldItems.SpawnRandom).SourceRandom;
        var beforeRandom = random.Clone();
        f.Set(0, 0, 0); f.Set(58, 2334, 3); f.Complete();
        Assert.Equal(Item(2334, 3), f.Image()[58]);
        f.Set(49, 2334, 3); f.Set(58, 0, 0); f.Complete();
        Assert.True(f.Inventory.CanSpendFishingStock(f.Connection, 49, 2334));

        var before = f.Image();
        var slots = before.Select(item => new WorldItemPickupSlot1458(item.ItemType.Value, item.Stack, item.Prefix.Value, false)).ToArray();
        Assert.True(VanillaWorldItemOwner1458.TryPlanSort(slots, true, 0, 228, false, false, new(false, false, false), out var sorted));
        Assert.NotNull(sorted);
        for (short slot = 0; slot < before.Length; slot++)
            if (sorted.Slots[slot] != slots[slot])
                f.Set(slot, checked((short)sorted.Slots[slot].Type), checked((short)sorted.Slots[slot].Stack));
        f.Complete();
        var after = f.Image();
        Assert.Equal(3, after.Where(item => item.ItemType.Value == 2334).Sum(item => item.Stack));
        int destination = Array.FindIndex(after, item => item.ItemType.Value == 2334);
        Assert.True(f.Inventory.CanSpendFishingStock(f.Connection, destination, 2334));
        Assert.All(f.Inventory.CaptureFundedQuantities(f.Connection), quantity => Assert.Equal(0, quantity));

        // Source bank-to-cursor gesture reports the low cursor slot before99.
        // Its genuine two units must remain usable after a second cursor move.
        f.Set(58, 2674, 2); f.Set(99, 0, 0); f.Complete();
        Assert.Equal(Item(2674, 2), f.Image()[58]);
        f.Set(54, 2674, 2); f.Set(58, 0, 0); f.Complete();
        Assert.True(f.Inventory.CanSpendFishingStock(f.Connection, 54, 2674));
        Assert.True(f.Inventory.TryCaptureShopBanks(f.Connection, new RuntimePlayerInventoryItem[160], new long[160]));
        Assert.All(f.Inventory.CaptureFundedQuantities(f.Connection), quantity => Assert.Equal(0, quantity));
        Assert.True(beforeRandom.HasSameState(random));
        Assert.DoesNotContain(f.Image(), item => item.ItemType.Value == 2323);
        var worldItems = new WorldItemSnapshot[f.Items.Capacity];
        Assert.Equal(0, f.Items.CopyActive(worldItems));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Imported_last_crate_actual_reports_pay_once_and_cannot_restore_or_launder_stock(bool rewardFirst)
    {
        short sourceSlot = rewardFirst ? (short)49 : (short)0;
        short potionSlot = rewardFirst ? (short)0 : (short)1;
        short baitSlot = rewardFirst ? (short)48 : (short)49;
        using var f = new Character(new() { [sourceSlot] = Item(2334, 1) });
        var random = ((SystemWorldItemSpawnRandom)f.Runtime.WorldItems.SpawnRandom).SourceRandom;
        random.CopyStateFrom(new(0));
        if (!rewardFirst) f.Set(sourceSlot, 0, 0);
        f.Set(potionSlot, 2323, 3); f.Set(baitSlot, 2674, 4);
        if (rewardFirst) f.Set(sourceSlot, 0, 0);
        f.Complete();
        Assert.True(f.Image()[sourceSlot].IsEmpty);
        Assert.Equal(Item(2323, 3), f.Image()[potionSlot]); Assert.Equal(Item(2674, 4), f.Image()[baitSlot]);
        Assert.True(f.Inventory.TryGetFundedQuantity(f.Connection, sourceSlot, out short source)); Assert.Equal(0, source);
        Assert.True(f.Inventory.TryGetFundedQuantity(f.Connection, baitSlot, out short reward)); Assert.Equal(4, reward);
        var accepted = f.Image(); var rng = random.Clone();
        f.Complete(); f.Set(sourceSlot, 2334, 1); f.Set(99, 2334, 1); f.Complete();
        Assert.Equal(accepted, f.Image()); Assert.True(rng.HasSameState(random));
        Assert.False(f.Inventory.CanSpendFishingStock(f.Connection, sourceSlot, 2334));
        // The seal is idempotent; a later call cannot replace the consumed cap.
        f.Inventory.SealFishingCharacterInventory(f.Connection);
        f.Set(sourceSlot, 2334, 1); Assert.True(f.Image()[sourceSlot].IsEmpty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Staged_unearned_crate_credit_cannot_pay_itself_or_lose_imported_source(bool reportDebit)
    {
        using var f = new Character(new() { [0] = Item(2334, 2) });
        var before = f.Image();
        var random = ((SystemWorldItemSpawnRandom)f.Runtime.WorldItems.SpawnRandom).SourceRandom;
        var beforeRandom = random.Clone();
        if (reportDebit) f.Set(0, 2334, 1);
        f.Set(49, 2674, 999); // Exceeds every original wooden-crate bait reward.
        f.Complete();
        Assert.Equal(before, f.Image()); Assert.True(beforeRandom.HasSameState(random));
        Assert.True(f.Inventory.CanSpendFishingStock(f.Connection, 0, 2334));
        Assert.All(f.Inventory.CaptureFundedQuantities(f.Connection), quantity => Assert.Equal(0, quantity));
        Assert.Equal(0, f.Items.CopyActive(new WorldItemSnapshot[f.Items.Capacity]));
    }

    [Fact]
    public void Imported_bait_reaches_real_bite_catch_and_last_unit_without_funding_the_source()
    {
        using var f = new FishingAuthority1458Tests.FishingFixture(1, importedBait: true);
        var inventory = FishingCrateAuthority1458Tests.Store(f.Players);
        Assert.True(inventory.CanSpendFishingStock(f.Connection, f.BaitSlot, 2674));
        bool caught = false;
        for (int cast = 0; cast < 10 && !f.Item(f.BaitSlot).IsEmpty; cast++)
        {
            var birth = f.Cast(); var bite = f.WaitForBite(); f.Pull();
            var returning = f.Authority.CaptureFishingBobber(f.Connection);
            Assert.NotNull(returning);
            bool paid = returning.PaidCatch;
            short after = f.Item(f.BaitSlot).Stack;
            f.Report(f.BirthPacket with { Ai0 = 1, Ai1 = bite.Result.ItemType }); f.Pull();
            Assert.Equal(after, f.Item(f.BaitSlot).Stack);
            f.Return();
            if (paid) { Assert.True(f.Count(bite.Result.ItemType) > 0); caught = true; }
            Assert.Equal(0, f.Funded(f.BaitSlot));
            Assert.False(f.Shots.TryGet(birth.Handle, out _));
        }
        Assert.True(caught); Assert.True(f.Item(f.BaitSlot).IsEmpty);
        f.Apply(new PlayerEquipmentRuntimeCommand(f.Connection, Item(2674, 1).ToCommitRequest(f.Connection.Player.Slot, f.BaitSlot)));
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection));
        Assert.True(f.Item(f.BaitSlot).IsEmpty);
        Assert.False(inventory.CanSpendFishingStock(f.Connection, f.BaitSlot, 2674));
    }

    [Theory]
    [InlineData(2354, 121, 28800)]
    [InlineData(2355, 122, 28800)]
    [InlineData(2356, 123, 14400)]
    public void Imported_fishing_potion_uses_existing_effect_clock_and_consumes_last_unit(short potion, short buff, int duration)
    {
        using var f = new FishingAuthority1458Tests.FishingFixture(1, importedBait: true,
            initialItems: new Dictionary<short, RuntimePlayerInventoryItem> { [2] = Item(potion, 1) });
        Assert.Equal(0, f.Funded(2));
        f.Control(false, 2); for (int tick = 0; tick < 20; tick++) f.Tick();
        f.Control(true, 2); f.Tick(); f.Control(false, 2);
        Assert.True(f.Item(2).IsEmpty); Assert.Equal(0, f.Funded(2));
        var buffs = Profiles(f.Players).CaptureBuffState(f.Connection)!;
        Assert.Equal(duration, buffs.GetDuration(new(buff)));
        f.Apply(new PlayerEquipmentRuntimeCommand(f.Connection, Item(potion, 1).ToCommitRequest(f.Connection.Player.Slot, 2)));
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection));
        Assert.True(f.Item(2).IsEmpty);
        Assert.Equal(duration, Profiles(f.Players).CaptureBuffState(f.Connection)!.GetDuration(new(buff)));
    }

    [Fact]
    public void Imported_real_quest_fish_is_spent_by_existing_Angler_owner_once()
    {
        using var f = new Character(new() { [0] = Item(2450, 1) });
        f.State.BindAnglerWorld(new WorldFileRuntimeMetadata { WorldSurface = 80, AnglerQuest = new(0, []) });
        f.State.Apply(new PlayerAppearanceRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, 0, 0, 0, 0, "Imported fisher",
            0, 0, 0, default, default, default, default, default, default, default, 0, 0, 0)));
        var npcs = new RuntimeNpcStore();
        Assert.True(npcs.TrySpawnVanilla(new(369, 369, 1610, 1606, 0, 0, 255, default, NpcSimulationState.Initial), out var vendor));
        Assert.True(f.Players.TrySetTalkNpc(f.Connection, checked((short)vendor.Handle.Slot)));
        var talk = new RuntimeTownShopSession1458(checked((short)vendor.Handle.Slot), new(369),
            RuntimeTownShopSessionKind1458.TalkOnly, 1f, false, default, [], RuntimeTownCommerceMissingFacts1458.None)
        { VendorHandle = vendor.Handle, VendorStore = npcs };
        Assert.True(f.Players.TrySetTownShopSession(f.Connection, talk));
        Assert.True(f.State.TryCompleteAnglerQuest(f.Connection));
        Assert.DoesNotContain(f.Image(), item => item.ItemType.Value == 2450);
        Assert.True(f.State.TryCaptureAnglerState(f.Connection, out _, out bool done, out int count));
        Assert.True(done); Assert.Equal(1, count);
        f.Set(0, 2450, 1); f.Complete();
        Assert.DoesNotContain(f.Image(), item => item.ItemType.Value == 2450);
        Assert.False(f.State.TryCompleteAnglerQuest(f.Connection));
        Assert.True(f.State.TryCaptureAnglerState(f.Connection, out _, out done, out count)); Assert.Equal(1, count);
    }

    [Fact]
    public async Task Consumed_import_ceiling_survives_respawn_and_managed_transfer_but_new_join_imports_its_own_image()
    {
        using var f = new Character(new() { [0] = Item(2334, 1), [1] = Item(2354, 2) });
        Assert.True(f.Players.TryOpenFishingCrate(f.Connection, 0, f.Inventory.Serial));
        f.State.Apply(new PlayerRespawnRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, 100, 103, 0, 0, 0, 0, 0)));
        f.Set(0, 2334, 1); Assert.True(f.Image()[0].IsEmpty);
        var detached = new TaskCompletionSource<RuntimePlayerTransferState?>();
        f.State.Apply(new PlayerTransferDetachRuntimeCommand(f.Connection, detached));
        var transfer = await detached.Task; Assert.NotNull(transfer);
        Assert.Equal(0, transfer.FundedInventoryQuantities[1]);
        Assert.Contains(transfer.OwnedInventoryQuantities, cap => cap.Slot == 0 && cap.ItemType.Value == 2334 && cap.Maximum == 0);
        var attached = new TaskCompletionSource<bool>();
        f.State.Apply(new PlayerTransferAttachRuntimeCommand(f.Connection, transfer, 100, 103, true, false, attached));
        Assert.True(await attached.Task);
        Assert.True(f.Inventory.CanSpendFishingStock(f.Connection, 1, 2354));
        f.Set(0, 2334, 1); Assert.True(f.Image()[0].IsEmpty);
        f.State.Apply(new PlayerDisconnectRuntimeCommand(f.Connection));
        Assert.False(f.Inventory.CanSpendFishingStock(f.Connection, 1, 2354));
        using var joined = new Character(new() { [0] = Item(2334, 1) });
        Assert.True(joined.Inventory.CanSpendFishingStock(joined.Connection, 0, 2334));
        Assert.Equal(0, joined.Inventory.CaptureFundedQuantities(joined.Connection)[0]);
    }

    private static RuntimePlayerInventoryItem Item(short type, short stack) => new(new(type), stack, default, 0);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "transferProfiles")]
    private static extern ref RuntimePlayerTransferProfileStore Profiles(PlayerAuthority players);

    private sealed class Character : IDisposable
    {
        private readonly PlayerJoinSession session;
        internal readonly ServerRuntimeState State;
        internal readonly ServerRuntimeComposition Runtime;
        internal readonly RuntimeWorldItemStore Items = new();
        internal readonly ConnectionHandle Connection;
        internal PlayerAuthority Players => Runtime.Players;
        internal RuntimePlayerInventoryStore Inventory => FishingCrateAuthority1458Tests.Store(Players);
        internal Character(Dictionary<short, RuntimePlayerInventoryItem> imported)
        {
            State = new(worldTiles: new(new(400, 300)), worldItems: Items, playerUpdateRandomSeed: new(0), itemPrefixWorld: new(false, false, false));
            Runtime = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(State)!;
            Players.SetPlayerUpdateWorldFacts(new(400, 300, 80, false, false) { WindowsItemPrefixArithmetic = false });
            Players.BindFishingCrateWorld(false);
            var pool = new PlayerSlotPool(1); Assert.True(pool.TryAcquireConnection(out var lease)); session = new(lease!);
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(99423), session.Handle);
            State.Apply(new PlayerHealthRuntimeCommand(Connection, new(session.Slot, 400, 400)));
            State.Apply(new PlayerManaRuntimeCommand(Connection, new(session.Slot, 50, 200)));
            for (short slot = 0; slot < 59; slot++)
                State.Apply(new PlayerEquipmentRuntimeCommand(Connection, imported.GetValueOrDefault(slot).ToCommitRequest(session.Slot, slot)));
            foreach (var pair in imported.Where(pair => pair.Key >= 59))
                State.Apply(new PlayerEquipmentRuntimeCommand(Connection, pair.Value.ToCommitRequest(session.Slot, pair.Key)));
            // Vanilla initial5 comes before spawn12; no fabricated initial138.
            State.Apply(new PlayerSpawnRuntimeCommand(Connection, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
            State.Apply(new PlayerMovementRuntimeCommand(Connection, new(session.Slot, 64, 16, 0, 0, 0, 1600, 1606, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
            State.Apply(new PlayerBuffTypesRuntimeCommand(Connection, new(session.Slot, Array.Empty<BuffTypeId>())));
        }
        internal void Set(short slot, short type, short stack) =>
            State.Apply(new PlayerEquipmentRuntimeCommand(Connection, Item(type, stack).ToCommitRequest(Connection.Player.Slot, slot)));
        internal void Complete()
        {
            State.Apply(new PlayerInventorySyncCompleteRuntimeCommand(Connection));
            int slices = 0;
            while (!Players.CanApplyShopSource(Connection.Source))
            {
                Assert.True(slices++ < 100_000);
                Players.TickShopJournalWork();
            }
        }
        internal RuntimePlayerInventoryItem[] Image()
        {
            var image = new RuntimePlayerInventoryItem[59]; Assert.True(Inventory.TryCopyInventory(Connection, image)); return image;
        }
        public void Dispose() => session.Dispose();
    }
}
