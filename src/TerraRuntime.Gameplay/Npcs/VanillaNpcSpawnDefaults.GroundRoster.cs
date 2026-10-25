using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

public readonly partial record struct VanillaNpcSpawnDefaults
{
    internal static bool TryResolveGroundRoster(in VanillaNpcDefinition definition,
        in VanillaNpcSpawnContext context, bool windowsArithmetic, out VanillaNpcSpawnDefaults defaults)
    {
        defaults = default;
        if (!VanillaNpcGroundRoster1458.TryGetSource(definition.Type, new((short)definition.Type.Value), out var source))
            return false;
        float money = VanillaNpcGroundRoster1458.BaselineMoney(source.Type);

        bool variant = definition.SourceNetId.Value < 0;
        float scale = variant ? definition.Scale : source.Scale;
        if (variant && context.GoodWorld)
            scale = (scale + scale * scale) / 2f;
        int width = (int)(windowsArithmetic ? source.BaseWidth * (double)scale : source.BaseWidth * scale);
        int height = (int)(windowsArithmetic ? source.BaseHeight * (double)scale : source.BaseHeight * scale);
        if (variant && height is 16 or 32) height++;
        int life = source.LifeMax, damage = source.Damage, defense = source.Defense;

        // NPC.SetDefaults type181 applies Remix's stats before its final body scaling. Good's
        // dedicated adjustment has no positive AI003/007 member; Tenth's final multiply still runs
        // even for types whose visual scale did not change (and therefore scales their body twice).
        if (source.Type.Value == 181 && context.RemixWorld)
        {
            scale = 1.1f;
            width = (int)(windowsArithmetic ? source.BaseWidth * (double)scale : source.BaseWidth * scale);
            height = (int)(windowsArithmetic ? source.BaseHeight * (double)scale : source.BaseHeight * scale);
            life = (int)((windowsArithmetic ? life * (double)scale : life * scale) * 1.2d);
            damage = (int)((windowsArithmetic ? damage * (double)scale : damage * scale) * 1.2d);
            defense = (int)((windowsArithmetic ? defense * (double)scale : defense * scale) * 1.2d);
            money = (int)((windowsArithmetic ? money * (double)scale : money * scale) * 1.2d);
        }
        if (!context.GoodWorld && context.TenthAnniversaryWorld)
        {
            width = (int)(windowsArithmetic ? width * (double)scale : width * scale);
            height = (int)(windowsArithmetic ? height * (double)scale : height * scale);
        }

        bool scales = source.AiStyle.Value == 3 && source.Damage != 0 && source.LifeMax > 5 &&
            !VanillaNpcChaseability1458.FriendlyAtSpawn(source.Type.Value);
        float difficulty = scales ? context.Difficulty : 1f;
        if (scales)
        {
            if (difficulty >= 2f && context.HardMode && life < 1000)
            {
                int budget = Math.Max(1, damage + defense + life / 4);
                int threshold = context.DownedPlantera ? 100 : 80;
                if (budget < threshold)
                {
                    int factor = threshold / budget;
                    damage = windowsArithmetic ? (int)(damage * (double)factor * .9f) : (int)(damage * factor * .9f);
                    defense *= factor;
                    life = (int)((double)(life * factor) * 1.1d);
                    money = (int)((double)(money * factor) * .8d);
                }
            }
            life = windowsArithmetic ? (int)(life * (double)difficulty) : (int)(life * difficulty);
            money = VanillaNpcMoneyDefaults1458.ScaleBaseline((int)money, difficulty);
            double damageFactor = DamageMultiplier(difficulty, windowsArithmetic);
            damage = windowsArithmetic ? (int)(damage * damageFactor) : (int)(damage * (float)damageFactor);

            bool moon = source.Type.Value is >= 305 and <= 314 or 326 or >= 338 and <= 340 or
                342 or 343 or >= 348 and <= 351;
            float lifeTweak = moon ? .75f : source.Type.Value == 471 ? .85f : 1f;
            float damageTweak = moon ? .75f : 1f;
            float rampLife = (float)Ramp(difficulty, 1f, 2f, lifeTweak, windowsArithmetic);
            life = (int)Math.Round(windowsArithmetic ? life * (double)rampLife : life * rampLife);
            double rampDamage = Ramp(difficulty, 1f, 2f, damageTweak, windowsArithmetic);
            damage = (int)Math.Round(windowsArithmetic ? damage * rampDamage : damage * (float)rampDamage);
            if (difficulty >= 2f)
            {
                double multiplayer = moon ? 1d + (context.ActivePlayers - 1) * .2d :
                    source.Type.Value == 471 ? 1d + (PlayerBalance(context.ActivePlayers, windowsArithmetic) - 1d) * (2d / 3d) : 1d;
                life = (int)Math.Round(life * multiplayer);
            }
            life = Math.Max(6, life);
        }
        float knockback = windowsArithmetic
            ? (float)(source.KnockBackResist * Ramp(difficulty, 1f, 3f, .8f, true))
            : source.KnockBackResist * (float)Ramp(difficulty, 1f, 3f, .8f, false);

        if (variant)
        {
            // These 27 variants retain the canonical ScaleStats result before their own truncation;
            // unlike Slime net variants they do not run ScaleStats a second time.
            int net = definition.SourceNetId.Value;
            defense = (int)(windowsArithmetic ? defense * (double)scale : defense * scale);
            if (net is -14 or -15)
            {
                // The 1.1 double tail is applied before integer truncation for these two variants.
                damage = (int)((windowsArithmetic ? damage * (double)scale : damage * scale) * 1.1d);
                life = (int)((windowsArithmetic ? life * (double)scale : life * scale) * 1.1d);
            }
            else
            {
                damage = (int)(windowsArithmetic ? damage * (double)scale : damage * scale);
                life = (int)(windowsArithmetic ? life * (double)scale : life * scale);
            }
            if (net == -15) { life = 400; height = 44; }
            if (net != -13) knockback = windowsArithmetic ? (float)(knockback * (2d - scale)) : knockback * (2f - scale);
            money = (int)(windowsArithmetic ? money * (double)scale : money * scale);
        }
        defaults = new(new(width, height), scale, life, damage, defense)
        { KnockBackResist = knockback, Difficulty = difficulty, VerifiedMoneyValue = money };
        return true;
    }
}
