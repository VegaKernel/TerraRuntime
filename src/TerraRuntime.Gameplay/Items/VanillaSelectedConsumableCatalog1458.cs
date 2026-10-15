using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

public readonly record struct SelectedConsumableDefinition1458(
    ItemTypeId Type, int HealLife, int HealMana, bool HealingDelay, int UseTime,
    int UseAnimation, int DrinkColourCount, int BuffType = 0, int BuffTime = 0)
{
    public int FoodColourCount { get; init; }
}

public static class VanillaSelectedConsumableCatalog1458
{
    public static bool TryGet(ItemTypeId type, out SelectedConsumableDefinition1458 definition)
    {
        if (type == VanillaItemIds.None)
        {
            definition = new(type, 0, 0, false, 0, 0, 0);
            return true;
        }
        if (type.Value is 29 or 109 or 1291)
        {
            definition = new(type, 0, 0, false, 30, 30, 0);
            return true;
        }
        if (type.Value is 3335 or 5043)
        {
            definition = new(type, 0, 0, false, 30, 30, 0);
            return true;
        }
        if (type.Value == 5326 || type.Value is >= 5337 and <= 5342)
        {
            int useDuration = type.Value == 5326 ? 17 : 45;
            definition = new(type, 0, 0, false, useDuration, useDuration, 0);
            return true;
        }
        if (TryGetFood(type, out definition)) return true;
        (int buff, int duration) = type.Value switch
        {
            292 => (5, 28_800),
            303 => (16, 28_800),
            2344 => (112, 28_800),
            2346 => (114, 14_400),
            2347 => (115, 14_400),
            2349 => (117, 14_400),
            _ => (0, 0)
        };
        if (buff > 0)
        {
            definition = new(type, 0, 0, false, 17, 17, 3, buff, duration);
            return true;
        }
        // These ordinary SetDefaults families share useStyle9, neutral prefix, no mana cost and three drink colours.
        (int life, int mana) = type.Value switch
        {
            28 => (50, 0),
            188 => (100, 0),
            499 => (150, 0),
            3544 => (200, 0),
            110 => (0, 50),
            189 => (0, 100),
            500 => (0, 200),
            2209 => (0, 400),
            _ => (0, 0)
        };
        definition = new(type, life, mana, life > 0, 17, 17, 3);
        return life > 0 || mana > 0;
    }
    // Original SetDefaults roster: all82 retained food identities share ordinary17-tick
    // consumable clocks, no healing/mana/shoot/place path, with separately captured particles.
    private static bool TryGetFood(ItemTypeId type, out SelectedConsumableDefinition1458 definition)
    {
        (int buff, int duration, int foodColours, int drinkColours) = type.Value switch
        {
            357 => (206, 28_800, 2, 0),
            967 => (26, 3_600, 0, 2),
            969 => (26, 36_000, 0, 3),
            1787 => (26, 28_800, 3, 0),
            1911 => (207, 14_400, 4, 0),
            1919 => (207, 14_400, 4, 0),
            1920 => (207, 14_400, 4, 0),
            2267 => (206, 28_800, 3, 0),
            2268 => (206, 36_000, 3, 0),
            2425 => (26, 28_800, 4, 0),
            2426 => (206, 36_000, 2, 0),
            2427 => (206, 28_800, 3, 0),
            3195 => (206, 50_400, 4, 0),
            3532 => (207, 86_400, 2, 0),
            4009 => (26, 18_000, 2, 0),
            4011 => (207, 57_600, 2, 0),
            4012 => (206, 36_000, 4, 0),
            4013 => (207, 86_400, 2, 0),
            4014 => (26, 36_000, 3, 0),
            4015 => (207, 28_800, 4, 0),
            4016 => (206, 50_400, 2, 0),
            4017 => (206, 72_000, 3, 0),
            4018 => (206, 57_600, 0, 2),
            4019 => (206, 36_000, 3, 0),
            4020 => (206, 50_400, 2, 0),
            4021 => (206, 36_000, 3, 0),
            4022 => (207, 172_800, 3, 0),
            4023 => (206, 57_600, 2, 0),
            4024 => (26, 36_000, 2, 0),
            4025 => (207, 43_200, 3, 0),
            4026 => (206, 50_400, 4, 0),
            4027 => (207, 57_600, 0, 4),
            4028 => (206, 57_600, 3, 0),
            4029 => (207, 28_800, 4, 0),
            4030 => (26, 90_000, 3, 0),
            4031 => (26, 36_000, 3, 0),
            4032 => (206, 36_000, 3, 0),
            4033 => (26, 36_000, 3, 0),
            4034 => (206, 50_400, 4, 0),
            4035 => (206, 64_800, 4, 0),
            4036 => (207, 28_800, 4, 0),
            4037 => (207, 57_600, 3, 0),
            4282 => (26, 18_000, 2, 0),
            4283 => (26, 18_000, 3, 0),
            4284 => (26, 18_000, 3, 0),
            4285 => (26, 18_000, 3, 0),
            4286 => (26, 18_000, 2, 0),
            4287 => (26, 18_000, 3, 0),
            4288 => (206, 18_000, 4, 0),
            4289 => (26, 18_000, 3, 0),
            4290 => (26, 18_000, 3, 0),
            4291 => (26, 18_000, 2, 0),
            4292 => (26, 18_000, 3, 0),
            4293 => (26, 18_000, 2, 0),
            4294 => (26, 18_000, 2, 0),
            4295 => (26, 18_000, 3, 0),
            4296 => (26, 18_000, 3, 0),
            4297 => (206, 18_000, 2, 0),
            4403 => (206, 36_000, 4, 0),
            4411 => (26, 36_000, 2, 0),
            4614 => (26, 36_000, 0, 2),
            4615 => (207, 57_600, 0, 2),
            4616 => (26, 36_000, 0, 2),
            4617 => (26, 54_000, 0, 2),
            4618 => (26, 36_000, 0, 2),
            4619 => (26, 72_000, 0, 2),
            4620 => (26, 72_000, 0, 2),
            4621 => (26, 72_000, 0, 2),
            4622 => (26, 72_000, 0, 2),
            4623 => (206, 72_000, 0, 3),
            4624 => (26, 54_000, 0, 2),
            4625 => (26, 90_000, 3, 0),
            5009 => (26, 18_000, 0, 2),
            5041 => (26, 72_000, 0, 2),
            5042 => (206, 36_000, 0, 2),
            5092 => (206, 28_800, 3, 0),
            5093 => (206, 21_600, 6, 0),
            5275 => (26, 7_200, 0, 2),
            5277 => (26, 18_000, 3, 0),
            5278 => (26, 18_000, 3, 0),
            5537 => (26, 18_000, 3, 0),
            5645 => (26, 36_000, 6, 0),
            _ => (0, 0, 0, 0)
        };
        definition = new(type, 0, 0, false, 17, 17, drinkColours, buff, duration) { FoodColourCount = foodColours };
        return buff != 0;
    }

    /// <summary>Known SetDefaults families that may enter QuickBuff's food or non-summon buff path.
    /// Other retained families have no applicable buff; unsupported candidates must not be silently skipped.</summary>
    public static bool MayParticipateInQuickBuff(ItemTypeId type) => type.Value is
        115 or 288 or 289 or 290 or 291 or 292 or 293 or 294 or 295 or 296 or 297 or 298 or
        299 or 300 or 301 or 302 or 303 or 304 or 305 or 353 or 357 or 425 or 603 or 669 or
        753 or 967 or 969 or 994 or 1134 or 1169 or 1170 or 1171 or 1172 or 1180 or 1181 or 1182 or
        1183 or 1242 or 1311 or 1312 or 1340 or 1353 or 1354 or 1355 or 1356 or 1357 or 1358 or 1359 or
        1787 or 1798 or 1799 or 1810 or 1837 or 1911 or 1919 or 1920 or 1927 or 1959 or 2266 or 2267 or
        2268 or 2322 or 2323 or 2324 or 2325 or 2326 or 2327 or 2328 or 2329 or 2344 or 2345 or 2346 or
        2347 or 2348 or 2349 or 2354 or 2355 or 2356 or 2359 or 2420 or 2425 or 2426 or 2427 or 2587 or
        3043 or 3060 or 3062 or 3195 or 3453 or 3454 or 3455 or 3532 or 3577 or 3628 or 3855 or 3856 or
        3857 or 4009 or 4011 or 4012 or 4013 or 4014 or 4015 or 4016 or 4017 or 4018 or 4019 or 4020 or
        4021 or 4022 or 4023 or 4024 or 4025 or 4026 or 4027 or 4028 or 4029 or 4030 or 4031 or 4032 or
        4033 or 4034 or 4035 or 4036 or 4037 or 4282 or 4283 or 4284 or 4285 or 4286 or 4287 or 4288 or
        4289 or 4290 or 4291 or 4292 or 4293 or 4294 or 4295 or 4296 or 4297 or 4365 or 4366 or 4403 or
        4411 or 4425 or 4477 or 4478 or 4479 or 4550 or 4551 or 4603 or 4604 or 4605 or 4614 or 4615 or
        4616 or 4617 or 4618 or 4619 or 4620 or 4621 or 4622 or 4623 or 4624 or 4625 or 4701 or 4735 or
        4736 or 4737 or 4777 or 4797 or 4798 or 4799 or 4800 or 4801 or 4802 or 4803 or 4804 or 4805 or
        4806 or 4807 or 4808 or 4809 or 4810 or 4811 or 4812 or 4813 or 4814 or 4815 or 4816 or 4817 or
        4960 or 5009 or 5041 or 5042 or 5088 or 5089 or 5090 or 5091 or 5092 or 5093 or 5098 or 5131 or
        5211 or 5275 or 5276 or 5277 or 5278 or 5297 or 5332 or 5333 or 5400 or 5466 or 5513 or 5517 or
        5523 or 5537 or 5573 or 5645 or 5654;
}
