namespace TerraRuntime.Contracts.Gameplay;

/// <summary>
/// Initial source-verified TerrariaServer 1.4.5.8 NPC content ids used by TerraRuntime gameplay.
/// Extend this catalog only from the repository source-of-truth hierarchy; do not guess ids.
/// </summary>
public static class VanillaNpcIds
{
    public static readonly NpcTypeId BlueSlime = new(1);
    public static readonly NpcTypeId DemonEye = new(2);
    public static readonly NpcTypeId Zombie = new(3);
    public static readonly NpcTypeId EyeOfCthulhu = new(4);
    public static readonly NpcTypeId ServantOfCthulhu = new(5);
    public static readonly NpcTypeId EaterOfSouls = new(6);
    public static readonly NpcTypeId DevourerHead = new(7);
    public static readonly NpcTypeId DevourerBody = new(8);
    public static readonly NpcTypeId DevourerTail = new(9);
    public static readonly NpcTypeId GiantWormHead = new(10);
    public static readonly NpcTypeId GiantWormBody = new(11);
    public static readonly NpcTypeId GiantWormTail = new(12);
    public static readonly NpcTypeId EaterOfWorldsHead = new(13);
    public static readonly NpcTypeId EaterOfWorldsBody = new(14);
    public static readonly NpcTypeId EaterOfWorldsTail = new(15);
    public static readonly NpcTypeId MotherSlime = new(16);
    public static readonly NpcTypeId Merchant = new(17);
    public static readonly NpcTypeId Nurse = new(18);
    public static readonly NpcTypeId ArmsDealer = new(19);
    public static readonly NpcTypeId Dryad = new(20);
    public static readonly NpcTypeId Guide = new(22);
    public static readonly NpcTypeId FireImp = new(24);
    public static readonly NpcTypeId BurningSphere = new(25);
    public static readonly NpcTypeId SkeletronHead = new(35);
    public static readonly NpcTypeId SkeletronHand = new(36);
    public static readonly NpcTypeId Demolitionist = new(38);
    public static readonly NpcTypeId Clothier = new(54);
    public static readonly NpcTypeId BoundGoblin = new(105);
    public static readonly NpcTypeId BoundWizard = new(106);
    public static readonly NpcTypeId GoblinTinkerer = new(107);
    public static readonly NpcTypeId Wizard = new(108);
    public static readonly NpcTypeId BoundMechanic = new(123);
    public static readonly NpcTypeId Mechanic = new(124);
    public static readonly NpcTypeId SantaClaus = new(142);
    public static readonly NpcTypeId Truffle = new(160);
    public static readonly NpcTypeId Steampunker = new(178);
    public static readonly NpcTypeId DyeTrader = new(207);
    public static readonly NpcTypeId PartyGirl = new(208);
    public static readonly NpcTypeId Cyborg = new(209);
    public static readonly NpcTypeId Painter = new(227);
    public static readonly NpcTypeId WitchDoctor = new(228);
    public static readonly NpcTypeId Pirate = new(229);
    public static readonly NpcTypeId Stylist = new(353);
    public static readonly NpcTypeId WebbedStylist = new(354);
    public static readonly NpcTypeId Angler = new(369);
    public static readonly NpcTypeId SleepingAngler = new(376);
    public static readonly NpcTypeId TaxCollector = new(441);
    public static readonly NpcTypeId DemonTaxCollector = new(534);
    public static readonly NpcTypeId Tavernkeep = new(550);
    public static readonly NpcTypeId BartenderUnconscious = new(579);
    public static readonly NpcTypeId Golfer = new(588);
    public static readonly NpcTypeId GolferRescue = new(589);
    public static readonly NpcTypeId Zoologist = new(633);
    public static readonly NpcTypeId TownCat = new(637);
    public static readonly NpcTypeId TownDog = new(638);
    public static readonly NpcTypeId TownBunny = new(656);
    public static readonly NpcTypeId Princess = new(663);
    public static readonly NpcTypeId TownSlimeBlue = new(670);
    public static readonly NpcTypeId TownSlimeGreen = new(678);
    public static readonly NpcTypeId TownSlimeOld = new(679);
    public static readonly NpcTypeId TownSlimePurple = new(680);
    public static readonly NpcTypeId TownSlimeRainbow = new(681);
    public static readonly NpcTypeId TownSlimeRed = new(682);
    public static readonly NpcTypeId TownSlimeYellow = new(683);
    public static readonly NpcTypeId TownSlimeCopper = new(684);
    public static readonly NpcTypeId MysticFrog = new(687);
    public static readonly NpcTypeId Skeleton = new(21);
    public static readonly NpcTypeId GoblinPeon = new(26);
    public static readonly NpcTypeId GoblinThief = new(27);
    public static readonly NpcTypeId GoblinWarrior = new(28);
    public static readonly NpcTypeId AngryBones = new(31);
    public static readonly NpcTypeId DoctorBones = new(52);
    public static readonly NpcTypeId TheGroom = new(53);
    public static readonly NpcTypeId GoblinScout = new(73);
    public static readonly NpcTypeId ArmoredSkeleton = new(77);
    public static readonly NpcTypeId BaldZombie = new(132);
    public static readonly NpcTypeId ZombieEskimo = new(161);
    public static readonly NpcTypeId UndeadViking = new(167);
    public static readonly NpcTypeId PincushionZombie = new(186);
    public static readonly NpcTypeId SlimedZombie = new(187);
    public static readonly NpcTypeId SwampZombie = new(188);
    public static readonly NpcTypeId TwiggyZombie = new(189);
    public static readonly NpcTypeId FemaleZombie = new(200);
    public static readonly NpcTypeId MeteorHead = new(23);
    public static readonly NpcTypeId Hornet = new(42);
    public static readonly NpcTypeId BoneSerpentHead = new(39);
    public static readonly NpcTypeId BoneSerpentBody = new(40);
    public static readonly NpcTypeId BoneSerpentTail = new(41);
    public static readonly NpcTypeId KingSlime = new(50);
    public static readonly NpcTypeId LavaSlime = new(59);
    public static readonly NpcTypeId DungeonSlime = new(71);
    public static readonly NpcTypeId CorruptSlime = new(81);
    public static readonly NpcTypeId WyvernHead = new(87);
    public static readonly NpcTypeId WyvernLegs = new(88);
    public static readonly NpcTypeId WyvernBody = new(89);
    public static readonly NpcTypeId WyvernBody2 = new(90);
    public static readonly NpcTypeId WyvernBody3 = new(91);
    public static readonly NpcTypeId WyvernTail = new(92);
    public static readonly NpcTypeId Corruptor = new(94);
    public static readonly NpcTypeId DiggerHead = new(95);
    public static readonly NpcTypeId DiggerBody = new(96);
    public static readonly NpcTypeId DiggerTail = new(97);
    public static readonly NpcTypeId SeekerHead = new(98);
    public static readonly NpcTypeId SeekerBody = new(99);
    public static readonly NpcTypeId SeekerTail = new(100);
    public static readonly NpcTypeId Retinazer = new(125);
    public static readonly NpcTypeId Spazmatism = new(126);
    public static readonly NpcTypeId SkeletronPrime = new(127);
    public static readonly NpcTypeId PrimeCannon = new(128);
    public static readonly NpcTypeId PrimeSaw = new(129);
    public static readonly NpcTypeId PrimeVice = new(130);
    public static readonly NpcTypeId PrimeLaser = new(131);
    public static readonly NpcTypeId WallOfFlesh = new(113);
    public static readonly NpcTypeId WallOfFleshEye = new(114);
    public static readonly NpcTypeId TheHungry = new(115);
    public static readonly NpcTypeId TheHungryII = new(116);
    public static readonly NpcTypeId LeechHead = new(117);
    public static readonly NpcTypeId LeechBody = new(118);
    public static readonly NpcTypeId LeechTail = new(119);
    public static readonly NpcTypeId WanderingEye = new(133);
    public static readonly NpcTypeId Destroyer = new(134);
    public static readonly NpcTypeId DestroyerBody = new(135);
    public static readonly NpcTypeId DestroyerTail = new(136);
    public static readonly NpcTypeId Probe = new(139);
    public static readonly NpcTypeId IlluminantSlime = new(138);
    public static readonly NpcTypeId ToxicSludge = new(141);
    public static readonly NpcTypeId IceSlime = new(147);
    public static readonly NpcTypeId Crimslime = new(183);
    public static readonly NpcTypeId SpikedIceSlime = new(184);
    public static readonly NpcTypeId CataractEye = new(190);
    public static readonly NpcTypeId SleepyEye = new(191);
    public static readonly NpcTypeId DilatedEye = new(192);
    public static readonly NpcTypeId GreenEye = new(193);
    public static readonly NpcTypeId PurpleEye = new(194);
    public static readonly NpcTypeId SpikedJungleSlime = new(204);
    public static readonly NpcTypeId UmbrellaSlime = new(225);
    public static readonly NpcTypeId RainbowSlime = new(244);
    public static readonly NpcTypeId Golem = new(245);
    public static readonly NpcTypeId GolemHead = new(246);
    public static readonly NpcTypeId GolemFistLeft = new(247);
    public static readonly NpcTypeId GolemFistRight = new(248);
    public static readonly NpcTypeId GolemHeadFree = new(249);
    public static readonly NpcTypeId SlimeMasked = new(302);
    public static readonly NpcTypeId DemonEyeOwl = new(317);
    public static readonly NpcTypeId DemonEyeSpaceship = new(318);
    public static readonly NpcTypeId SlimeRibbonWhite = new(333);
    public static readonly NpcTypeId SlimeRibbonYellow = new(334);
    public static readonly NpcTypeId SlimeRibbonGreen = new(335);
    public static readonly NpcTypeId SlimeRibbonRed = new(336);
    public static readonly NpcTypeId DukeFishron = new(370);
    public static readonly NpcTypeId DetonatingBubble = new(371);
    public static readonly NpcTypeId Sharkron = new(372);
    public static readonly NpcTypeId Sharkron2 = new(373);
    public static readonly NpcTypeId TruffleWormDigger = new(375);
    public static readonly NpcTypeId MoonLordHead = new(396);
    public static readonly NpcTypeId MoonLordHand = new(397);
    public static readonly NpcTypeId MoonLordCore = new(398);
    public static readonly NpcTypeId MoonLordFreeEye = new(400);
    public static readonly NpcTypeId StardustWormHead = new(402);
    public static readonly NpcTypeId SolarCrawltipedeHead = new(412);
    public static readonly NpcTypeId SolarCrawltipedeBody = new(413);
    public static readonly NpcTypeId SolarCrawltipedeTail = new(414);
    public static readonly NpcTypeId LunaticCultist = new(439);
    public static readonly NpcTypeId LunaticCultistClone = new(440);
    public static readonly NpcTypeId CultistDragonHead = new(454);
    public static readonly NpcTypeId CultistDragonBody1 = new(455);
    public static readonly NpcTypeId CultistDragonBody2 = new(456);
    public static readonly NpcTypeId CultistDragonBody3 = new(457);
    public static readonly NpcTypeId CultistDragonBody4 = new(458);
    public static readonly NpcTypeId CultistDragonTail = new(459);
    public static readonly NpcTypeId AncientVision = new(521);
    public static readonly NpcTypeId AncientLight = new(522);
    public static readonly NpcTypeId AncientDoom = new(523);
    public static readonly NpcTypeId SpikedSlime = new(535);
    public static readonly NpcTypeId SandSlime = new(537);
    public static readonly NpcTypeId DuneSplicerHead = new(510);
    public static readonly NpcTypeId DuneSplicerBody = new(511);
    public static readonly NpcTypeId DuneSplicerTail = new(512);
    public static readonly NpcTypeId TombCrawlerHead = new(513);
    public static readonly NpcTypeId TombCrawlerBody = new(514);
    public static readonly NpcTypeId TombCrawlerTail = new(515);
    public static readonly NpcTypeId QueenSlimeMinionBlue = new(658);
    public static readonly NpcTypeId QueenSlimeMinionPink = new(659);
    public static readonly NpcTypeId GoldenSlime = new(667);
    public static readonly NpcTypeId ShimmerSlime = new(676);
    public static readonly NpcTypeId PigronCorruption = new(170);
    public static readonly NpcTypeId PigronHallow = new(171);
    public static readonly NpcTypeId Crimera = new(173);
    public static readonly NpcTypeId MossHornet = new(176);
    public static readonly NpcTypeId PigronCrimson = new(180);
    public static readonly NpcTypeId Moth = new(205);
    public static readonly NpcTypeId Bee = new(210);
    public static readonly NpcTypeId SmallBee = new(211);
    public static readonly NpcTypeId QueenBee = new(222);
    public static readonly NpcTypeId Deerclops = new(668);
    public static readonly NpcTypeId FattyHornet = new(231);
    public static readonly NpcTypeId HoneyHornet = new(232);
    public static readonly NpcTypeId LeafyHornet = new(233);
    public static readonly NpcTypeId SpikeyHornet = new(234);
    public static readonly NpcTypeId StingyHornet = new(235);
    public static readonly NpcTypeId Parrot = new(252);
    public static readonly NpcTypeId Plantera = new(262);
    public static readonly NpcTypeId PlanteraHook = new(263);
    public static readonly NpcTypeId PlanteraTentacle = new(264);
    public static readonly NpcTypeId PlanteraSpore = new(265);
    public static readonly NpcTypeId BrainOfCthulhu = new(266);
    public static readonly NpcTypeId BrainCreeper = new(267);
    public static readonly NpcTypeId BloodSquid = new(619);
    public static readonly NpcTypeId EmpressOfLight = new(636);
    public static readonly NpcTypeId QueenSlime = new(657);
    public static readonly NpcTypeId QueenSlimeMinionPurple = new(660);
    public static readonly NpcTypeId BloodEelHead = new(621);
    public static readonly NpcTypeId BloodEelBody = new(622);
    public static readonly NpcTypeId BloodEelTail = new(623);
    public static readonly NpcTypeId Vulture = new(61);
    public static readonly NpcTypeId SpikeBall = new(70);
    public static readonly NpcTypeId BlazingWheel = new(72);
    public static readonly NpcTypeId Raven = new(301);
}

/// <summary>
/// Initial source-verified TerrariaServer 1.4.5.8 NPC AI-style ids corresponding to the supported
/// lifecycle/AI slice. These are behavior families, not NPC content ids.
/// </summary>
public static class VanillaNpcAiStyles
{
    public static readonly NpcAiStyleId Slime = new(1);
    public static readonly NpcAiStyleId DemonEye = new(2);
    public static readonly NpcAiStyleId Fighter = new(3);
    public static readonly NpcAiStyleId EyeOfCthulhu = new(4);
    public static readonly NpcAiStyleId Flyer = new(5);
    public static readonly NpcAiStyleId Worm = new(6);
    public static readonly NpcAiStyleId Town = new(7);
    public static readonly NpcAiStyleId Caster = new(8);
    public static readonly NpcAiStyleId BurningSphere = new(9);
    public static readonly NpcAiStyleId SkeletronHead = new(11);
    public static readonly NpcAiStyleId SkeletronHand = new(12);
    public static readonly NpcAiStyleId KingSlime = new(15);
    public static readonly NpcAiStyleId Vulture = new(17);
    public static readonly NpcAiStyleId SpikeBall = new(20);
    public static readonly NpcAiStyleId BlazingWheel = new(21);
    public static readonly NpcAiStyleId WallOfFlesh = new(27);
    public static readonly NpcAiStyleId WallOfFleshEye = new(28);
    public static readonly NpcAiStyleId TheHungry = new(29);
    public static readonly NpcAiStyleId Retinazer = new(30);
    public static readonly NpcAiStyleId Spazmatism = new(31);
    public static readonly NpcAiStyleId SkeletronPrime = new(32);
    public static readonly NpcAiStyleId PrimeSaw = new(33);
    public static readonly NpcAiStyleId PrimeVice = new(34);
    public static readonly NpcAiStyleId PrimeCannon = new(35);
    public static readonly NpcAiStyleId PrimeLaser = new(36);
    public static readonly NpcAiStyleId Destroyer = new(37);
    public static readonly NpcAiStyleId QueenBee = new(43);
    public static readonly NpcAiStyleId Golem = new(45);
    public static readonly NpcAiStyleId GolemHead = new(46);
    public static readonly NpcAiStyleId GolemFist = new(47);
    public static readonly NpcAiStyleId GolemHeadFree = new(48);
    public static readonly NpcAiStyleId PlanteraSpore = new(50);
    public static readonly NpcAiStyleId Plantera = new(51);
    public static readonly NpcAiStyleId PlanteraHook = new(52);
    public static readonly NpcAiStyleId PlanteraTentacle = new(53);
    public static readonly NpcAiStyleId BrainOfCthulhu = new(54);
    public static readonly NpcAiStyleId BrainCreeper = new(55);
    public static readonly NpcAiStyleId DukeFishron = new(69);
    public static readonly NpcAiStyleId DetonatingBubble = new(70);
    public static readonly NpcAiStyleId Sharkron = new(71);
    public static readonly NpcAiStyleId MoonLordCore = new(77);
    public static readonly NpcAiStyleId MoonLordHand = new(78);
    public static readonly NpcAiStyleId MoonLordHead = new(79);
    public static readonly NpcAiStyleId MoonLordFreeEye = new(81);
    public static readonly NpcAiStyleId LunaticCultist = new(84);
    public static readonly NpcAiStyleId AncientVision = new(86);
    public static readonly NpcAiStyleId AncientLight = new(100);
    public static readonly NpcAiStyleId AncientDoom = new(101);
    public static readonly NpcAiStyleId EmpressOfLight = new(120);
    public static readonly NpcAiStyleId QueenSlime = new(121);
    public static readonly NpcAiStyleId Deerclops = new(123);
}

/// <summary>
/// TerrariaServer 1.4.5.8 item content bounds and named identities currently consumed by gameplay.
/// The count is pinned to ItemID.Count as independently exercised by packet-5 Item.netDefaults normalization tests.
/// Named ids are added only after source verification against the pinned TerrariaServer binary.
/// </summary>
public static class VanillaItemIds
{
    public const int Count = 6196;

    public static ItemTypeId None => default;
    public static readonly ItemTypeId DirtBlock = new(2);
    public static readonly ItemTypeId StoneBlock = new(3);
    public static readonly ItemTypeId Torch = new(8);
    public static readonly ItemTypeId Gel = new(23);
    public static readonly ItemTypeId WoodenBow = new(39);
    public static readonly ItemTypeId WoodenArrow = new(40);
    public static readonly ItemTypeId FlamingArrow = new(41);
    public static readonly ItemTypeId UnholyArrow = new(47);
    public static readonly ItemTypeId JestersArrow = new(51);
    public static readonly ItemTypeId CopperGreaves = new(76);
    public static readonly ItemTypeId CopperChainmail = new(80);
    public static readonly ItemTypeId CopperHelmet = new(89);
    public static readonly ItemTypeId IronBow = new(99);
    public static readonly ItemTypeId Chest = new(48);
    public static readonly ItemTypeId FlowerOfFire = new(112);
    public static readonly ItemTypeId MagicMissile = new(113);
    public static readonly ItemTypeId Muramasa = new(155);
    public static readonly ItemTypeId CobaltShield = new(156);
    public static readonly ItemTypeId SorcererEmblem = new(489);
    public static readonly ItemTypeId WarriorEmblem = new(490);
    public static readonly ItemTypeId RangerEmblem = new(491);
    public static readonly ItemTypeId MagicQuiver = new(1321);
    public static readonly ItemTypeId SharkToothNecklace = new(3212);
    public static readonly ItemTypeId AquaScepter = new(157);
    public static readonly ItemTypeId BlueMoon = new(163);
    public static readonly ItemTypeId Handgun = new(164);
    public static readonly ItemTypeId SandBlock = new(169);
    public static readonly ItemTypeId Obsidian = new(173);
    public static readonly ItemTypeId Stinger = new(209);
    public static readonly ItemTypeId Vine = new(210);
    public static readonly ItemTypeId Flamelash = new(218);
    public static readonly ItemTypeId Sunfury = new(220);
    public static readonly ItemTypeId DarkLance = new(274);
    public static readonly ItemTypeId GoldenKey = new(327);
    public static readonly ItemTypeId JungleSpores = new(331);
    public static readonly ItemTypeId Rope = new(965);
    public static readonly ItemTypeId SlimeStaff = new(1309);
    public static readonly ItemTypeId RecallPotion = new(2350);
    public static readonly ItemTypeId HellwingBow = new(3019);
    public static readonly ItemTypeId Valor = new(3317);
    public static readonly ItemTypeId PlatinumBow = new(3480);
    public static readonly ItemTypeId TungstenBow = new(3486);
    public static readonly ItemTypeId LeadBow = new(3492);
    public static readonly ItemTypeId TinBow = new(3498);
    public static readonly ItemTypeId CopperBow = new(3504);
    public static readonly ItemTypeId CopperHammer = new(3505);
    public static readonly ItemTypeId CopperAxe = new(3506);
    public static readonly ItemTypeId CopperBroadsword = new(3508);
    public static readonly ItemTypeId CopperPickaxe = new(3509);
    public static readonly ItemTypeId SilverBow = new(3510);
    public static readonly ItemTypeId GoldBow = new(3516);

    public static bool TryCreate(int rawType, out ItemTypeId type)
    {
        if ((uint)rawType >= (uint)Count)
        {
            type = default;
            return false;
        }

        type = new ItemTypeId(rawType);
        return true;
    }
}

/// <summary>
/// TerrariaServer 1.4.5.8 projectile content bounds and the initial named identities used by runtime tests.
/// IDs 1111..1135 were the final additions in the 1.4.5.7 content update, leaving ProjectileID.Count at
/// 1136 for 1.4.5.8. Zero is ProjectileID.None. A handful of in-range legacy gaps also fall through
/// Projectile.SetDefaults and therefore are not live authoritative projectile types.
/// </summary>
public static class VanillaProjectileIds
{
    public const int Count = 1136;

    public static ProjectileTypeId None => default;
    public static readonly ProjectileTypeId WoodenArrowFriendly = new(1);
    public static readonly ProjectileTypeId FireArrow = new(2);
    public static readonly ProjectileTypeId Shuriken = new(3);
    public static readonly ProjectileTypeId UnholyArrow = new(4);
    public static readonly ProjectileTypeId JestersArrow = new(5);
    public static readonly ProjectileTypeId EnchantedBoomerang = new(6);
    public static readonly ProjectileTypeId Bullet = new(14);
    public static readonly ProjectileTypeId GreenLaser = new(20);
    public static readonly ProjectileTypeId Bone = new(21);
    public static readonly ProjectileTypeId ThrowingKnife = new(48);
    public static readonly ProjectileTypeId Seed = new(51);
    public static readonly ProjectileTypeId PoisonedKnife = new(54);
    public static readonly ProjectileTypeId ProbePinkLaser = new(84);
    public static readonly ProjectileTypeId ConfettiGun = new(178);
    public static readonly ProjectileTypeId GolemFireball = new(258);
    public static readonly ProjectileTypeId GolemEyeBeam = new(259);
    public static readonly ProjectileTypeId SkeletronSkull = new(270);
    public static readonly ProjectileTypeId PlanteraSeed = new(275);
    public static readonly ProjectileTypeId PlanteraPoisonSeed = new(276);
    public static readonly ProjectileTypeId PlanteraThornBall = new(277);
    public static readonly ProjectileTypeId SharknadoBolt = new(385);
    public static readonly ProjectileTypeId PhantasmalEye = new(452);
    public static readonly ProjectileTypeId PhantasmalSphere = new(454);
    public static readonly ProjectileTypeId PhantasmalDeathray = new(455);
    public static readonly ProjectileTypeId MoonLeech = new(456);
    public static readonly ProjectileTypeId PhantasmalBolt = new(462);
    public static readonly ProjectileTypeId CultistBossIceMist = new(464);
    public static readonly ProjectileTypeId CultistBossLightningOrb = new(465);
    public static readonly ProjectileTypeId CultistBossFireBall = new(467);
    public static readonly ProjectileTypeId CultistBossFireBallClone = new(468);
    public static readonly ProjectileTypeId CultistRitual = new(490);
    public static readonly ProjectileTypeId AncientDoomProjectile = new(593);
    public static readonly ProjectileTypeId MoonLordBlowupSmoke = new(622);
    public static readonly ProjectileTypeId HallowBossLastingRainbow = new(872);
    public static readonly ProjectileTypeId HallowBossRainbowStreak = new(873);
    public static readonly ProjectileTypeId HallowBossDeathAurora = new(874);
    public static readonly ProjectileTypeId FairyQueenLance = new(919);
    public static readonly ProjectileTypeId FairyQueenSunDance = new(923);
    public static readonly ProjectileTypeId MoonBoulder = new(1021);
    public static readonly ProjectileTypeId QueenBeeStinger = new(719);
    public static readonly ProjectileTypeId WallOfFleshEyeLaser = new(83);
    public static readonly ProjectileTypeId SpazmatismCursedFlame = new(96);
    public static readonly ProjectileTypeId RetinazerDeathLaser = new(100);
    public static readonly ProjectileTypeId SpazmatismEyeFire = new(101);
    public static readonly ProjectileTypeId SkeletronPrimeBomb = new(102);
    public static readonly ProjectileTypeId SuperStar = new(728);
    public static readonly ProjectileTypeId StarCannonStar = new(955);
    public static readonly ProjectileTypeId QueenSlimeSmash = new(922);
    public static readonly ProjectileTypeId QueenSlimeGelAttack = new(926);
    public static readonly ProjectileTypeId DeerclopsIceSpike = new(961);
    public static readonly ProjectileTypeId DeerclopsRubble = new(962);
    public static readonly ProjectileTypeId DeerclopsShadowHand = new(965);
    public static readonly ProjectileTypeId BloodShot = new(811);
    public static readonly ProjectileTypeId ConfettiMelee = new(289);
    public static readonly ProjectileTypeId RottenEgg = new(318);
    public static readonly ProjectileTypeId StarAnise = new(330);
    public static readonly ProjectileTypeId BoneArrowFromMerchant = new(474);
    public static readonly ProjectileTypeId NurseSyringeHurt = new(583);
    public static readonly ProjectileTypeId SantaBombs = new(589);
    public static readonly ProjectileTypeId BoneDagger = new(599);
    public static readonly ProjectileTypeId Waffle = new(1012);
    public static readonly ProjectileTypeId SoundGun = new(1099);
    public static readonly ProjectileTypeId MeleeBone = new(1111);
    public static readonly ProjectileTypeId BoneShard = new(1124);

    /// <summary>
    /// Validates only the version-pinned projectile identity range. Whether an in-range ID is a live
    /// <c>Projectile.SetDefaults</c> type is gameplay semantics owned by
    /// <c>TerraRuntime.Gameplay.Projectiles.VanillaProjectileLifecycleFacts</c>.
    /// </summary>
    public static bool TryCreate(int rawType, out ProjectileTypeId type)
    {
        if ((uint)rawType >= (uint)Count)
        {
            type = default;
            return false;
        }

        type = new ProjectileTypeId(rawType);
        return true;
    }
}

/// <summary>
/// Source-verified TerrariaServer 1.4.5.8 tile identities currently consumed by world/gameplay code.
/// The catalog intentionally grows with implemented behavior instead of copying an unverified giant ID table.
/// Behavior-family predicates live here so consumers do not duplicate raw-id membership sets.
/// </summary>
public static class VanillaTileIds
{
    public const int Count = 754;

    public static readonly TileTypeId Dirt = new(0);
    public static readonly TileTypeId Stone = new(1);
    public static readonly TileTypeId Grass = new(2);
    public static readonly TileTypeId Trees = new(5);
    public static readonly TileTypeId ClosedDoor = new(10);
    public static readonly TileTypeId OpenDoor = new(11);
    public static readonly TileTypeId Tables = new(14);
    public static readonly TileTypeId Chairs = new(15);
    public static readonly TileTypeId Platforms = new(19);
    public static readonly TileTypeId Containers = new(21);
    public static readonly TileTypeId CorruptGrass = new(23);
    public static readonly TileTypeId Ebonstone = new(25);
    public static readonly TileTypeId DemonAltar = new(26);
    public static readonly TileTypeId ShadowOrbs = new(31);
    public static readonly TileTypeId Sunflower = new(27);
    public static readonly TileTypeId Cobweb = new(51);
    public static readonly TileTypeId Sand = new(53);
    public static readonly TileTypeId Signs = new(55);
    public static readonly TileTypeId Obsidian = new(56);
    public static readonly TileTypeId Mud = new(59);
    public static readonly TileTypeId JungleGrass = new(60);
    public static readonly TileTypeId MushroomGrass = new(70);
    public static readonly TileTypeId MushroomPlants = new(71);
    public static readonly TileTypeId MushroomTrees = new(72);
    public static readonly TileTypeId ObsidianBrick = new(75);
    public static readonly TileTypeId HellstoneBrick = new(76);
    public static readonly TileTypeId Hellforge = new(77);
    public static readonly TileTypeId Tombstones = new(85);
    public static readonly TileTypeId Dressers = new(88);
    public static readonly TileTypeId Bookcases = new(101);
    public static readonly TileTypeId SnowBlock = new(147);
    public static readonly TileTypeId IceBlock = new(161);
    public static readonly TileTypeId CrimsonGrass = new(199);
    public static readonly TileTypeId Crimstone = new(203);
    public static readonly TileTypeId DemoniteBrick = new(140);
    public static readonly TileTypeId CrimtaneBrick = new(347);
    public static readonly TileTypeId Hive = new(225);
    public static readonly TileTypeId LihzahrdBrick = new(226);
    public static readonly TileTypeId Larva = new(231);
    public static readonly TileTypeId LihzahrdAltar = new(237);
    public static readonly TileTypeId Marble = new(367);
    public static readonly TileTypeId Granite = new(368);
    public static readonly TileTypeId TargetDummy = new(378);
    public static readonly TileTypeId Bubble = new(379);
    public static readonly TileTypeId TrapdoorOpen = new(386);
    public static readonly TileTypeId TallGateClosed = new(388);
    public static readonly TileTypeId TallGateOpen = new(389);
    public static readonly TileTypeId ItemFrame = new(395);
    public static readonly TileTypeId AnnouncementBox = new(425);
    public static readonly TileTypeId TeamBlockRedPlatform = new(427);
    public static readonly TileTypeId TeamBlockGreenPlatform = new(435);
    public static readonly TileTypeId TeamBlockBluePlatform = new(436);
    public static readonly TileTypeId TeamBlockYellowPlatform = new(437);
    public static readonly TileTypeId TeamBlockPinkPlatform = new(438);
    public static readonly TileTypeId TeamBlockWhitePlatform = new(439);
    public static readonly TileTypeId Containers2 = new(467);
    public static readonly TileTypeId DisplayDoll = new(470);
    public static readonly TileTypeId WeaponsRack2 = new(471);
    public static readonly TileTypeId HatRack = new(475);
    public static readonly TileTypeId Toilets = new(497);
    public static readonly TileTypeId FoodPlatter = new(520);
    public static readonly TileTypeId MushroomVines = new(528);
    public static readonly TileTypeId TatteredWoodSign = new(573);
    public static readonly TileTypeId TeleportationPylon = new(597);
    public static readonly TileTypeId StinkbugHousingBlocker = new(630);
    public static readonly TileTypeId StinkbugHousingBlockerEcho = new(631);
    public static readonly TileTypeId DeadCellsDisplayJar = new(698);

    public static bool TryCreate(int rawType, out TileTypeId type)
    {
        if ((uint)rawType >= (uint)Count)
        {
            type = default;
            return false;
        }

        type = new TileTypeId(rawType);
        return true;
    }

    public static bool IsPlatform(TileTypeId type) =>
        type == Platforms ||
        type == TeamBlockRedPlatform ||
        type == TeamBlockGreenPlatform ||
        type == TeamBlockBluePlatform ||
        type == TeamBlockYellowPlatform ||
        type == TeamBlockPinkPlatform ||
        type == TeamBlockWhitePlatform;

    public static bool IsChestAnchor(TileTypeId type) =>
        type == Containers ||
        type == Containers2 ||
        type == Dressers;

    public static bool IsClosedDoor(TileTypeId type) =>
        type == ClosedDoor ||
        type == TallGateClosed;

    public static bool IsNpcChair(TileTypeId type) =>
        type == Chairs ||
        type == Toilets;

    public static bool CountsForTruffleHousing(TileTypeId type) =>
        type == MushroomGrass ||
        type == MushroomPlants ||
        type == MushroomTrees ||
        type == MushroomVines;

    public static bool CarriesSignText(TileTypeId type) =>
        type == Signs ||
        type == Tombstones ||
        type == AnnouncementBox ||
        type == TatteredWoodSign;
}

/// <summary>
/// Source-verified TerrariaServer 1.4.5.8 wall identities currently consumed by world/gameplay code.
/// Zero is the vanilla no-wall identity and remains a catalogued value at storage/protocol boundaries.
/// </summary>
public static class VanillaWallIds
{
    public const int Count = 367;

    public static readonly WallTypeId None = new(0);
    public static readonly WallTypeId Stone = new(1);
    public static readonly WallTypeId DirtUnsafe = new(2);
    public static readonly WallTypeId BlueDungeonUnsafe = new(7);
    public static readonly WallTypeId HellstoneBrickUnsafe = new(13);
    public static readonly WallTypeId ObsidianBrickUnsafe = new(14);
    public static readonly WallTypeId Dirt = new(16);
    public static readonly WallTypeId BlueDungeon = new(17);
    public static readonly WallTypeId Glass = new(21);
    public static readonly WallTypeId SpiderUnsafe = new(62);
    public static readonly WallTypeId HiveUnsafe = new(86);
    public static readonly WallTypeId LihzahrdBrickUnsafe = new(87);
    public static readonly WallTypeId UnbreakableTemple = new(350);

    public static bool TryCreate(int rawType, out WallTypeId type)
    {
        if ((uint)rawType >= (uint)Count)
        {
            type = default;
            return false;
        }

        type = new WallTypeId(rawType);
        return true;
    }
}

