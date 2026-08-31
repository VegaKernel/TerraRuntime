using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Source-backed subset of TerrariaServer 1.4.5.8 WorldGen.Reset state that is required by generation passes and
/// persisted-world parity. The bootstrap deliberately owns the random choices made before Terrain so later source
/// ports continue from the same shared UnifiedRandom stream instead of reconstructing values independently.
/// </summary>
internal sealed class VanillaWorldGenerationBootstrapState1458
{
    public int JungleHut { get; init; }
    public bool CrimsonLeft { get; init; }
    public int NumClouds { get; init; }
    public float WindSpeedCurrent { get; init; }
    public required int[] HellChestItems { get; init; }
    public int SlimeRainTime { get; init; }
    public int CloudBackgroundActive { get; init; }
    public int CopperOre { get; init; }
    public int IronOre { get; init; }
    public int SilverOre { get; init; }
    public int GoldOre { get; init; }
    public int CopperBar { get; init; }
    public int IronBar { get; init; }
    public int SilverBar { get; init; }
    public int GoldBar { get; init; }
    public bool RandomEvilCrimson { get; init; }
    public bool EffectiveCrimson { get; init; }
    public int WorldId { get; init; }
    public required int[] TreeX { get; init; }
    public required int[] TreeStyle { get; init; }
    public required int[] CaveBackX { get; init; }
    public required int[] CaveBackStyle { get; init; }
    public int IceBackStyle { get; init; }
    public int HellBackStyle { get; init; }
    public int JungleBackStyle { get; init; }
    public required int[] ForestBackgroundStyles { get; init; }
    public int CorruptBackground { get; init; }
    public int JungleBackground { get; init; }
    public int SnowBackground { get; init; }
    public int HallowBackground { get; init; }
    public int CrimsonBackground { get; init; }
    public int DesertBackground { get; init; }
    public int OceanBackground { get; init; }
    public int MushroomBackground { get; init; }
    public int UnderworldBackground { get; init; }
    public int MoonType { get; init; }
    public int DungeonSide { get; init; }
    public int JungleOriginX { get; init; }
    public int SnowOriginLeft { get; init; }
    public int SnowOriginRight { get; init; }
    public int LeftBeachEnd { get; init; }
    public int RightBeachStart { get; init; }
    public int DungeonLocation { get; init; }
    public int SkyLakes { get; init; }
    public int ExtraBastStatueCountMax { get; init; }
}

/// <summary>
/// Clean-room WorldGen.Reset RNG/bootstrap port for TerrariaServer 1.4.5.8. This pass is enabled for the three
/// canonical vanilla dimensions and either an ordinary profile or the pure Don't Dig Up/Remix profile. Other special
/// seeds and synthetic test dimensions intentionally consume no extra RNG until their own Reset branches are ported.
/// </summary>
internal sealed class VanillaWorldGenerationBootstrapPass1458 : IWorldGenerationPass
{
    internal const int BeachBordersWidth = 275;
    internal const int BeachSandRandomCenter = BeachBordersWidth + 5 + 40;
    internal const int BeachSandRandomWidthRange = 20;
    internal const int BeachSandDungeonExtraWidth = 40;
    internal const int BeachSandJungleExtraWidth = 20;
    internal const int DungeonBeachPadding = 50;

    private const int DungeonSideLeft = -1;
    private const int DungeonSideRight = 1;
    private readonly VanillaWorldGenerationParityState1458 state;

    public VanillaWorldGenerationBootstrapPass1458(VanillaWorldGenerationParityState1458 state) =>
        this.state = state;

    public void Execute(IWorldGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        WorldGenerationRequest request = context.Request;
        VanillaWorldSeedProfile1458 seedProfile = VanillaWorldSeedResolver1458.Resolve(in request);
        if (!seedProfile.SupportsSourceBackedResetAndTerrain ||
            !VanillaTerrainPass1458.IsCanonicalWorldSize(context.Workspace.WidthTiles, context.Workspace.HeightTiles))
        {
            return;
        }

        IWorldGenerationVanillaRandom random = context.VanillaRandom ??
            throw new InvalidOperationException("The source-backed Terraria bootstrap requires shared UnifiedRandom semantics.");
        state.Bootstrap = Run(
            random,
            context.Workspace.WidthTiles,
            request.Options.Evil == WorldGenerationEvil.Crimson,
            seedProfile.Special == VanillaSpecialWorldSeed1458.Remix);
        context.ReportProgress(1d, "Applying source-backed Terraria WorldGen.Reset bootstrap");
    }

    internal static VanillaWorldGenerationBootstrapState1458 Run(
        IWorldGenerationVanillaRandom random,
        int width,
        bool effectiveCrimson,
        bool isRemix = false)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (width is not (4200 or 6400 or 8400))
            throw new ArgumentOutOfRangeException(nameof(width));

        int jungleHut = random.Next(5);
        bool crimsonLeft = random.Next(2) != 0;

        int numClouds = random.Next(10, 200);
        float windSpeedCurrent = 0f;
        while (windSpeedCurrent == 0f)
        {
            windSpeedCurrent = (float)random.NextDouble() * 0.35f * (random.Next(2) * 2 - 1);
        }

        int[] hellChestItems = ShuffleHellChestItems(random, isRemix);
        int slimeRainTime = -random.Next(86400 * 2, 86400 * 3);
        int cloudBackgroundActive = -random.Next(8640, 86400);

        int copperOre = 7;
        int ironOre = 6;
        int silverOre = 9;
        int goldOre = 8;
        int copperBar = 20;
        int ironBar = 22;
        int silverBar = 21;
        int goldBar = 19;
        if (random.Next(2) == 0)
        {
            copperOre = 166;
            copperBar = 703;
        }
        if (random.Next(2) == 0)
        {
            ironOre = 167;
            ironBar = 704;
        }
        if (random.Next(2) == 0)
        {
            silverOre = 168;
            silverBar = 705;
        }
        if (random.Next(2) == 0)
        {
            goldOre = 169;
            goldBar = 706;
        }

        bool randomEvilCrimson = random.Next(2) == 0;
        jungleHut = jungleHut switch
        {
            0 => 119,
            1 => 120,
            2 => 158,
            3 => 175,
            _ => 45
        };
        int worldId = random.Next(int.MaxValue);

        (int[] treeX, int[] treeStyle) = RandomizeTreeStyle(random, width);
        (int[] caveBackX, int[] caveBackStyle) = RandomizeCaveBackgrounds(random, width);
        int iceBackStyle = random.Next(4);
        int hellBackStyle = random.Next(3);
        int jungleBackStyle = random.Next(2);

        int[] forestBackgroundStyles = RandomizeForestBackgrounds(random);
        int corruptBackground = RandomizeCorruptionBackground(random);
        int jungleBackground = random.Next(7);
        int snowBackground = RandomizeSnowBackground(random);
        int hallowBackground = random.Next(6);
        int crimsonBackground = random.Next(7);
        int desertBackground = RandomizeDesertBackground(random);
        int oceanBackground = random.Next(8);
        int mushroomBackground = random.Next(5);
        int underworldBackground = random.Next(3);
        int moonType = random.Next(9);

        int dungeonSide = random.Next(2) == 0 ? DungeonSideLeft : DungeonSideRight;
        int jungleOriginX;
        int jungleDistanceMin = isRemix ? 20 : 15;
        int jungleDistanceMax = isRemix ? 35 : 30;
        if (dungeonSide <= DungeonSideLeft)
            jungleOriginX = (int)(width * (1d - random.Next(jungleDistanceMin, jungleDistanceMax) * 0.01d));
        else
            jungleOriginX = (int)(width * (random.Next(jungleDistanceMin, jungleDistanceMax) * 0.01d));

        int snowCenter = random.Next(width);
        if (dungeonSide == DungeonSideRight)
        {
            while (snowCenter < width * 0.6d || snowCenter > width * 0.75d)
                snowCenter = random.Next(width);
        }
        else
        {
            while (snowCenter < width * 0.25d || snowCenter > width * 0.4d)
                snowCenter = random.Next(width);
        }

        double widthScale = width / 4200d;
        int snowHalfWidth = random.Next(50, 90);
        snowHalfWidth += (int)(random.Next(20, 40) * widthScale);
        snowHalfWidth += (int)(random.Next(20, 40) * widthScale);
        int snowOriginLeft = Math.Max(0, snowCenter - snowHalfWidth);

        snowHalfWidth = random.Next(50, 90);
        snowHalfWidth += (int)(random.Next(20, 40) * widthScale);
        snowHalfWidth += (int)(random.Next(20, 40) * widthScale);
        int snowOriginRight = Math.Min(width, snowCenter + snowHalfWidth);

        int leftBeachEnd = random.Next(
            BeachSandRandomCenter - BeachSandRandomWidthRange,
            BeachSandRandomCenter + BeachSandRandomWidthRange);
        leftBeachEnd += dungeonSide == DungeonSideRight
            ? BeachSandDungeonExtraWidth
            : BeachSandJungleExtraWidth;

        int rightBeachStart = width - random.Next(
            BeachSandRandomCenter - BeachSandRandomWidthRange,
            BeachSandRandomCenter + BeachSandRandomWidthRange);
        rightBeachStart -= dungeonSide == DungeonSideLeft
            ? BeachSandDungeonExtraWidth
            : BeachSandJungleExtraWidth;

        int dungeonLocation = dungeonSide <= DungeonSideLeft
            ? random.Next(leftBeachEnd + DungeonBeachPadding, (int)(width * 0.2d))
            : random.Next((int)(width * 0.8d), rightBeachStart - DungeonBeachPadding);

        int skyLakes = 1 + (width > 8000 ? 1 : 0) + (width > 6000 ? 1 : 0);
        int extraBastStatueCountMax = width >= 8400 ? 4 : width >= 6400 ? 3 : 2;

        return new VanillaWorldGenerationBootstrapState1458
        {
            JungleHut = jungleHut,
            CrimsonLeft = crimsonLeft,
            NumClouds = numClouds,
            WindSpeedCurrent = windSpeedCurrent,
            HellChestItems = hellChestItems,
            SlimeRainTime = slimeRainTime,
            CloudBackgroundActive = cloudBackgroundActive,
            CopperOre = copperOre,
            IronOre = ironOre,
            SilverOre = silverOre,
            GoldOre = goldOre,
            CopperBar = copperBar,
            IronBar = ironBar,
            SilverBar = silverBar,
            GoldBar = goldBar,
            RandomEvilCrimson = randomEvilCrimson,
            EffectiveCrimson = effectiveCrimson,
            WorldId = worldId,
            TreeX = treeX,
            TreeStyle = treeStyle,
            CaveBackX = caveBackX,
            CaveBackStyle = caveBackStyle,
            IceBackStyle = iceBackStyle,
            HellBackStyle = hellBackStyle,
            JungleBackStyle = jungleBackStyle,
            ForestBackgroundStyles = forestBackgroundStyles,
            CorruptBackground = corruptBackground,
            JungleBackground = jungleBackground,
            SnowBackground = snowBackground,
            HallowBackground = hallowBackground,
            CrimsonBackground = crimsonBackground,
            DesertBackground = desertBackground,
            OceanBackground = oceanBackground,
            MushroomBackground = mushroomBackground,
            UnderworldBackground = underworldBackground,
            MoonType = moonType,
            DungeonSide = dungeonSide,
            JungleOriginX = jungleOriginX,
            SnowOriginLeft = snowOriginLeft,
            SnowOriginRight = snowOriginRight,
            LeftBeachEnd = leftBeachEnd,
            RightBeachStart = rightBeachStart,
            DungeonLocation = dungeonLocation,
            SkyLakes = skyLakes,
            ExtraBastStatueCountMax = extraBastStatueCountMax
        };
    }

    private static int[] ShuffleHellChestItems(IWorldGenerationVanillaRandom random, bool isRemix)
    {
        // WorldGen.Reset substitutes the Sunfury (112) slot with the Dark Lance (683) for Remix.
        var source = isRemix
            ? new List<int> { 274, 220, 683, 218, 3019 }
            : new List<int> { 274, 220, 112, 218, 3019 };
        var shuffled = new int[source.Count];
        for (int output = 0; output < shuffled.Length; output++)
        {
            int index = random.Next(source.Count);
            shuffled[output] = source[index];
            source.RemoveAt(index);
        }
        return shuffled;
    }

    private static (int[] X, int[] Style) RandomizeTreeStyle(IWorldGenerationVanillaRandom random, int width)
    {
        var x = new int[3];
        var style = new int[4];
        int count;
        if (width == 4200)
        {
            x[0] = random.Next((int)(width * 0.25d), (int)(width * 0.75d));
            x[1] = width;
            x[2] = width;
            count = 2;
        }
        else if (width == 6400)
        {
            x[0] = random.Next((int)(width * 0.134d), (int)(width * 0.534d));
            x[1] = random.Next((int)(width * 0.467d), (int)(width * 0.867d));
            x[2] = width;
            count = 3;
        }
        else
        {
            x[0] = random.Next((int)(width * 0.10d), (int)(width * 0.40d));
            x[1] = random.Next((int)(width * 0.35d), (int)(width * 0.65d));
            x[2] = random.Next((int)(width * 0.60d), (int)(width * 0.90d));
            count = 4;
        }

        for (int i = 0; i < count; i++)
            style[i] = random.Next(6);
        for (int i = 1; i < count; i++)
        {
            while (style.AsSpan(0, i).Contains(style[i]))
                style[i] = random.Next(6);
        }
        for (int i = 0; i < count; i++)
        {
            if (style[i] == 0 && random.Next(3) != 0)
                style[i] = 4;
        }
        return (x, style);
    }

    private static (int[] X, int[] Style) RandomizeCaveBackgrounds(IWorldGenerationVanillaRandom random, int width)
    {
        var x = new int[3];
        var style = new int[4];
        int count;
        if (width == 4200)
        {
            x[0] = random.Next((int)(width * 0.25d), (int)(width * 0.75d));
            x[1] = width;
            x[2] = width;
            count = 2;
        }
        else if (width == 6400)
        {
            x[0] = random.Next((int)(width * 0.134d), (int)(width * 0.534d));
            x[1] = random.Next((int)(width * 0.467d), (int)(width * 0.867d));
            x[2] = width;
            count = 3;
        }
        else
        {
            x[0] = random.Next((int)(width * 0.10d), (int)(width * 0.40d));
            x[1] = random.Next((int)(width * 0.35d), (int)(width * 0.65d));
            x[2] = random.Next((int)(width * 0.60d), (int)(width * 0.90d));
            count = 4;
        }

        for (int i = 0; i < count; i++)
            style[i] = random.Next(8);
        for (int i = 1; i < count; i++)
        {
            while (style.AsSpan(0, i).Contains(style[i]))
                style[i] = random.Next(8);
        }
        return (x, style);
    }

    private static int[] RandomizeForestBackgrounds(IWorldGenerationVanillaRandom random)
    {
        var styles = new int[4];
        for (int i = 0; i < styles.Length; i++)
        {
            styles[i] = RollRandomForestBackgroundStyle(random);
            while (styles.AsSpan(0, i).Contains(styles[i]))
                styles[i] = RollRandomForestBackgroundStyle(random);
        }
        return styles;
    }

    private static int RollRandomForestBackgroundStyle(IWorldGenerationVanillaRandom random)
    {
        int value = random.Next(14);
        if ((value == 1 || value == 2) && random.Next(2) == 0)
            value = random.Next(14);
        if (value == 0)
            value = random.Next(14);
        if (value == 3 && random.Next(3) == 0)
            value = 31;
        if (value == 5 && random.Next(2) == 0)
            value = 51;
        if (value == 7 && random.Next(4) == 0)
            value = random.Next(71, 74);
        return value;
    }

    private static int RandomizeCorruptionBackground(IWorldGenerationVanillaRandom random)
    {
        int value = random.Next(6);
        if (value == 5)
            value = random.Next(2) == 0 ? 51 : 52;
        return value;
    }

    private static int RandomizeSnowBackground(IWorldGenerationVanillaRandom random)
    {
        int value = random.Next(9);
        if (value == 2 && random.Next(2) == 0)
            value = random.Next(2) == 0 ? 21 : 22;
        if (value == 3 && random.Next(2) == 0)
            value = random.Next(2) == 0 ? 31 : 32;
        if (value == 4 && random.Next(2) == 0)
            value = random.Next(2) == 0 ? 41 : 42;
        return value;
    }

    private static int RandomizeDesertBackground(IWorldGenerationVanillaRandom random)
    {
        int value = random.Next(6);
        if (value == 5)
            value = 51 + random.Next(5) / 2;
        return value;
    }
}
