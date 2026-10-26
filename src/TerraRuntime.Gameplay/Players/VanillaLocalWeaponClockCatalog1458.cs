using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;
namespace TerraRuntime.Gameplay.Players;

/// <summary>Only the41 source-captured weapon clock families; launch/combat/payment authority stays with existing owners.</summary>
public static class VanillaLocalWeaponClockCatalog1458
{
    public static bool TryResolve(ItemTypeId item, PrefixId prefix, bool windows,
        out LocalItemClockDefinition1458 definition)
    {
        if (!TryGet(item,out definition) || !VanillaItemPrefixTable1458.TryResolveSpeedMultiplier(item,prefix,windows,out float speed)) return false;
        int useTime=(int)Math.Round(windows?definition.UseTime*(double)speed:definition.UseTime*speed);
        int animation=(int)Math.Round(windows?definition.UseAnimation*(double)speed:definition.UseAnimation*speed);
        if(useTime<=0 || animation<=0) return false;
        definition=definition with {UseTime=useTime,UseAnimation=animation};
        return true;
    }

    public static bool TryGet(ItemTypeId item,out LocalItemClockDefinition1458 definition)
    {
        definition = item.Value switch
        {
            4 => new(20, 20, 1, false, false, 0) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            24 => new(20, 20, 1, false, false, 0) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            39 => new(30, 30, 5, false, false, 1) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            98 => new(8, 8, 5, true, false, 10) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = true },
            99 => new(28, 28, 5, false, false, 1) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            113 => new(22, 22, 1, false, true, 16) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            218 => new(30, 30, 1, false, true, 34) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            219 => new(14, 14, 5, false, false, 14) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            482 => new(21, 21, 1, true, false, 0) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            483 => new(19, 19, 1, true, false, 0) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            484 => new(20, 20, 1, true, false, 0) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            495 => new(25, 25, 1, false, true, 79) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            533 => new(7, 7, 5, true, false, 10) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = true },
            534 => new(45, 45, 5, false, false, 10) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            653 => new(19, 19, 1, false, false, 0) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            656 => new(19, 19, 1, false, false, 0) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            659 => new(15, 15, 1, true, false, 0) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            679 => new(34, 34, 5, true, false, 10) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = true },
            921 => new(19, 19, 1, false, false, 0) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            964 => new(40, 40, 5, false, false, 10) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            1185 => new(22, 22, 1, true, false, 0) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            1192 => new(22, 22, 1, true, false, 0) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            1199 => new(20, 20, 1, true, false, 0) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            1929 => new(4, 4, 5, true, false, 10) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = true },
            2517 => new(19, 19, 1, false, false, 0) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            2745 => new(20, 20, 1, false, false, 0) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            3480 => new(25, 25, 5, false, false, 1) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            3484 => new(17, 17, 1, false, false, 0) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            3486 => new(26, 26, 5, false, false, 1) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            3490 => new(19, 19, 1, false, false, 0) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            3492 => new(27, 27, 5, false, false, 1) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            3496 => new(20, 20, 1, false, false, 0) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            3498 => new(28, 28, 5, false, false, 1) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            3502 => new(20, 20, 1, false, false, 0) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            3504 => new(29, 29, 5, false, false, 1) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            3508 => new(20, 21, 1, false, false, 0) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            3510 => new(27, 27, 5, false, false, 1) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            3514 => new(20, 20, 1, false, false, 0) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            3516 => new(26, 26, 5, false, false, 1) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            3520 => new(18, 18, 1, false, false, 0) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            4703 => new(55, 55, 5, false, false, 14) { ShootsEveryUse = false, ReuseDelay = 0, Style5AnimationBonus = false },
            _ => default
        };
        return definition.UseTime > 0;
    }
}
