using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Ordered 1.4.5.8 NPC-specific registrations for the explicit ordinary melee cohort.</summary>
public static class VanillaOrdinaryMeleeLootCatalog1458
{
    private static readonly Dictionary<int, VanillaNpcLootTable> Tables = CreateTables();

    public static bool TryGet(NpcTypeId type, out VanillaNpcLootTable table) => Tables.TryGetValue(type.Value, out table);

    private static VanillaNpcLootRule Common(ItemTypeId item, int chance = 1, short min = 1, short max = 1,
        VanillaNpcLootRuleKind kind = VanillaNpcLootRuleKind.NormalVsExpertCommon) => new(kind, item, chance, chance, min, max, 1);

    private static VanillaNpcLootRule Options(int chance, params ItemTypeId[] items) =>
        new(VanillaNpcLootRuleKind.OneFromOptions, items[0], chance, chance, 1, 1, 1, Options: new(items));

    private static Dictionary<int, VanillaNpcLootTable> CreateTables()
    {
        var tables = new Dictionary<int, VanillaNpcLootTable>();
        void Add(int type, params VanillaNpcLootRule[] rules) => tables.Add(type, new(new(type), rules));
        // Populate registers food first, then weird/misc rules, status immunity and Blood Moon fishing.
        // A verified empty table (140) remains distinct from an unsupported actor.
        Add(44,
            Common(VanillaOrdinaryMeleeDropItemIds1458.Steak, 10, kind: VanillaNpcLootRuleKind.NotFromStatueCommon),
            Common(VanillaOrdinaryMeleeDropItemIds1458.BonePickaxe, 20),
            Common(VanillaOrdinaryMeleeDropItemIds1458.MiningHelmet, 20),
            new(VanillaNpcLootRuleKind.FailedRollChain, VanillaOrdinaryMeleeDropItemIds1458.Hook, 1, 1, 1, 1, 1,
                new([Common(VanillaOrdinaryMeleeDropItemIds1458.Hook, 25),
                    Options(4, VanillaOrdinaryMeleeDropItemIds1458.MiningShirt, VanillaOrdinaryMeleeDropItemIds1458.MiningPants),
                    Common(VanillaOrdinaryMeleeDropItemIds1458.Bomb, 1, 1, 3)])));
        Add(104, Common(VanillaOrdinaryMeleeDropItemIds1458.MoonCharm, 60),
            Common(VanillaOrdinaryMeleeDropItemIds1458.AdhesiveBandage, 100, kind: VanillaNpcLootRuleKind.ExpertGetsOneReroll));
        Add(140);
        Add(77, Common(new(723), 150),
            Common(new(886), 100, kind: VanillaNpcLootRuleKind.ExpertGetsOneReroll));
        Add(185, Common(new(951), 25), Common(new(5070), 1, 1, 2) with { ExpertMaximumStack = 3 },
            Common(VanillaOrdinaryMeleeDropItemIds1458.Compass, 50));
        Add(181, Common(VanillaOrdinaryMeleeDropItemIds1458.Vertebrae, 3),
            Common(VanillaOrdinaryMeleeDropItemIds1458.TentacleSpike, 100, kind: VanillaNpcLootRuleKind.DontStarveCommon),
            Common(VanillaOrdinaryMeleeDropItemIds1458.TentacleSpike, 525, kind: VanillaNpcLootRuleKind.NotDontStarveCommon),
            Common(VanillaOrdinaryMeleeDropItemIds1458.PigPetItem, 500, kind: VanillaNpcLootRuleKind.DontStarveCommon),
            Common(VanillaOrdinaryMeleeDropItemIds1458.PigPetItem, 1500, kind: VanillaNpcLootRuleKind.NotDontStarveCommon));
        Add(196, VanillaNpcLootRule.NormalVsExpertCommon(VanillaOrdinaryMeleeDropItemIds1458.MetalDetector, 2, 1));
        Add(197, Common(VanillaOrdinaryMeleeDropItemIds1458.IceSickle, 100), Common(VanillaOrdinaryMeleeDropItemIds1458.Compass, 50));
        Add(464, Common(VanillaOrdinaryMeleeDropItemIds1458.BunnyHood, 75),
            Common(VanillaOrdinaryMeleeDropItemIds1458.KiteBunnyCrimson, 25, kind: VanillaNpcLootRuleKind.WindyCommon));
        Add(470, Options(50, VanillaOrdinaryMeleeDropItemIds1458.PedguinHat,
            VanillaOrdinaryMeleeDropItemIds1458.PedguinShirt, VanillaOrdinaryMeleeDropItemIds1458.PedguinPants));
        Add(481, Common(new(4029), 50, kind: VanillaNpcLootRuleKind.NotFromStatueCommon),
            Common(new(3094), 2, 40, 80), Options(7, new(3187), new(3188), new(3189)),
            Common(new(4463), 20), Common(new(5543), 100), Common(VanillaOrdinaryMeleeDropItemIds1458.Hook, 25));
        Add(482, Common(new(4036), 50, kind: VanillaNpcLootRuleKind.NotFromStatueCommon),
            Common(new(3086), 1, 5, 10), Common(new(6167), 80), Common(new(3109), 30), Common(new(4400), 20));
        foreach (int id in new[] { 508, 580 })
            Add(id, Common(new(4012), 50, kind: VanillaNpcLootRuleKind.NotFromStatueCommon),
                Common(new(323), 3, 1, 2), Common(new(3772), 50));
        Add(582, Common(new(323), 6));
        Add(489, VanillaNpcLootRule.NormalVsExpertCommon(VanillaOrdinaryMeleeDropItemIds1458.SharkToothNecklace, 150, 75),
            VanillaNpcLootRule.NormalVsExpertCommon(VanillaOrdinaryMeleeDropItemIds1458.MoneyTrough, 200, 100),
            Common(VanillaOrdinaryMeleeDropItemIds1458.BloodMoonStarter, 100, kind: VanillaNpcLootRuleKind.BloodMoonNotFromStatueCommon));
        Add(524, Common(VanillaOrdinaryMeleeDropItemIds1458.AncientCloth, 10, 1, 3));
        Add(525, VanillaNpcLootRule.NormalVsExpertCommon(VanillaOrdinaryMeleeDropItemIds1458.EaterOfLife, 200, 150),
            Common(VanillaOrdinaryMeleeDropItemIds1458.AncientCloth, 10),
            Common(VanillaOrdinaryMeleeDropItemIds1458.CursedFlame, 3, 1, 3), Common(VanillaOrdinaryMeleeDropItemIds1458.DarkShard, 15));
        Add(526, VanillaNpcLootRule.NormalVsExpertCommon(VanillaOrdinaryMeleeDropItemIds1458.EaterOfLife, 200, 150),
            Common(VanillaOrdinaryMeleeDropItemIds1458.AncientCloth, 10),
            Common(VanillaOrdinaryMeleeDropItemIds1458.Ichor, 3, 1, 3), Common(VanillaOrdinaryMeleeDropItemIds1458.DarkShard, 15));
        Add(527, VanillaNpcLootRule.NormalVsExpertCommon(VanillaOrdinaryMeleeDropItemIds1458.Crystallize, 200, 150),
            Common(VanillaOrdinaryMeleeDropItemIds1458.AncientCloth, 10), Common(VanillaOrdinaryMeleeDropItemIds1458.LightShard, 15));
        Add(528, Common(VanillaOrdinaryMeleeDropItemIds1458.SunMask, 25),
            Options(40, VanillaOrdinaryMeleeDropItemIds1458.LamiaHat, VanillaOrdinaryMeleeDropItemIds1458.LamiaShirt, VanillaOrdinaryMeleeDropItemIds1458.LamiaPants),
            Common(VanillaOrdinaryMeleeDropItemIds1458.SnakeBand, 40));
        Add(529, VanillaNpcLootRule.NormalVsExpertCommon(VanillaOrdinaryMeleeDropItemIds1458.EaterOfLife, 200, 150),
            Common(VanillaOrdinaryMeleeDropItemIds1458.MoonMask, 25),
            Options(40, VanillaOrdinaryMeleeDropItemIds1458.LamiaHat, VanillaOrdinaryMeleeDropItemIds1458.LamiaShirt, VanillaOrdinaryMeleeDropItemIds1458.LamiaPants),
            Common(VanillaOrdinaryMeleeDropItemIds1458.SnakeBand, 40));
        Add(536, Common(VanillaOrdinaryMeleeDropItemIds1458.TheBrideHat), Common(VanillaOrdinaryMeleeDropItemIds1458.TheBrideDress),
            Common(VanillaOrdinaryMeleeDropItemIds1458.BloodMoonStarter, 5, kind: VanillaNpcLootRuleKind.OnlyBadLuckCommon));
        Add(691, Options(1, VanillaOrdinaryMeleeDropItemIds1458.BlueMoss, VanillaOrdinaryMeleeDropItemIds1458.BrownMoss,
            VanillaOrdinaryMeleeDropItemIds1458.GreenMoss, VanillaOrdinaryMeleeDropItemIds1458.PurpleMoss,
            VanillaOrdinaryMeleeDropItemIds1458.RedMoss, VanillaOrdinaryMeleeDropItemIds1458.LavaMoss,
            VanillaOrdinaryMeleeDropItemIds1458.VioletMoss, VanillaOrdinaryMeleeDropItemIds1458.XenonMoss,
            VanillaOrdinaryMeleeDropItemIds1458.KryptonMoss, VanillaOrdinaryMeleeDropItemIds1458.ArgonMoss));
        return tables;
    }
}
