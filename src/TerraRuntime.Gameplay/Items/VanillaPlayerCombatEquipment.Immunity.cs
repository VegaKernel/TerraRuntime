using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>Only the ten source immunities granted by the Ankh Charm ingredient family.</summary>
[Flags]
public enum VanillaPlayerBuffImmunity1458 : ushort
{
    None = 0,
    Poisoned = 1,
    Darkness = 2,
    Cursed = 4,
    Bleeding = 8,
    Confused = 16,
    Slow = 32,
    Weak = 64,
    Silenced = 128,
    BrokenArmor = 256,
    Stoned = 512,
    All = 1023
}

public static partial class VanillaPlayerCombatEquipmentCatalog
{
    public static bool TryGetAccessoryImmunity(ItemTypeId item, out VanillaPlayerBuffImmunity1458 immunity)
    {
        immunity = item.Value switch
        {
            885 => VanillaPlayerBuffImmunity1458.Bleeding,
            886 => VanillaPlayerBuffImmunity1458.BrokenArmor,
            887 => VanillaPlayerBuffImmunity1458.Poisoned,
            888 => VanillaPlayerBuffImmunity1458.Darkness,
            889 => VanillaPlayerBuffImmunity1458.Slow,
            890 => VanillaPlayerBuffImmunity1458.Silenced,
            891 => VanillaPlayerBuffImmunity1458.Cursed,
            892 => VanillaPlayerBuffImmunity1458.Weak,
            893 => VanillaPlayerBuffImmunity1458.Confused,
            3781 => VanillaPlayerBuffImmunity1458.Stoned,
            901 => VanillaPlayerBuffImmunity1458.Weak | VanillaPlayerBuffImmunity1458.BrokenArmor,
            902 => VanillaPlayerBuffImmunity1458.Bleeding | VanillaPlayerBuffImmunity1458.Poisoned,
            903 => VanillaPlayerBuffImmunity1458.Slow | VanillaPlayerBuffImmunity1458.Confused,
            904 => VanillaPlayerBuffImmunity1458.Silenced | VanillaPlayerBuffImmunity1458.Cursed,
            5354 => VanillaPlayerBuffImmunity1458.Darkness | VanillaPlayerBuffImmunity1458.Stoned,
            1612 => VanillaPlayerBuffImmunity1458.All,
            _ => VanillaPlayerBuffImmunity1458.None
        };
        return immunity != VanillaPlayerBuffImmunity1458.None;
    }

    internal static VanillaPlayerBuffImmunity1458 ImmunityForBuff(BuffTypeId type) => type.Value switch
    {
        20 => VanillaPlayerBuffImmunity1458.Poisoned,
        22 => VanillaPlayerBuffImmunity1458.Darkness,
        23 => VanillaPlayerBuffImmunity1458.Cursed,
        30 => VanillaPlayerBuffImmunity1458.Bleeding,
        31 => VanillaPlayerBuffImmunity1458.Confused,
        32 => VanillaPlayerBuffImmunity1458.Slow,
        33 => VanillaPlayerBuffImmunity1458.Weak,
        35 => VanillaPlayerBuffImmunity1458.Silenced,
        36 => VanillaPlayerBuffImmunity1458.BrokenArmor,
        156 => VanillaPlayerBuffImmunity1458.Stoned,
        _ => VanillaPlayerBuffImmunity1458.None
    };
}
