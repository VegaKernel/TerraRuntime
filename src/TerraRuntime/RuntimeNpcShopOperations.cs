using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.HostContracts;

namespace TerraRuntime;

internal sealed class RuntimeNpcShopOperations(RuntimeNpcShopCatalogRegistry shops) : INpcShopOperations
{
    public NpcShopRegistrationStatus TryRegister(
        NpcShopCatalog catalog,
        out INpcShopRegistration? registration)
    {
        NpcShopRegistrationResult result = shops.TryRegister(catalog, out NpcShopRegistrationLease? lease);
        registration = lease is null ? null : new Registration(lease);
        return result switch
        {
            NpcShopRegistrationResult.Registered => NpcShopRegistrationStatus.Registered,
            NpcShopRegistrationResult.InvalidCatalog => NpcShopRegistrationStatus.InvalidCatalog,
            NpcShopRegistrationResult.DuplicateShopId => NpcShopRegistrationStatus.DuplicateShopId,
            NpcShopRegistrationResult.ArchetypeAlreadyHasShop => NpcShopRegistrationStatus.ArchetypeAlreadyHasShop,
            _ => throw new InvalidOperationException($"Unknown NPC shop registration result '{result}'.")
        };
    }

    private sealed class Registration(NpcShopRegistrationLease lease) : INpcShopRegistration
    {
        public ShopId ShopId => lease.ShopId;
        public GameplayArchetypeId NpcArchetypeId => lease.NpcArchetypeId;

        public bool TryReplaceCatalog(NpcShopCatalog catalog) => lease.TryReplaceCatalog(catalog);

        public void Dispose() => lease.Dispose();
    }
}
