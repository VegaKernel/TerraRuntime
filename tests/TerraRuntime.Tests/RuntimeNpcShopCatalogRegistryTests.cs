using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.HostContracts;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcShopCatalogRegistryTests
{
    [Fact]
    public void Registration_is_invisible_until_safe_boundary_and_indexes_by_archetype_and_shop()
    {
        var registry = new RuntimeNpcShopCatalogRegistry();
        NpcShopCatalog catalog = CreateCatalog("test:merchant", "worldslicer:merchant", price: 25);

        Assert.Equal(
            NpcShopRegistrationResult.Registered,
            registry.TryRegister(catalog, out NpcShopRegistrationLease? lease));
        Assert.NotNull(lease);
        Assert.Equal(0UL, registry.Snapshot.Revision);
        Assert.False(registry.Snapshot.TryGetById(catalog.Id, out _));
        Assert.False(registry.Snapshot.TryGetByArchetype(catalog.NpcArchetypeId, out _));

        Assert.True(registry.CommitPending());

        Assert.Equal(1UL, registry.Snapshot.Revision);
        Assert.True(registry.Snapshot.TryGetById(catalog.Id, out NpcShopCatalog byId));
        Assert.Same(catalog, byId);
        Assert.True(registry.Snapshot.TryGetByArchetype(catalog.NpcArchetypeId, out NpcShopCatalog byArchetype));
        Assert.Same(catalog, byArchetype);
    }

    [Fact]
    public void One_archetype_cannot_publish_two_competing_shops()
    {
        var registry = new RuntimeNpcShopCatalogRegistry();
        GameplayArchetypeId archetype = new("worldslicer:merchant");
        NpcShopCatalog first = CreateCatalog("test:first", archetype.Value, price: 10);
        NpcShopCatalog second = CreateCatalog("test:second", archetype.Value, price: 20);

        Assert.Equal(NpcShopRegistrationResult.Registered, registry.TryRegister(first, out _));
        Assert.Equal(NpcShopRegistrationResult.ArchetypeAlreadyHasShop, registry.TryRegister(second, out _));
    }

    [Fact]
    public void Lease_can_replace_catalog_atomically_without_changing_shop_identity()
    {
        var registry = new RuntimeNpcShopCatalogRegistry();
        NpcShopCatalog initial = CreateCatalog("test:merchant", "worldslicer:merchant", price: 25);
        Assert.Equal(
            NpcShopRegistrationResult.Registered,
            registry.TryRegister(initial, out NpcShopRegistrationLease? lease));
        registry.CommitPending();

        NpcShopCatalog replacement = CreateCatalog("test:merchant", "worldslicer:merchant", price: 40);
        Assert.True(lease!.TryReplaceCatalog(replacement));
        Assert.True(registry.Snapshot.TryGetById(initial.Id, out NpcShopCatalog beforeCommit));
        Assert.Same(initial, beforeCommit);

        Assert.True(registry.CommitPending());

        Assert.True(registry.Snapshot.TryGetById(initial.Id, out NpcShopCatalog published));
        Assert.Same(replacement, published);
        Assert.True(published.TryGetOffer(new ShopOfferId("dirt"), out ShopOffer offer));
        Assert.Equal(40, offer.UnitPrice);
    }

    [Fact]
    public void Disposing_lease_retires_shop_only_after_next_commit()
    {
        var registry = new RuntimeNpcShopCatalogRegistry();
        NpcShopCatalog catalog = CreateCatalog("test:merchant", "worldslicer:merchant", price: 25);
        Assert.Equal(
            NpcShopRegistrationResult.Registered,
            registry.TryRegister(catalog, out NpcShopRegistrationLease? lease));
        registry.CommitPending();

        lease!.Dispose();
        Assert.True(registry.Snapshot.TryGetById(catalog.Id, out _));

        Assert.True(registry.CommitPending());
        Assert.False(registry.Snapshot.TryGetById(catalog.Id, out _));
        Assert.False(registry.Snapshot.TryGetByArchetype(catalog.NpcArchetypeId, out _));
    }

    [Fact]
    public void Host_registration_publishes_and_retires_only_on_authoritative_ticks()
    {
        var state = new ServerRuntimeState();
        var operations = new RuntimeNpcShopOperations(state.NpcShops);
        NpcShopCatalog catalog = CreateCatalog("test:host-merchant", "test:host-archetype", price: 25);

        Assert.Equal(
            NpcShopRegistrationStatus.Registered,
            operations.TryRegister(catalog, out INpcShopRegistration? registration));
        Assert.NotNull(registration);
        Assert.False(state.NpcShops.Snapshot.TryGetById(catalog.Id, out _));

        state.Tick();

        Assert.True(state.NpcShops.Snapshot.TryGetById(catalog.Id, out NpcShopCatalog published));
        Assert.Same(catalog, published);

        registration!.Dispose();
        Assert.True(state.NpcShops.Snapshot.TryGetById(catalog.Id, out _));

        state.Tick();

        Assert.False(state.NpcShops.Snapshot.TryGetById(catalog.Id, out _));
    }

    [Fact]
    public void Catalog_defensively_copies_offers_and_rejects_duplicate_offer_identity()
    {
        GameplayArchetypeId archetype = new("worldslicer:merchant");
        ShopOffer original = CreateOffer("dirt", price: 25);
        ShopOffer[] source = [original];
        var catalog = new NpcShopCatalog(new ShopId("test:merchant"), archetype, source);

        source[0] = CreateOffer("changed", price: 999);

        Assert.True(catalog.TryGetOffer(original.Id, out ShopOffer retained));
        Assert.Equal(25, retained.UnitPrice);
        Assert.False(catalog.TryGetOffer(new ShopOfferId("changed"), out _));

        Assert.Throws<ArgumentException>(() =>
            new NpcShopCatalog(
                new ShopId("test:duplicate"),
                archetype,
                [CreateOffer("same", 1), CreateOffer("same", 2)]));
    }

    [Fact]
    public void Offer_rejects_none_or_out_of_range_item_types()
    {
        var none = new ShopOffer(
            new ShopOfferId("none"),
            VanillaItemIds.None,
            Stack: 1,
            UnitPrice: 0);
        var invalid = new ShopOffer(
            new ShopOfferId("invalid"),
            new ItemTypeId(VanillaItemIds.Count),
            Stack: 1,
            UnitPrice: 0);

        Assert.False(none.IsValid);
        Assert.False(invalid.IsValid);
    }

    private static NpcShopCatalog CreateCatalog(string shopId, string archetypeId, long price) =>
        new(
            new ShopId(shopId),
            new GameplayArchetypeId(archetypeId),
            [CreateOffer("dirt", price)]);

    private static ShopOffer CreateOffer(string id, long price) =>
        new(
            new ShopOfferId(id),
            VanillaItemIds.DirtBlock,
            Stack: 1,
            UnitPrice: price);
}
