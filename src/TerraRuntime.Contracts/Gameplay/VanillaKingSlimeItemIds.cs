namespace TerraRuntime.Contracts.Gameplay;

/// <summary>
/// TerrariaServer 1.4.5.8 item identities consumed by the source-backed King Slime loot slice.
/// Expert/Master identities are included only to document the verified boundary; normal world-item delivery
/// implemented by this slice never flattens treasure bags or per-player Master drops into ordinary drops.
/// </summary>
public static class VanillaKingSlimeItemIds
{
    public static readonly ItemTypeId NinjaHood = new(256);
    public static readonly ItemTypeId NinjaShirt = new(257);
    public static readonly ItemTypeId NinjaPants = new(258);
    public static readonly ItemTypeId Solidifier = new(998);
    public static readonly ItemTypeId SlimeStaff = new(1309);
    public static readonly ItemTypeId SlimySaddle = new(2430);
    public static readonly ItemTypeId KingSlimeTrophy = new(2489);
    public static readonly ItemTypeId KingSlimeMask = new(2493);
    public static readonly ItemTypeId SlimeHook = new(2585);
    public static readonly ItemTypeId SlimeGun = new(2610);
    public static readonly ItemTypeId KingSlimeBossBag = new(3318);
    public static readonly ItemTypeId KingSlimePetItem = new(4797);
    public static readonly ItemTypeId KingSlimeMasterTrophy = new(4929);
}
