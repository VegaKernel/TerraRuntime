using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcShopPurchaseExecutorTests
{
    [Fact]
    public void Purchase_debits_coins_grants_item_and_emits_replication_only_after_atomic_commit()
    {
        Fixture fixture = new();
        fixture.SetInventory(50, 1, VanillaCoinFacts.PlatinumCoin);
        NpcShopPurchaseRequest request = fixture.Request(price: 1);

        Assert.Equal(NpcShopPurchaseResult.Committed, fixture.Executor.TryPurchase(fixture.Buyer, in request));

        Assert.True(fixture.Inventory.TryGet(fixture.Buyer, 0, out RuntimePlayerInventoryItem bought));
        Assert.Equal(VanillaItemIds.DirtBlock, bought.ItemType);
        Assert.Equal((short)1, bought.Stack);
        Assert.True(fixture.Inventory.TryGet(fixture.Buyer, 50, out RuntimePlayerInventoryItem gold));
        Assert.Equal(VanillaCoinFacts.GoldCoin, gold.ItemType);
        Assert.Equal((short)99, gold.Stack);
        Assert.True(fixture.Inventory.TryGet(fixture.Buyer, 51, out RuntimePlayerInventoryItem silver));
        Assert.Equal(VanillaCoinFacts.SilverCoin, silver.ItemType);
        Assert.Equal((short)99, silver.Stack);
        Assert.True(fixture.Inventory.TryGet(fixture.Buyer, 52, out RuntimePlayerInventoryItem copper));
        Assert.Equal(VanillaCoinFacts.CopperCoin, copper.ItemType);
        Assert.Equal((short)99, copper.Stack);
        Assert.True(fixture.Inventory.TryGet(fixture.Buyer, 53, out RuntimePlayerInventoryItem oldPlatinum));
        Assert.True(oldPlatinum.IsEmpty);
        Assert.Equal(5, fixture.Events.EquipmentUpdates.Count);
    }

    [Fact]
    public void Insufficient_funds_leave_inventory_untouched()
    {
        Fixture fixture = new();
        fixture.SetInventory(50, 5, VanillaCoinFacts.CopperCoin);
        NpcShopPurchaseRequest request = fixture.Request(price: 6);

        Assert.Equal(NpcShopPurchaseResult.InsufficientFunds, fixture.Executor.TryPurchase(fixture.Buyer, in request));

        Assert.True(fixture.Inventory.TryGet(fixture.Buyer, 50, out RuntimePlayerInventoryItem coins));
        Assert.Equal(VanillaCoinFacts.CopperCoin, coins.ItemType);
        Assert.Equal((short)5, coins.Stack);
        Assert.True(fixture.Inventory.TryGet(fixture.Buyer, 0, out RuntimePlayerInventoryItem item));
        Assert.True(item.IsEmpty);
        Assert.Empty(fixture.Events.EquipmentUpdates);
    }

    [Fact]
    public void Full_main_inventory_rejects_purchase_before_coin_commit()
    {
        Fixture fixture = new();
        for (short slot = 0; slot < 50; slot++)
            fixture.SetInventory(slot, 1, VanillaItemIds.DirtBlock);
        fixture.SetInventory(50, 1, VanillaCoinFacts.SilverCoin);
        NpcShopPurchaseRequest request = fixture.Request(price: 1);

        Assert.Equal(NpcShopPurchaseResult.InventoryFull, fixture.Executor.TryPurchase(fixture.Buyer, in request));

        Assert.True(fixture.Inventory.TryGet(fixture.Buyer, 50, out RuntimePlayerInventoryItem coins));
        Assert.Equal(VanillaCoinFacts.SilverCoin, coins.ItemType);
        Assert.Equal((short)1, coins.Stack);
        Assert.Empty(fixture.Events.EquipmentUpdates);
    }

    [Fact]
    public void Purchase_fails_closed_when_change_needs_more_slots_than_the_inventory_can_hold()
    {
        Fixture fixture = new();
        for (short slot = 0; slot < VanillaPlayerItemSlotCatalog.OrdinaryInventoryCount; slot++)
        {
            if (slot == 0 || slot == 50)
                continue;
            fixture.SetInventory(slot, 1, VanillaItemIds.DirtBlock);
        }
        fixture.SetInventory(50, 1, VanillaCoinFacts.PlatinumCoin);
        NpcShopPurchaseRequest request = fixture.Request(price: 1);

        Assert.Equal(NpcShopPurchaseResult.ChangeDoesNotFit, fixture.Executor.TryPurchase(fixture.Buyer, in request));

        Assert.True(fixture.Inventory.TryGet(fixture.Buyer, 0, out RuntimePlayerInventoryItem destination));
        Assert.True(destination.IsEmpty);
        Assert.True(fixture.Inventory.TryGet(fixture.Buyer, 50, out RuntimePlayerInventoryItem platinum));
        Assert.Equal(VanillaCoinFacts.PlatinumCoin, platinum.ItemType);
        Assert.Equal((short)1, platinum.Stack);
        Assert.Empty(fixture.Events.EquipmentUpdates);
    }

    [Fact]
    public void Vendor_archetype_must_match_the_published_shop_catalog()
    {
        Fixture fixture = new(shopArchetypeId: new GameplayArchetypeId("test:other-vendor"));
        fixture.SetInventory(50, 1, VanillaCoinFacts.CopperCoin);
        NpcShopPurchaseRequest request = fixture.Request(price: 1);

        Assert.Equal(NpcShopPurchaseResult.VendorShopMismatch, fixture.Executor.TryPurchase(fixture.Buyer, in request));
        Assert.Empty(fixture.Events.EquipmentUpdates);
    }

    [Fact]
    public void Multi_item_offer_is_rejected_until_item_max_stack_catalog_exists()
    {
        Fixture fixture = new(offerStack: 2);
        fixture.SetInventory(50, 1, VanillaCoinFacts.CopperCoin);
        NpcShopPurchaseRequest request = fixture.Request(price: 1);

        Assert.Equal(NpcShopPurchaseResult.UnsupportedQuantity, fixture.Executor.TryPurchase(fixture.Buyer, in request));
        Assert.Empty(fixture.Events.EquipmentUpdates);
    }

    [Fact]
    public void Stale_buyer_generation_cannot_spend_current_players_inventory()
    {
        Fixture fixture = new();
        fixture.SetInventory(50, 1, VanillaCoinFacts.CopperCoin);
        ConnectionHandle stale = new(
            GameCommandSourceId.FromConnection(9002),
            new PlayerHandle(fixture.Buyer.Player.Slot, new PlayerSessionGeneration(2)));
        NpcShopPurchaseRequest request = fixture.Request(price: 1) with { Buyer = stale.Player };

        Assert.Equal(NpcShopPurchaseResult.InvalidBuyer, fixture.Executor.TryPurchase(stale, in request));
        Assert.True(fixture.Inventory.TryGet(fixture.Buyer, 50, out RuntimePlayerInventoryItem coins));
        Assert.Equal(VanillaCoinFacts.CopperCoin, coins.ItemType);
        Assert.Empty(fixture.Events.EquipmentUpdates);
    }

    private sealed class Fixture
    {
        private readonly ShopId shopId = new("test:shop");
        private readonly ShopOfferId offerId = new("test:dirt");

        public Fixture(GameplayArchetypeId? shopArchetypeId = null, short offerStack = 1)
        {
            Buyer = new ConnectionHandle(
                GameCommandSourceId.FromConnection(9001),
                new PlayerHandle(new PlayerSlotId(0), new PlayerSessionGeneration(1)));
            Inventory = new RuntimePlayerInventoryStore();
            Assert.True(Inventory.TryAttach(Buyer));

            NpcIdentities = new RuntimeNpcArchetypeIdentityStore(capacity: 4);
            Npcs = new RuntimeNpcStore(capacity: 4, commitSink: NpcIdentities);
            var archetypes = new RuntimeNpcArchetypeRegistry();
            VendorArchetypeId = new GameplayArchetypeId("test:vendor");
            Assert.Equal(
                GameplayArchetypeRegistrationResult.Registered,
                archetypes.TryRegister(
                    new NpcArchetypeDescriptor(VendorArchetypeId, VanillaNpcIds.Zombie),
                    out _));
            archetypes.CommitPending();
            var spawner = new RuntimeNpcArchetypeSpawner(Npcs, archetypes, NpcIdentities);
            var spawn = new NpcArchetypeSpawnRequest(VendorArchetypeId, Slot: 0, PositionX: 100f, PositionY: 100f);
            Assert.True(spawner.TrySpawn(in spawn, out Vendor));

            Shops = new RuntimeNpcShopCatalogRegistry();
            GameplayArchetypeId catalogArchetype = shopArchetypeId ?? VendorArchetypeId;
            var catalog = new NpcShopCatalog(
                shopId,
                catalogArchetype,
                [new ShopOffer(offerId, VanillaItemIds.DirtBlock, offerStack, UnitPrice: 1)]);
            Assert.Equal(NpcShopRegistrationResult.Registered, Shops.TryRegister(catalog, out _));
            Assert.True(Shops.CommitPending());

            Events = new RecordingPlayerEventSink();
            Executor = new RuntimeNpcShopPurchaseExecutor(Npcs, NpcIdentities, Shops, Inventory, Events);
        }

        public ConnectionHandle Buyer { get; }
        public RuntimePlayerInventoryStore Inventory { get; }
        public RuntimeNpcStore Npcs { get; }
        public RuntimeNpcArchetypeIdentityStore NpcIdentities { get; }
        public RuntimeNpcShopCatalogRegistry Shops { get; }
        public GameplayArchetypeId VendorArchetypeId { get; }
        public NpcSnapshot Vendor { get; }
        public RecordingPlayerEventSink Events { get; }
        public RuntimeNpcShopPurchaseExecutor Executor { get; }

        public void SetInventory(short slot, short stack, ItemTypeId itemType)
        {
            var request = new PlayerEquipmentCommitRequest(
                Buyer.Player.Slot,
                slot,
                stack,
                Prefix: 0,
                ItemNetId: checked((short)itemType.Value),
                ItemFlags: 0);
            Assert.True(Inventory.TrySet(Buyer, in request));
        }

        public NpcShopPurchaseRequest Request(long price)
        {
            NpcShopCatalog catalog = Assert.IsType<NpcShopCatalog>(GetCatalog());
            ShopOffer existing = Assert.Single(catalog.Offers.ToArray());
            if (existing.UnitPrice != price)
            {
                var replacement = new NpcShopCatalog(
                    shopId,
                    catalog.NpcArchetypeId,
                    [existing with { UnitPrice = price }]);
                Assert.True(Shops.Snapshot.TryGetById(shopId, out _));
                // Tests mutate catalog policy only through a fresh registry publication, mirroring provider updates.
                RuntimeNpcShopCatalogRegistry replacementRegistry = new();
                Assert.Equal(NpcShopRegistrationResult.Registered, replacementRegistry.TryRegister(replacement, out _));
                replacementRegistry.CommitPending();
                return new NpcShopPurchaseRequest(Buyer.Player, Vendor.Handle, shopId, offerId);
            }

            return new NpcShopPurchaseRequest(Buyer.Player, Vendor.Handle, shopId, offerId);
        }

        private NpcShopCatalog GetCatalog()
        {
            Assert.True(Shops.Snapshot.TryGetById(shopId, out NpcShopCatalog? catalog));
            return catalog;
        }
    }

    private sealed class RecordingPlayerEventSink : IRuntimePlayerEventSink
    {
        public List<PlayerEquipmentCommitRequest> EquipmentUpdates { get; } = [];

        public void PlayerAppearanceUpdated(ConnectionHandle connection, in PlayerAppearanceCommitRequest request)
        {
        }

        public void PlayerEquipmentUpdated(ConnectionHandle connection, in PlayerEquipmentCommitRequest request) =>
            EquipmentUpdates.Add(request);

        public void PlayerSpawned(ConnectionHandle connection, in PlayerSpawnCommitRequest request)
        {
        }

        public void PlayerMoved(ConnectionHandle connection, in PlayerMovementCommitRequest request)
        {
        }

        public void PlayerDisconnected(ConnectionHandle connection)
        {
        }
    }
}
