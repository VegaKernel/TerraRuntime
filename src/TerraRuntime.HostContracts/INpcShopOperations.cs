using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.HostContracts;

public enum NpcShopRegistrationStatus : byte
{
    Registered = 0,
    InvalidCatalog = 1,
    DuplicateShopId = 2,
    ArchetypeAlreadyHasShop = 3,
    RuntimeDetached = 4
}

/// <summary>A runtime-scoped registration for one immutable NPC shop catalog.</summary>
public interface INpcShopRegistration : IDisposable
{
    ShopId ShopId { get; }
    GameplayArchetypeId NpcArchetypeId { get; }

    bool TryReplaceCatalog(NpcShopCatalog catalog);
}

/// <summary>
/// Semantic NPC-shop registration surface for trusted host modules. Changes become visible at an authoritative
/// game-loop tick boundary, and every registration is retired automatically when its runtime scope detaches.
/// </summary>
public interface INpcShopOperations
{
    NpcShopRegistrationStatus TryRegister(
        NpcShopCatalog catalog,
        out INpcShopRegistration? registration);
}
