using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Application.Bots;

/// <summary>
/// Curated source-backed fake-player presets. Functional armor deliberately avoids complete dynamic endgame sets;
/// the matching complete set is placed in vanity slots so appearance stays coherent without inventing transient
/// Solar/Vortex/Shroomite/Beetle set-bonus state.
/// </summary>
internal readonly record struct RuntimePlayerBotLoadout1458(
    string Name,
    ItemTypeId MeleeWeapon,
    ItemTypeId BowWeapon,
    ItemTypeId GunWeapon,
    ItemTypeId ArrowAmmo,
    ItemTypeId BulletAmmo,
    ItemTypeId FunctionalHead,
    ItemTypeId FunctionalBody,
    ItemTypeId FunctionalLegs,
    ItemTypeId VanityHead,
    ItemTypeId VanityBody,
    ItemTypeId VanityLegs,
    ItemTypeId Accessory1,
    ItemTypeId Accessory2,
    ItemTypeId Accessory3,
    ItemTypeId Accessory4,
    ItemTypeId Accessory5);

internal static class RuntimePlayerBotLoadoutCatalog1458
{
    private static readonly RuntimePlayerBotLoadout1458[] Presets =
    [
        new(
            "Solar Vanguard",
            VanillaItemIds.Muramasa, VanillaItemIds.PlatinumBow, VanillaItemIds.Handgun,
            VanillaItemIds.UnholyArrow, VanillaItemIds.SilverBullet,
            VanillaItemIds.SolarFlareHelmet, VanillaItemIds.BeetleShell, VanillaItemIds.SolarFlareLeggings,
            VanillaItemIds.SolarFlareHelmet, VanillaItemIds.SolarFlareBreastplate, VanillaItemIds.SolarFlareLeggings,
            VanillaItemIds.FishronWings, VanillaItemIds.CelestialShell, VanillaItemIds.DestroyerEmblem,
            VanillaItemIds.AnkhShield, VanillaItemIds.WarriorEmblem),
        new(
            "Vortex Ranger",
            VanillaItemIds.Muramasa, VanillaItemIds.PlatinumBow, VanillaItemIds.Minishark,
            VanillaItemIds.UnholyArrow, VanillaItemIds.SilverBullet,
            VanillaItemIds.VortexHelmet, VanillaItemIds.ShroomiteBreastplate, VanillaItemIds.VortexLeggings,
            VanillaItemIds.VortexHelmet, VanillaItemIds.VortexBreastplate, VanillaItemIds.VortexLeggings,
            VanillaItemIds.FishronWings, VanillaItemIds.CelestialShell, VanillaItemIds.DestroyerEmblem,
            VanillaItemIds.RangerEmblem, VanillaItemIds.SniperScope),
        new(
            "Shroomite Scout",
            VanillaItemIds.Muramasa, VanillaItemIds.PlatinumBow, VanillaItemIds.Revolver,
            VanillaItemIds.UnholyArrow, VanillaItemIds.SilverBullet,
            VanillaItemIds.ShroomiteMask, VanillaItemIds.VortexBreastplate, VanillaItemIds.ShroomiteLeggings,
            VanillaItemIds.ShroomiteMask, VanillaItemIds.ShroomiteBreastplate, VanillaItemIds.ShroomiteLeggings,
            VanillaItemIds.FishronWings, VanillaItemIds.CelestialShell, VanillaItemIds.DestroyerEmblem,
            VanillaItemIds.RangerEmblem, VanillaItemIds.MagicQuiver),
        new(
            "Beetle Bruiser",
            VanillaItemIds.Muramasa, VanillaItemIds.PlatinumBow, VanillaItemIds.Musket,
            VanillaItemIds.UnholyArrow, VanillaItemIds.SilverBullet,
            VanillaItemIds.BeetleHelmet, VanillaItemIds.SolarFlareBreastplate, VanillaItemIds.BeetleLeggings,
            VanillaItemIds.BeetleHelmet, VanillaItemIds.BeetleShell, VanillaItemIds.BeetleLeggings,
            VanillaItemIds.FishronWings, VanillaItemIds.CelestialShell, VanillaItemIds.DestroyerEmblem,
            VanillaItemIds.AnkhShield, VanillaItemIds.WarriorEmblem)
    ];

    public static RuntimePlayerBotLoadout1458 Pick(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return Presets[random.Next(Presets.Length)];
    }
}
