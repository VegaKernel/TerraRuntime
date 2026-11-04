using System.Numerics;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// The loot half of TerrariaServer 1.4.5.8 <c>WorldGen.AddBuriedChest</c>: four depth-banded tables and the
/// two tails that run after whichever one was chosen.
/// </summary>
/// <remarks>
/// <para>
/// Most of the method's two hundred draws are here. The shape is the same in all four bands - a signature item,
/// then a run of independent gates each of which may add one stack - but the tables disagree about almost
/// everything else, and the gates are not interchangeable: the surface band rolls six-sided gates where the
/// cavern band rolls two-sided ones, and a gate that fires spends further values on its selector and its stack
/// in that order.
/// </para>
/// <para>
/// Two things here are easy to get wrong and expensive when wrong. The signature item usually takes a natural
/// prefix, which is a reroll loop on the shared stream rather than one value, so it is handed to
/// <see cref="VanillaItemPrefixRoll1458"/> rather than approximated. And the whole fill sits inside a
/// <c>while</c> that repeats while the chest is still empty, so a chest whose every gate refused is rolled
/// again from the top rather than left empty.
/// </para>
/// <para>
/// These shared tables consume the caller's canonical seed profile. Placement performs admission before
/// mutation. The original requested style remains separate from the resolved atlas style because remix's
/// cavern selection tests the caller's style even when the physical chest family changes.
/// </para>
/// </remarks>
internal static class BuriedChestLoot1458
{
    /// <summary>
    /// Fills a freshly placed chest. <paramref name="primary"/> is the source's <c>mainItemInChest</c> after
    /// the placement half has finished deciding it; zero lets the band choose its own.
    /// </summary>
    internal static WorldGenerationChestItem[] Fill(
        IWorldGenerationVanillaRandom random,
        BuriedChestContext1458 context,
        in BuriedChestKind1458 kind,
        int floorY,
        int style,
        int primary,
        ushort chestTileType,
        int requestedStyle,
        VanillaWorldSeedProfile1458 profile=default)
    {
        var chest = new Slots(random);
        bool rainbowGlowsticks=profile.Has(VanillaSpecialWorldSeed1458.CelebrationMk10)||profile.Has(VanillaSecretWorldSeed1458.RainbowRoad);
        bool remix=profile.Has(VanillaSpecialWorldSeed1458.Remix);
        while (chest.Count == 0)
        {
            bool aboveSurface = remix?floorY>=(context.RockLayer+(context.Height-350)*2)/3.0:floorY < context.WorldSurface + 25.0;
            if ((aboveSurface && (kind.Wooden || kind.LivingWood)) || kind.ForcedSurfaceItem)
                Surface(random, context, in kind, chest, primary,rainbowGlowsticks,profile);
            else if (!remix&&floorY<context.RockLayer||remix&&floorY>context.RockLayer&&floorY<context.Height-250)
                Underground(random, context, in kind, chest, primary,profile);
            else if (floorY < context.Height - 250 || remix&&requestedStyle is 7 or 14)
                Cavern(random, context, in kind, chest, primary, floorY,rainbowGlowsticks,profile);
            else
                Underworld(random, context, in kind, chest, primary,rainbowGlowsticks);

            if (chest.Count > 0 && chestTileType == 21)
                ContainerTail(random, in kind, chest, style,profile);
            if (chest.Count > 0 && chestTileType == 467)
                DesertContainerTail(random, in kind, chest, style);

            if(profile.Has(VanillaSecretWorldSeed1458.RainbowRoad)&&primary is 1156 or 1571 or 1569 or 1572 or 4607)
            {chest.Put(1260);chest.Prefix();chest.Advance();}
            bool vampire=profile.Has(VanillaSecretWorldSeed1458.WhatAHorribleNightToHaveACurse);
            if(vampire&&!kind.Dungeon&&!kind.Lihzahrd&&!kind.Skyware&&!kind.Hell&&random.Next(4)==0)chest.PutAndAdvance(5597);

            // One chest in twelve carries a voice-change item, and its identity is a fourteen-way draw even
            // though only fourteen of the results are distinct.
            if (random.Next(12) == 0)
            {
                chest.Put(VoiceItem(random));
                chest.Prefix();
                chest.Advance();
            }
            else if(vampire&&random.Next(12)==0)
            {
                chest.Put(random.Next(4) switch{1=>5505,2=>5506,3=>5508,_=>5509});
                chest.Prefix();chest.Advance();
            }
        }

        return chest.ToArray();
    }

    private static void ApplyExtraSignature(IWorldGenerationVanillaRandom random,Slots chest,VanillaWorldSeedProfile1458 profile,int vampireMatch)
    {
        if(profile.Has(VanillaSecretWorldSeed1458.WhatAHorribleNightToHaveACurse)&&random.Next(10)==vampireMatch)
        {chest.Put(random.Next(2)==0?1800:900);chest.Prefix();}
        if(profile.Has(VanillaSecretWorldSeed1458.IAmError)&&random.Next(3*((BitOperations.PopCount((ulong)profile.Secret)+3)/4))==0)
        {
            if(BitOperations.PopCount((ulong)profile.Secret)<6)
            {
                ReadOnlySpan<int> pool=[4008,238,2275,3352,3262,3334,4818,1325,4144,3350,4347,1309,1863,485,748,1825,1321,5451,3385,3386,3387,3388,4951,4420,3043,2341,2342,2800,3623,4980,4273,4711];
                chest.Put(pool[random.Next(32)]);
            }
            chest.Prefix();
        }
    }

    /// <summary>Source <c>Item.GetRandomVoiceItem</c>.</summary>
    private static int VoiceItem(IWorldGenerationVanillaRandom random) => random.Next(14) switch
    {
        1 => 5500, 2 => 5501, 3 => 5502, 4 => 5503, 5 => 5504, 6 => 5505, 7 => 5506,
        8 => 5507, 9 => 5508, 10 => 5509, 11 => 5484, 12 => 5485, 13 => 5534, _ => 5499
    };

    private static void Surface(
        IWorldGenerationVanillaRandom random,
        BuriedChestContext1458 context,
        in BuriedChestKind1458 kind,
        Slots chest,
        int primary,bool rainbowGlowsticks,VanillaWorldSeedProfile1458 profile)
    {
        if (primary > 0)
        {
            chest.Put(primary);
            chest.Prefix();
            chest.Advance();
            // Two signature items come with a fixed companion, added without a gate of its own.
            switch (primary)
            {
                case 848: chest.Put(866); chest.Advance(); break;
                case 832: chest.Put(933); chest.Advance(); break;
            }

            if (kind.LivingWood && random.Next(3) == 0)
                chest.PutAndAdvance(5629);
            if (kind.LivingWood && random.Next(6) == 0)
                chest.PutAndAdvance(random.Next(2) == 0 ? 4429 : 4427);
            if (kind.LivingWood && random.Next(3) != 0)
                chest.PutAndAdvance(5528);
            if(kind.ForcedSurfaceItem&&profile.Has(VanillaSpecialWorldSeed1458.CelebrationMk10))
            {chest.PutAndAdvance(848);chest.PutAndAdvance(866);}
        }
        else
        {
            int choice = random.Next(11);
            chest.Put(choice switch
            {
                0 => 280, 1 => 281, 2 => 284, 3 => 285, 4 => 953, 5 => 946,
                6 => 3068, 7 => profile.Has(VanillaSpecialWorldSeed1458.Remix)?517:3069, 8 => 3084, 9 => 4341, _ => 6165
            });
            chest.Prefix();
            chest.Advance();
        }

        if (random.Next(6) == 0)
            chest.PutStackAndAdvance(rainbowGlowsticks?5643:282, random.Next(40, 76));
        if (random.Next(6) == 0)
            chest.PutStackAndAdvance(279, random.Next(150, 301));
        if (random.Next(6) == 0)
            chest.PutStackAndAdvance(3093, Torches(random));
        if (random.Next(6) == 0)
            chest.PutStackAndAdvance(4345, Torches(random));
        if (random.Next(3) == 0)
            chest.PutStackAndAdvance(168, random.Next(3, 6));
        if (random.Next(2) == 0)
        {
            int bar = random.Next(2) == 0 ? context.CopperBar : context.IronBar;
            chest.PutStackAndAdvance(bar, random.Next(8) + 3);
        }
        if (random.Next(2) == 0)
            chest.PutStackAndAdvance(965, random.Next(50, 101));
        if (random.Next(3) != 0)
        {
            int rope = random.Next(2) == 0 ? 40 : 42;
            chest.PutStackAndAdvance(rope, random.Next(26) + 25);
        }
        if (random.Next(2) == 0)
            chest.PutStackAndAdvance(28, random.Next(3) + 3);
        if (random.Next(3) != 0)
            chest.PutStackAndAdvance(2350, random.Next(3, 6));
        if (random.Next(3) > 0)
        {
            int potion = random.Next(6) switch { 0 => 292, 1 => 298, 2 => 299, 3 => 290, 4 => 2322, _ => 2325 };
            chest.PutStackAndAdvance(potion, random.Next(1, 3));
        }
        if (random.Next(2) == 0)
        {
            int torch = random.Next(2) == 0 ? 8 : 31;
            chest.PutStackAndAdvance(torch, random.Next(11) + 10);
        }
        if (random.Next(2) == 0)
            chest.PutStackAndAdvance(72, random.Next(10, 30));
        if (random.Next(2) == 0)
            chest.PutStackAndAdvance(9, random.Next(50, 100));
    }

    /// <summary>
    /// The two surface torch stacks start at one and grow through two further gates, which is three values
    /// for a stack that is usually still one.
    /// </summary>
    private static int Torches(IWorldGenerationVanillaRandom random)
    {
        int stack = 1;
        if (random.Next(5) == 0)
            stack += random.Next(2);
        if (random.Next(10) == 0)
            stack += random.Next(3);
        return stack;
    }

    private static void Underground(
        IWorldGenerationVanillaRandom random,
        BuriedChestContext1458 context,
        in BuriedChestKind1458 kind,
        Slots chest,
        int primary,VanillaWorldSeedProfile1458 profile)
    {
        if (primary > 0)
        {
            // The one companion that goes in FIRST, before the item it belongs to.
            if (primary == 832)
            {
                chest.Put(933);
                chest.Advance();
            }

            chest.Put(primary);
            chest.Prefix();
            chest.Advance();

            if (kind.Water)
            {
                if (random.Next(2) == 0) chest.PutAndAdvance(4425);
                if (random.Next(2) == 0) chest.PutAndAdvance(4460);
            }
            if (kind.Skyware && random.Next(40) == 0)
            {
                chest.Put(4978);
                chest.Prefix();
                chest.Advance();
            }
            if (kind.LivingWood && random.Next(3) == 0)
                chest.PutAndAdvance(5629);
            if (kind.LivingWood && random.Next(6) == 0)
                chest.PutAndAdvance(random.Next(2) == 0 ? 4429 : 4427);
            if (kind.LivingWood && random.Next(3) != 0)
                chest.PutAndAdvance(5528);
            DungeonKeys(random, context, in kind, chest);
        }
        else
        {
            chest.Put(random.Next(6) switch { 0 => 49, 1 => 50, 2 => 53, 3 => 54, 4 => 5011, _ => 975 });
            chest.Prefix();
            ApplyExtraSignature(random,chest,profile,1);
            chest.Advance();

            if (random.Next(20) == 0)
            {
                chest.Put(997);
                chest.Prefix();
                chest.Advance();
            }
            else if (random.Next(20) == 0)
            {
                chest.Put(930);
                chest.Prefix();
                chest.Advance();
                if(profile.Has(VanillaSecretWorldSeed1458.RainbowRoad))chest.PutStackAndAdvance(5379,random.Next(50)+50);
                else chest.PutStackAndAdvance(931, random.Next(26) + 25);
            }

            if (kind.Style32 && random.Next(2) == 0)
                chest.PutAndAdvance(4450);
            if (kind.Style32 && random.Next(3) == 0)
            {
                chest.PutAndAdvance(4779);
                chest.PutAndAdvance(4780);
                chest.PutAndAdvance(4781);
            }
        }

        if (kind.DesertHive)
        {
            if (random.Next(3) == 0)
                chest.PutStackAndAdvance(4423, random.Next(10, 20));
        }
        else if (random.Next(3) == 0)
        {
            chest.PutStackAndAdvance(166, random.Next(10, 20));
        }

        if (random.Next(5) == 0)
            chest.PutAndAdvance(52);
        if (random.Next(3) == 0)
            chest.PutStackAndAdvance(965, random.Next(50, 101));
        if (random.Next(2) == 0)
        {
            int bar = random.Next(2) == 0 ? context.IronBar : context.SilverBar;
            chest.PutStackAndAdvance(bar, random.Next(10) + 5);
        }
        if (random.Next(2) == 0)
        {
            int rope = random.Next(2) == 0 ? 40 : 42;
            chest.PutStackAndAdvance(rope, random.Next(25) + 25);
        }
        if (random.Next(2) == 0)
            chest.PutStackAndAdvance(28, random.Next(3) + 3);
        if (random.Next(3) > 0)
        {
            int potion = random.Next(9) switch
            {
                0 => 289, 1 => 298, 2 => 299, 3 => 290, 4 => 303, 5 => 291, 6 => 304, 7 => 2322, _ => 2329
            };
            chest.PutStackAndAdvance(potion, random.Next(1, 3));
        }
        if (random.Next(3) != 0)
            chest.PutStackAndAdvance(2350, random.Next(2, 5));
        if (random.Next(2) == 0)
            chest.PutStackAndAdvance(kind.Frozen ? 974 : 8, random.Next(11) + 10);
        if (random.Next(2) == 0)
            chest.PutStackAndAdvance(72, random.Next(50, 90));
    }

    private static void Cavern(
        IWorldGenerationVanillaRandom random,
        BuriedChestContext1458 context,
        in BuriedChestKind1458 kind,
        Slots chest,
        int primary,
        int floorY,bool rainbowGlowsticks,VanillaWorldSeedProfile1458 profile)
    {
        if (primary > 0)
        {
            chest.Put(primary);
            chest.Prefix();
            chest.Advance();

            if (kind.Frozen && random.Next(5) == 0)
                chest.PutAndAdvance(3199);
            if (kind.DesertHive)
            {
                if (random.Next(7) == 0) chest.PutAndAdvance(4346);
                if (random.Next(15) == 0) chest.PutAndAdvance(4066);
            }
            if (kind.Ivy)
            {
                if (!context.GennedLivingMahoganyWands || random.Next(5) == 0)
                {
                    context.GennedLivingMahoganyWands = true;
                    chest.PutAndAdvance(3360);
                    chest.PutAndAdvance(3361);
                }
                if (random.Next(10) == 0) chest.PutAndAdvance(4426);
                if (random.Next(10) == 0) chest.PutAndAdvance(5525);
            }
            if (kind.Water)
            {
                if (random.Next(2) == 0) chest.PutAndAdvance(4425);
                if (random.Next(2) == 0) chest.PutAndAdvance(4460);
            }
            DungeonKeys(random, context, in kind, chest);
        }
        else
        {
            // The seven-way selector is drawn BEFORE the two gates that can override it, so a chest that ends
            // up with a Magic Mirror still paid for the roll it did not use.
            int choice = random.Next(7);
            bool belowLava = profile.Has(VanillaSpecialWorldSeed1458.Remix)?floorY>context.WorldSurface&&floorY<context.RockLayer:floorY > context.LavaLine;
            if (random.Next(profile.Has(VanillaSpecialWorldSeed1458.CelebrationMk10)?15:20) == 0 && belowLava)
            {
                chest.Put(906);
                chest.Prefix();
            }
            else if (random.Next(15) == 0)
            {
                chest.Put(997);
                chest.Prefix();
            }
            else if (choice == 6)
            {
                chest.Put(930);
                chest.Prefix();
                chest.Advance();
                if(profile.Has(VanillaSecretWorldSeed1458.RainbowRoad))chest.PutStack(5379,random.Next(50)+50);
                else chest.PutStack(931, random.Next(26) + 25);
            }
            else
            {
                chest.Put(choice switch { 0 => 49, 1 => 50, 2 => 53, 3 => 54, 4 => 5011, _ => 975 });
                chest.Prefix();
                ApplyExtraSignature(random,chest,profile,0);
            }

            chest.Advance();

            if (kind.Style32)
            {
                if (random.Next(2) == 0)
                {
                    chest.PutAndAdvance(4450);
                }
                else
                {
                    chest.PutAndAdvance(4779);
                    chest.PutAndAdvance(4780);
                    chest.PutAndAdvance(4781);
                }
            }
        }

        if (random.Next(5) == 0)
            chest.PutAndAdvance(kind.Frozen ? 5120 : 43);
        if (random.Next(3) == 0)
            chest.PutAndAdvance(167);
        if (random.Next(4) == 0)
            chest.PutStackAndAdvance(51, random.Next(26) + 25);
        if (random.Next(2) == 0)
        {
            int bar = random.Next(2) == 0 ? context.GoldBar : context.SilverBar;
            chest.PutStackAndAdvance(bar, random.Next(8) + 3);
        }
        if (random.Next(2) == 0)
        {
            int rope = random.Next(2) == 0 ? 41 : 279;
            chest.PutStackAndAdvance(rope, random.Next(26) + 25);
        }
        if (random.Next(2) == 0)
            chest.PutStackAndAdvance(188, random.Next(3) + 3);
        if (random.Next(3) > 0)
        {
            int potion = random.Next(6) switch { 0 => 296, 1 => 295, 2 => 299, 3 => 302, 4 => 303, _ => 305 };
            chest.PutStackAndAdvance(potion, random.Next(1, 3));
        }
        if (random.Next(3) > 1)
        {
            int potion = random.Next(6) switch { 0 => 301, 1 => 297, 2 => 304, 3 => 2329, 4 => 2351, _ => 2326 };
            chest.PutStackAndAdvance(potion, random.Next(1, 3));
        }
        if (random.Next(2) == 0)
            chest.PutStackAndAdvance(2350, random.Next(2, 5));
        if (random.Next(2) == 0 || profile.Has(VanillaSecretWorldSeed1458.WhatAHorribleNightToHaveACurse)&&random.Next(2)==0)
        {
            int selector = random.Next(2);
            int stack = random.Next(15, 31);
            if(profile.Has(VanillaSecretWorldSeed1458.WhatAHorribleNightToHaveACurse))stack*=2;
            chest.PutStackAndAdvance(selector == 0 ? (kind.Frozen ? 974 : 8) : rainbowGlowsticks?5643:282, stack);
        }
        if (random.Next(2) == 0)
            chest.PutStackAndAdvance(73, random.Next(1, 3));
    }

    private static void Underworld(
        IWorldGenerationVanillaRandom random,
        BuriedChestContext1458 context,
        in BuriedChestKind1458 kind,
        Slots chest,
        int primary,bool rainbowGlowsticks)
    {
        if (primary > 0)
        {
            chest.Put(primary);
            chest.Prefix();
            chest.Advance();

            if (kind.Hell && random.Next(5) == 0)
            {
                chest.Put(5010);
                chest.Prefix();
                chest.Advance();
            }
            if (kind.Hell && random.Next(10) == 0) chest.PutAndAdvance(4443);
            if (kind.Hell && random.Next(10) == 0) chest.PutAndAdvance(4737);
            if (kind.Hell && random.Next(10) == 0) chest.PutAndAdvance(4551);
        }
        else
        {
            chest.Put(random.Next(4) switch { 0 => 49, 1 => 50, 2 => 53, _ => 54 });
            chest.Prefix();
            chest.Advance();
        }

        if (random.Next(3) == 0)
            chest.PutAndAdvance(167);
        if (random.Next(2) == 0)
        {
            int bar = random.Next(2) == 0 ? 117 : context.GoldBar;
            chest.PutStackAndAdvance(bar, random.Next(15) + 15);
        }
        if (random.Next(2) == 0)
        {
            int selector = random.Next(2);
            int stack = random.Next(25) + 50;
            int ammo = selector == 0 ? 265 : (context.TungstenIsSilverTier ? 4915 : 278);
            chest.PutStackAndAdvance(ammo, stack);
        }
        if (random.Next(2) == 0)
            chest.PutStackAndAdvance(227, random.Next(6) + 15);
        if (random.Next(4) > 0)
        {
            int potion = random.Next(8) switch
            {
                0 => 296, 1 => 295, 2 => 293, 3 => 288, 4 => 294, 5 => 297, 6 => 304, _ => 2323
            };
            chest.PutStackAndAdvance(potion, random.Next(1, 3));
        }
        if (random.Next(3) > 0)
        {
            int potion = random.Next(8) switch
            {
                0 => 305, 1 => 301, 2 => 302, 3 => 288, 4 => 300, 5 => 2351, 6 => 2348, _ => 2345
            };
            chest.PutStackAndAdvance(potion, random.Next(1, 3));
        }
        if (random.Next(3) == 0)
        {
            int stack = random.Next(1, 3);
            chest.PutStackAndAdvance(random.Next(2) == 0 ? 2350 : 4870, stack);
        }
        if (random.Next(2) == 0)
        {
            int selector = random.Next(2);
            int stack = random.Next(15) + 15;
            chest.PutStackAndAdvance(selector == 0 ? 8 : rainbowGlowsticks?5643:282, stack);
        }
        if (random.Next(2) == 0)
            chest.PutStackAndAdvance(73, random.Next(2, 5));
    }

    /// <summary>
    /// The dungeon's two one-per-world keys. Both are guaranteed the first time and rationed afterwards, which
    /// is why the flags live on the run's state rather than on the chest.
    /// </summary>
    private static void DungeonKeys(
        IWorldGenerationVanillaRandom random,
        BuriedChestContext1458 context,
        in BuriedChestKind1458 kind,
        Slots chest)
    {
        if (!kind.Dungeon || kind.LockedBiome)
            return;

        if (!context.GeneratedShadowKey || random.Next(3) == 0)
        {
            context.GeneratedShadowKey = true;
            chest.PutAndAdvance(329);
        }
        if (!context.GeneratedRamRune || random.Next(8) == 0)
        {
            context.GeneratedRamRune = true;
            chest.Put(5465);
            chest.Prefix();
            chest.Advance();
        }
        if (random.Next(4) == 0)
        {
            chest.Put(6156);
            chest.Prefix();
            chest.Advance();
        }
    }

    private static void ContainerTail(
        IWorldGenerationVanillaRandom random, in BuriedChestKind1458 kind, Slots chest, int style,VanillaWorldSeedProfile1458 profile)
    {
        if (kind.Ivy && random.Next(4) == 0) chest.PutAndAdvance(2204);
        if (kind.Ivy && random.Next(50) == 0) chest.PutAndAdvance(753);
        if (kind.Frozen && random.Next(7) == 0) chest.PutAndAdvance(2198);
        if (kind.Frozen && random.Next(50) == 0) chest.PutAndAdvance(669);
        if (kind.Skyware && random.Next(3) == 0) chest.PutAndAdvance(2197);
        if (kind.Lihzahrd) chest.PutAndAdvance(2195);
        if (kind.Dungeon && random.Next(8) == 0) chest.PutAndAdvance(2192);
        if(kind.Dungeon&&profile.Has(VanillaSpecialWorldSeed1458.ForTheWorthy)&&random.Next(5)==0)chest.PutAndAdvance(5515);
        if (kind.Skyware)
        {
            chest.PutAndAdvance(BiomeFurniture(random));
            chest.PutStackAndAdvance(751, random.Next(50, 101));
        }
        if (style is >= 23 and <= 27 && random.Next(2) == 0)
            chest.PutAndAdvance(5234);
        if (kind.Lihzahrd)
        {
            if (random.Next(5) == 0)
                chest.PutAndAdvance(2767);
            else
                chest.PutStackAndAdvance(2766, random.Next(3, 8));
        }
    }

    private static void DesertContainerTail(
        IWorldGenerationVanillaRandom random, in BuriedChestKind1458 kind, Slots chest, int style)
    {
        if (kind.Ivy && random.Next(4) == 0) chest.PutAndAdvance(2204);
        if (kind.Ivy && random.Next(50) == 0) chest.PutAndAdvance(753);
        if (kind.Frozen && random.Next(7) == 0) chest.PutAndAdvance(2198);
        if (kind.Frozen && random.Next(50) == 0) chest.PutAndAdvance(669);
        if (kind.Skyware && random.Next(3) == 0) chest.PutAndAdvance(2197);
        if (kind.Skyware)
        {
            chest.PutAndAdvance(BiomeFurniture(random));
            chest.PutStackAndAdvance(751, random.Next(50, 101));
        }
        if (style == 13 && random.Next(2) == 0)
            chest.PutAndAdvance(5234);
    }

    private static int BiomeFurniture(IWorldGenerationVanillaRandom random) =>
        random.Next(6) switch { 0 => 5258, 1 => 5226, 2 => 5254, 3 => 5238, 4 => 5255, _ => 5388 };

    /// <summary>
    /// The source writes into <c>chest.item[itemIndex]</c> and advances the index separately, and several
    /// branches write the same slot twice before advancing. This keeps that distinction rather than appending,
    /// because the difference is visible whenever a branch overwrites.
    /// </summary>
    private sealed class Slots(IWorldGenerationVanillaRandom random)
    {
        private readonly WorldGenerationChestItem[] items =
            new WorldGenerationChestItem[WorldGenerationChestRules.VanillaItemSlotCount];

        public int Count { get; private set; }

        public void Put(int type) => items[Count] = new WorldGenerationChestItem(1, new ItemTypeId(type));

        public void PutStack(int type, int stack) =>
            items[Count] = new WorldGenerationChestItem(stack, new ItemTypeId(type));

        public void Prefix() =>
            items[Count] = items[Count] with
            {
                Prefix = new PrefixId(VanillaItemPrefixRoll1458.Roll(items[Count].ItemType, random))
            };

        public void Advance() => Count++;

        public void PutAndAdvance(int type)
        {
            Put(type);
            Count++;
        }

        public void PutStackAndAdvance(int type, int stack)
        {
            PutStack(type, stack);
            Count++;
        }

        public WorldGenerationChestItem[] ToArray() => items[..Count];
    }
}
