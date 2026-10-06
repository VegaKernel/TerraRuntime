using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Authoritative inputs sampled at NewNPC time. Difficulty is the effective value after seed adjustment.</summary>
public readonly record struct VanillaNpcSpawnContext(float Difficulty, int ActivePlayers, bool GoodWorld)
{
    public bool HardMode { get; init; }
    public bool DownedPlantera { get; init; }
    public bool SkeletronActive { get; init; }
    public bool TenthAnniversaryWorld { get; init; }
    public bool RemixWorld { get; init; }
    public bool IsValid => float.IsFinite(Difficulty) && Difficulty is >= .5f and <= 4f &&
        ActivePlayers is >= 0 and <= 255;
}

public readonly partial record struct VanillaNpcSpawnDefaults(
    VanillaNpcHitboxSize Hitbox, float Scale, int LifeMax, int Damage, int Defense)
{
    public float? KnockBackResist { get; init; }
    public float? Difficulty { get; init; }
    public float? VerifiedMoneyValue { get; init; }

    /// <summary>Mother Slime's NewNPC(1) followed by SetDefaults(-5), including the nested knockback scaling.</summary>
    public static bool TryResolveMotherSlimeChild(in VanillaNpcSpawnContext context,
        out VanillaNpcSpawnDefaults defaults, out float moneyValue)
    {
        defaults = default;
        moneyValue = 0f;
        if (!context.IsValid || !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.BlueSlime,
            VanillaNpcNetVariantCatalog.BabySlime, out var definition))
            return false;
        bool windowsArithmetic = OperatingSystem.IsWindows();
        float firstKnockback = (float)Ramp(context.Difficulty, 1f, 3f, .8f, windowsArithmetic);
        definition = definition with { KnockBackResist = firstKnockback * .95f };
        var scaled = ResolveOrdinary(in definition, in context, windowsArithmetic);
        float scale = context.GoodWorld ? (.9f + .9f * .9f) / 2f : .9f;
        int width = (int)(24f * scale);
        int height = (int)(18f * scale);
        if (height is 16 or 32)
            height++;
        defaults = scaled with { Hitbox = new(width, height), Scale = scale, Difficulty = context.Difficulty };
        int rawMoney = 10;
        if (context.Difficulty >= 2f && context.HardMode)
        {
            int factor = (context.DownedPlantera ? 100 : 80) / (13 + 4 + 30 / 4);
            rawMoney = (int)((double)(rawMoney * factor) * .8d);
        }
        float multiplier = context.Difficulty <= 1f ? 1f : context.Difficulty < 2f
            ? 1f + (context.Difficulty - 1f) * 1.5f
            : context.Difficulty <= 3f ? 2.5f : 2.5f + context.Difficulty - 3f;
        moneyValue = (int)(rawMoney * multiplier);
        return true;
    }
    /// <summary>
    /// NPC.SetDefaults -> special-seed adjustments -> ScaleStats for Destroyer, Probe, Prime, Skeletron and Duke Fishron (1.4.5.8).
    /// Other families keep their existing definition defaults until their type-specific scaling is verified.
    /// </summary>
    public static bool TryResolve(in VanillaNpcDefinition definition, in VanillaNpcSpawnContext context,
        out VanillaNpcSpawnDefaults defaults) => TryResolve(in definition, in context, OperatingSystem.IsWindows(), out defaults);

    /// <summary>Resolves the pinned original platform arithmetic, also used for cross-platform differential verification.</summary>
    public static bool TryResolve(in VanillaNpcDefinition definition, in VanillaNpcSpawnContext context,
        bool windowsArithmetic, out VanillaNpcSpawnDefaults defaults)
    {
        defaults = default;
        if (context.IsValid && definition.Type == VanillaNpcIds.BlueSlime)
            return TryResolveSlimeFamily(in definition, in context, windowsArithmetic, out defaults);
        if (context.IsValid && (definition.Type == VanillaNpcIds.LavaSlime ||
            definition.Type == VanillaNpcIds.Bee || definition.Type == VanillaNpcIds.SmallBee))
            return TryResolveContainedFamily(in definition, in context, windowsArithmetic, out defaults);
        if (context.IsValid && (definition.Type == VanillaNpcIds.PirateGhost ||
            definition.Type == VanillaNpcIds.PirateDeckhand || definition.Type == VanillaNpcIds.PirateCorsair ||
            definition.Type == VanillaNpcIds.PirateDeadeye || definition.Type == VanillaNpcIds.PirateCrossbower ||
            definition.Type == VanillaNpcIds.PirateCaptain || definition.Type == VanillaNpcIds.Parrot))
        {
            // Independently captured actual GameMode0/1/2, with the Good-world effective difficulty increment.
            if (context.Difficulty is not (1f or 2f or 3f or 4f)) return false;
            // Source scaling uses the verified raw dimensions without admitting an executable public definition.
            var sourceDefinition = definition.Type == VanillaNpcIds.PirateGhost
                ? VanillaPirateGhostNpcCatalog1458.Definition : definition;
            defaults = ResolveOrdinary(in sourceDefinition, in context, windowsArithmetic) with { Difficulty = context.Difficulty };
            return true;
        }
        if (context.IsValid && definition.BehaviorFamily == VanillaNpcBehaviorFamily.FlyingEye)
            return TryResolveFlyingEye(in definition, in context, windowsArithmetic, out defaults);
        if (context.IsValid && VanillaBigMimicNpcCatalog1458.IsSupported(definition.Type))
        {
            defaults = ResolveOrdinary(in definition, in context, windowsArithmetic) with { Difficulty = context.Difficulty };
            return true;
        }
        if (context.IsValid && VanillaMothronNpcCatalog1458.IsSupported(definition.Type) && !context.GoodWorld)
        {
            defaults = definition.Type == VanillaNpcIds.MothronEgg
                ? new(new(definition.Width, definition.Height), definition.Scale, definition.LifeMax, definition.Damage, definition.Defense)
                    { KnockBackResist = definition.KnockBackResist, Difficulty = 1f }
                : ResolveOrdinary(in definition, in context, windowsArithmetic) with { Difficulty = context.Difficulty };
            return true;
        }
        if (context.IsValid && (definition.Type == VanillaNpcIds.Bunny || definition.Type == VanillaNpcIds.ExplosiveBunny))
        {
            // ScaleStats does not enter its scaling block for these five-life, zero-damage critters.
            defaults = new(new(definition.Width, definition.Height), definition.Scale, definition.LifeMax, definition.Damage, definition.Defense)
            { KnockBackResist = definition.KnockBackResist, Difficulty = 1f };
            return true;
        }
        if (context.IsValid && (definition.Type == VanillaNpcIds.DarkCaster || definition.Type == VanillaNpcIds.WaterSphere ||
            definition.Type == VanillaNpcIds.Demon || definition.Type == VanillaNpcIds.VoodooDemon))
        {
            defaults = ResolveOrdinary(in definition, in context, windowsArithmetic);
            return true;
        }
        if (context.IsValid && VanillaMimicNpcCatalog1458.IsMimic(definition.Type))
        {
            defaults = ResolveMimic(in definition, in context, windowsArithmetic);
            return true;
        }
        bool probe = definition.Type == VanillaNpcIds.Probe;
        bool skeletronHead = definition.Type == VanillaNpcIds.SkeletronHead;
        bool skeletronHand = definition.Type == VanillaNpcIds.SkeletronHand;
        bool skeletron = skeletronHead || skeletronHand;
        bool duke = definition.Type == VanillaNpcIds.DukeFishron;
        bool prime = definition.Type == VanillaNpcIds.SkeletronPrime || definition.Type == VanillaNpcIds.PrimeCannon ||
            definition.Type == VanillaNpcIds.PrimeSaw || definition.Type == VanillaNpcIds.PrimeVice || definition.Type == VanillaNpcIds.PrimeLaser;
        if (!context.IsValid || (!probe && !prime && !skeletron && !duke && definition.Type != VanillaNpcIds.Destroyer &&
            definition.Type != VanillaNpcIds.DestroyerBody && definition.Type != VanillaNpcIds.DestroyerTail)) return false;

        int width = definition.Width, height = definition.Height;
        float scale = definition.Scale;
        // NPC.SetDefaults calls getGoodAdjustments first and only calls getTenthAnniversaryAdjustments in
        // the alternative branch. A composite test context must retain that source precedence too.
        if (context.GoodWorld)
        {
            scale *= probe ? 1.6f : prime ? 1.1f : skeletronHead ? 1.25f : skeletronHand ? 1.15f : 1.3f;
            // getGoodAdjustments multiplies the already-scaled dimensions by the new visual scale.
            width = (int)(width * scale);
            height = (int)(height * scale);
        }
        else if (duke && context.TenthAnniversaryWorld)
        {
            // NPC.getTenthAnniversaryAdjustments halves type 370's visual scale, then materializes the same
            // half-scale physical dimensions before ScaleStats runs.
            scale *= .5f;
            width = (int)(width * scale);
            height = (int)(height * scale);
        }
        float difficulty = context.Difficulty;
        int life = windowsArithmetic ? (int)(definition.LifeMax * (double)difficulty) : (int)(definition.LifeMax * difficulty);
        double damageMultiplier = DamageMultiplier(difficulty, windowsArithmetic);
        int damage = windowsArithmetic ? (int)(definition.Damage * damageMultiplier) : (int)(definition.Damage * (float)damageMultiplier);
        float expertLifeTweak = duke ? .65f : skeletronHead ? 1f : skeletronHand ? 1.3f : .75f;
        float masterLifeTweak = duke ? .85f : probe ? 1f : .85f;
        float lifeTweak = (float)(Ramp(difficulty, 1f, 2f, expertLifeTweak, windowsArithmetic) * Ramp(difficulty, 2f, 3f, masterLifeTweak, windowsArithmetic));
        life = (int)Math.Round(windowsArithmetic ? life * (double)lifeTweak : life * lifeTweak);
        float expertDamageTweak = duke ? .7f : probe ? .8f : skeletron ? 1.1f : definition.Type == VanillaNpcIds.Destroyer ? 2f : .85f;
        double damageTweak = Ramp(difficulty, 1f, 2f, expertDamageTweak, windowsArithmetic);
        damage = (int)Math.Round(windowsArithmetic ? damage * damageTweak : damage * (float)damageTweak);
        if (difficulty >= 2f)
        {
            if (!prime && !skeletron && !duke) scale *= 1.05f;
            float balance = PlayerBalance(context.ActivePlayers, windowsArithmetic);
            double multiplier = probe ? 1d + (balance - 1d) * (2d / 3d) : balance;
            life = (int)Math.Round(life * multiplier);
        }
        defaults = new(new(width, height), scale, life, damage, definition.Defense);
        return true;
    }

    private static bool TryResolveFlyingEye(in VanillaNpcDefinition definition, in VanillaNpcSpawnContext context,
        bool windowsArithmetic, out VanillaNpcSpawnDefaults defaults)
    {
        defaults = default;
        if (!VanillaNpcDefinitionCatalog.TryGet(definition.Type, out var canonical) ||
            canonical.BehaviorFamily != VanillaNpcBehaviorFamily.FlyingEye ||
            !VanillaNpcMoneyDefaults1458.TryResolve(canonical.Type, new((short)canonical.Type.Value),
                1f, out float money))
            return false;
        bool hungry = canonical.Type == VanillaNpcIds.TheHungryII;
        var scalingContext = hungry ? context with { HardMode = false } : context;
        var scaled = ResolveOrdinary(in canonical, in scalingContext, windowsArithmetic);
        if (!hungry && context.Difficulty >= 2f && context.HardMode)
        {
            int budget = Math.Max(1, canonical.Damage + canonical.Defense + canonical.LifeMax / 4);
            int threshold = context.DownedPlantera ? 100 : 80;
            if (budget < threshold)
                money = (int)((double)(money * (threshold / budget)) * .8d);
        }
        money = VanillaNpcMoneyDefaults1458.ScaleBaseline((int)money, context.Difficulty);
        if (hungry)
        {
            float lifeTweak = (float)Ramp(context.Difficulty, 1f, 2f, .7f, windowsArithmetic);
            int life = (int)Math.Round(windowsArithmetic ? scaled.LifeMax * (double)lifeTweak : scaled.LifeMax * lifeTweak);
            float knockback = scaled.KnockBackResist ?? canonical.KnockBackResist;
            if (context.Difficulty >= 2f)
            {
                life = (int)Math.Round(life * (double)PlayerBalance(context.ActivePlayers, windowsArithmetic));
                if (context.ActivePlayers > 4)
                    knockback = 0f;
                else if (context.ActivePlayers > 1)
                {
                    float boost = .35f;
                    for (int player = 1; player < context.ActivePlayers; player++)
                        boost = windowsArithmetic ? (float)(boost + (1d - boost) / 3d) : boost + (1f - boost) / 3f;
                    knockback *= 1f - boost;
                }
            }
            float hungryScale = context.GoodWorld ? 1.4f : 1f;
            scaled = scaled with
            {
                LifeMax = life,
                Scale = hungryScale,
                Hitbox = new((int)(canonical.BaseWidth * hungryScale), (int)(canonical.BaseHeight * hungryScale)),
                KnockBackResist = knockback
            };
        }
        if (definition == canonical)
        {
            defaults = scaled with { Difficulty = context.Difficulty, VerifiedMoneyValue = money };
            return true;
        }
        // SetDefaultsFromNetId invokes canonical SetDefaults with the scale override, then multiplies
        // its already difficulty-scaled live stats. Recognize the exact existing source variant definition.
        bool recognized = false;
        foreach (var variant in VanillaNpcNetVariantCatalog.All)
            if (variant.NetId.Value is >= -43 and <= -38 && variant.Type == canonical.Type &&
                variant.ApplyTo(in canonical) == definition)
            {
                recognized = true;
                break;
            }
        if (!recognized)
            return false;
        float scale = definition.Scale;
        if (context.GoodWorld)
            scale = (scale + scale * scale) / 2f;
        defaults = new(new((int)(canonical.BaseWidth * scale), (int)(canonical.BaseHeight * scale)), scale,
            (int)(scaled.LifeMax * scale), (int)(scaled.Damage * scale), (int)(scaled.Defense * scale))
        {
            Difficulty = context.Difficulty,
            KnockBackResist = (scaled.KnockBackResist ?? canonical.KnockBackResist) * (2f - scale),
            VerifiedMoneyValue = (int)(money * scale)
        };
        return true;
    }

    private static VanillaNpcSpawnDefaults ResolveOrdinary(in VanillaNpcDefinition definition, in VanillaNpcSpawnContext context, bool windowsArithmetic)
    {
        bool sphere = definition.Type == VanillaNpcIds.WaterSphere;
        bool skeletron = (sphere || definition.Type == VanillaNpcIds.DarkCaster) && context.GoodWorld && context.SkeletronActive;
        float difficulty = context.Difficulty;
        int life = definition.LifeMax, damage = definition.Damage, defense = definition.Defense;
        // ScaleStats_ForExpertHardmode uses integer division before converting the budget ratio to float.
        if (difficulty >= 2f && context.HardMode && !skeletron)
        {
            int budget = Math.Max(1, damage + defense + life / 4);
            int threshold = context.DownedPlantera ? 100 : 80;
            if (budget < threshold)
            {
                float factor = threshold / budget;
                damage = windowsArithmetic ? (int)(damage * (double)factor * .9f) : (int)(damage * factor * .9f);
                if (!sphere)
                {
                    defense = (int)(defense * factor);
                    life = (int)((double)(life * factor) * 1.1d);
                }
            }
        }
        if (!sphere) life = windowsArithmetic ? (int)(life * (double)difficulty) : (int)(life * difficulty);
        double damageMultiplier = DamageMultiplier(difficulty, windowsArithmetic);
        damage = windowsArithmetic ? (int)(damage * damageMultiplier) : (int)(damage * (float)damageMultiplier);
        if (!sphere && skeletron)
        {
            float tweak = (float)(Ramp(difficulty, 1f, 2f, 1.5f, windowsArithmetic) * Ramp(difficulty, 2f, 3f, .85f, windowsArithmetic));
            life = (int)Math.Round(windowsArithmetic ? life * (double)tweak : life * tweak);
            if (difficulty >= 2f) defense += 6;
        }
        if (!sphere) life = Math.Max(6, life);
        return new(new(definition.Width, definition.Height), definition.Scale, life, damage, defense)
        {
            KnockBackResist = windowsArithmetic
                ? (float)(definition.KnockBackResist * Ramp(difficulty, 1f, 3f, .8f, true))
                : definition.KnockBackResist * (float)Ramp(difficulty, 1f, 3f, .8f, false)
        };
    }

    private static VanillaNpcSpawnDefaults ResolveMimic(in VanillaNpcDefinition definition, in VanillaNpcSpawnContext context,
        bool windowsArithmetic)
    {
        // NPC.SetDefaults assigns 30/12/300 to ordinary and Ice Mimics before ScaleStats. Present Mimic
        // retains its own 100/32/900 baseline. None of the three types has a type-specific ScaleStats tweak
        // or multiplayer life multiplier in 1.4.5.8.
        bool preHardMode = !context.HardMode && VanillaMimicNpcCatalog1458.HasPreHardModeDefaults(definition.Type);
        int life = preHardMode ? 300 : definition.LifeMax;
        int damage = preHardMode ? 30 : definition.Damage;
        int defense = preHardMode ? 12 : definition.Defense;
        float difficulty = context.Difficulty;
        life = windowsArithmetic ? (int)(life * (double)difficulty) : (int)(life * difficulty);
        double damageMultiplier = DamageMultiplier(difficulty, windowsArithmetic);
        damage = windowsArithmetic ? (int)(damage * damageMultiplier) : (int)(damage * (float)damageMultiplier);
        float knockBackResist = windowsArithmetic
            ? (float)(definition.KnockBackResist * Ramp(difficulty, 1f, 3f, .8f, true))
            : definition.KnockBackResist * (float)Ramp(difficulty, 1f, 3f, .8f, false);
        return new(new(definition.Width, definition.Height), definition.Scale, life, damage, defense)
        {
            KnockBackResist = knockBackResist
        };
    }

    // The original Windows x86 CLR carries wider expression results into integer conversions
    // and Math.Round. The two life ramps are stored as Single before the final product;
    // the single damage ramp is retained wider. These are source boundaries, not a global precision policy.
    private static double Ramp(float difficulty, float lower, float upper, float end, bool windowsArithmetic) =>
        windowsArithmetic
            ? 1d + ((double)end - 1d) * Math.Clamp(((double)difficulty - lower) / ((double)upper - lower), 0d, 1d)
            : 1f + (end - 1f) * Math.Clamp((difficulty - lower) / (upper - lower), 0f, 1f);

    private static double DamageMultiplier(float difficulty, bool windowsArithmetic) => difficulty <= 3f ? difficulty :
        windowsArithmetic ? 3d + ((double)difficulty - 3d) * ((double)5.3333335f - 3d) :
        3f + (difficulty - 3f) * (5.3333335f - 3f);

    private static float PlayerBalance(int players, bool windowsArithmetic)
    {
        float balance = 1f, increment = .35f;
        for (int player = 1; player < players; player++)
        {
            balance += increment;
            increment = windowsArithmetic ? (float)(increment + (1d - increment) / 3d) : increment + (1f - increment) / 3f;
        }
        if (balance > 8f) balance = windowsArithmetic ? (float)((balance * 2d + 8d) / 3d) : (balance * 2f + 8f) / 3f;
        return Math.Min(balance, 1000f);
    }
}
