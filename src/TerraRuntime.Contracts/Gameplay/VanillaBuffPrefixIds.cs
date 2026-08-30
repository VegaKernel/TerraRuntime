namespace TerraRuntime.Contracts.Gameplay;

/// <summary>
/// Version-pinned TerrariaServer 1.4.5.8 buff identities currently named by runtime metadata. The complete valid
/// identity range is catalogued; named members grow with authoritative behavior rather than copying game text.
/// </summary>
public static class VanillaBuffIds
{
    public const int Count = 401;

    public static readonly BuffTypeId None = new(0);
    public static readonly BuffTypeId ObsidianSkin = new(1);
    public static readonly BuffTypeId Regeneration = new(2);
    public static readonly BuffTypeId Swiftness = new(3);
    public static readonly BuffTypeId Gills = new(4);
    public static readonly BuffTypeId Ironskin = new(5);
    public static readonly BuffTypeId ManaRegeneration = new(6);
    public static readonly BuffTypeId MagicPower = new(7);
    public static readonly BuffTypeId Featherfall = new(8);
    public static readonly BuffTypeId Spelunker = new(9);
    public static readonly BuffTypeId Invisibility = new(10);
    public static readonly BuffTypeId Shine = new(11);
    public static readonly BuffTypeId NightOwl = new(12);
    public static readonly BuffTypeId Battle = new(13);
    public static readonly BuffTypeId Thorns = new(14);
    public static readonly BuffTypeId WaterWalking = new(15);
    public static readonly BuffTypeId Archery = new(16);
    public static readonly BuffTypeId Hunter = new(17);
    public static readonly BuffTypeId Gravitation = new(18);
    public static readonly BuffTypeId Poisoned = new(20);
    public static readonly BuffTypeId PotionSickness = new(21);
    public static readonly BuffTypeId Darkness = new(22);
    public static readonly BuffTypeId Cursed = new(23);
    public static readonly BuffTypeId OnFire = new(24);
    public static readonly BuffTypeId Tipsy = new(25);
    public static readonly BuffTypeId WellFed = new(26);
    public static readonly BuffTypeId Bleeding = new(30);
    public static readonly BuffTypeId Confused = new(31);
    public static readonly BuffTypeId Slow = new(32);
    public static readonly BuffTypeId Weak = new(33);
    public static readonly BuffTypeId Silenced = new(35);
    public static readonly BuffTypeId BrokenArmor = new(36);
    public static readonly BuffTypeId CursedInferno = new(39);
    public static readonly BuffTypeId Frostburn = new(44);
    public static readonly BuffTypeId Chilled = new(46);
    public static readonly BuffTypeId Frozen = new(47);
    public static readonly BuffTypeId Ichor = new(69);
    public static readonly BuffTypeId Venom = new(70);
    public static readonly BuffTypeId WeaponImbueVenom = new(71);
    public static readonly BuffTypeId Midas = new(72);
    public static readonly BuffTypeId WeaponImbueCursedFlames = new(73);
    public static readonly BuffTypeId WeaponImbueFire = new(74);
    public static readonly BuffTypeId WeaponImbueGold = new(75);
    public static readonly BuffTypeId WeaponImbueIchor = new(76);
    public static readonly BuffTypeId WeaponImbueNanites = new(77);
    public static readonly BuffTypeId WeaponImbueConfetti = new(78);
    public static readonly BuffTypeId WeaponImbuePoison = new(79);
    public static readonly BuffTypeId Blackout = new(80);
    public static readonly BuffTypeId WellFed2 = new(206);
    public static readonly BuffTypeId WellFed3 = new(207);
    public static readonly BuffTypeId OnFire3 = new(323);
    public static readonly BuffTypeId Frostburn2 = new(324);
    public static readonly BuffTypeId NeutralHunger = new(332);
    public static readonly BuffTypeId Hunger = new(333);
    public static readonly BuffTypeId Starving = new(334);
    public static readonly BuffTypeId Shimmer = new(353);
    public static readonly BuffTypeId Hemorrhage = new(375);
    public static readonly BuffTypeId PotentAcid = new(395);
    public static readonly BuffTypeId ManaHeat = new(396);
    public static readonly BuffTypeId AcceleratePoisons = new(398);

    public static bool TryCreate(int rawType, out BuffTypeId type)
    {
        if ((uint)rawType >= Count)
        {
            type = default;
            return false;
        }

        type = new BuffTypeId(rawType);
        return true;
    }
}

/// <summary>Version-pinned TerrariaServer 1.4.5.8 prefix identities consumed by runtime prefix rules.</summary>
public static class VanillaPrefixIds
{
    public const int Count = 98;

    public static readonly PrefixId None = new(0);
    public static readonly PrefixId Tiny = new(7);
    public static readonly PrefixId Terrible = new(8);
    public static readonly PrefixId Small = new(9);
    public static readonly PrefixId Dull = new(10);
    public static readonly PrefixId Unhappy = new(11);
    public static readonly PrefixId Awful = new(22);
    public static readonly PrefixId Lethargic = new(23);
    public static readonly PrefixId Awkward = new(24);
    public static readonly PrefixId Inept = new(29);
    public static readonly PrefixId Ignorant = new(30);
    public static readonly PrefixId Deranged = new(31);
    public static readonly PrefixId Forceful = new(38);
    public static readonly PrefixId Broken = new(39);
    public static readonly PrefixId Damaged = new(40);
    public static readonly PrefixId Shoddy = new(41);
    public static readonly PrefixId Slow = new(47);
    public static readonly PrefixId Sluggish = new(48);
    public static readonly PrefixId Lazy = new(49);
    public static readonly PrefixId Hurtful = new(53);
    public static readonly PrefixId Strong = new(54);
    public static readonly PrefixId Unpleasant = new(55);
    public static readonly PrefixId Weak = new(56);
    public static readonly PrefixId Ruthless = new(57);
    public static readonly PrefixId Fabled = new(85);
    public static readonly PrefixId Loyal = new(86);
    public static readonly PrefixId Worthy = new(87);
    public static readonly PrefixId Focused = new(88);
    public static readonly PrefixId Patient = new(89);
    public static readonly PrefixId Rabid = new(90);
    public static readonly PrefixId IllTempered = new(91);
    public static readonly PrefixId Petty = new(92);
    public static readonly PrefixId Feeble = new(93);
    public static readonly PrefixId Skittish = new(94);
    public static readonly PrefixId Eager = new(95);
    public static readonly PrefixId Ballistic = new(96);
    public static readonly PrefixId Scraggling = new(97);

    public static bool TryCreate(int rawType, out PrefixId type)
    {
        if ((uint)rawType >= Count)
        {
            type = default;
            return false;
        }

        type = new PrefixId(rawType);
        return true;
    }
}
