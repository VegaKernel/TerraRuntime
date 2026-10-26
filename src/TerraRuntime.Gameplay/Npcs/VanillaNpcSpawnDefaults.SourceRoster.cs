using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

public readonly partial record struct VanillaNpcSpawnDefaults
{
    internal static bool TryResolveRemainingSourceRoster(in VanillaNpcDefinition definition,
        in VanillaNpcSpawnContext context, bool windowsArithmetic, out VanillaNpcSpawnDefaults defaults)
    {
        defaults = default;
        if (!context.IsValid || !VanillaNpcSourceDefaultRoster1458.TryGetCreationRow(definition.Type, out var row))
            return false;
        int width = (int)(windowsArithmetic ? row.Width * (double)row.Scale : row.Width * row.Scale);
        int height = (int)(windowsArithmetic ? row.Height * (double)row.Scale : row.Height * row.Scale);
        float scale = row.Scale, money = row.Money, knockback = row.KnockBack;
        int life = row.Life, damage = row.Damage, defense = row.Defense;
        int type = row.Type;
        bool variant = type == 183 && definition.SourceNetId.Value is -25 or -24;
        if (variant)
        {
            scale = definition.SourceNetId.Value == -25 ? 1.15f : .85f;
            if (context.GoodWorld) scale = (scale + scale * scale) / 2f;
            width = (int)(windowsArithmetic ? row.Width * (double)scale : row.Width * scale);
            height = (int)(windowsArithmetic ? row.Height * (double)scale : row.Height * scale);
            if (height is 16 or 32) height++;
        }
        if (type == 548)
        {
            if (context.WorldDifficulty is not float worldDifficulty || !float.IsFinite(worldDifficulty) ||
                worldDifficulty is < .5f or > 4f ||
                context.HardMode && (context.DownedAnyMechanicalBoss is null || context.DownedGolem is null)) return false;
            if (context.HardMode && context.DownedAnyMechanicalBoss == true) { life = 3000; defense = 18; }
            if (context.HardMode && context.DownedGolem == true) { life = 5000; defense = 20; }
            life *= worldDifficulty >= 3f ? 3 : worldDifficulty >= 2f ? 2 : 1;
        }
        if (!context.GoodWorld && context.TenthAnniversaryWorld)
        {
            if (type is 422 or 493 or 507 or 517) scale *= .5f;
            width = (int)(windowsArithmetic ? width * (double)scale : width * scale);
            height = (int)(windowsArithmetic ? height * (double)scale : height * scale);
        }
        bool scales = row.NeedsScaling || life > 5 && damage != 0 && !row.Friendly;
        float difficulty = scales ? context.Difficulty : 1f;
        if (scales)
        {
            if (difficulty >= 2f && context.HardMode && !row.NoHardmodeScaling && !row.Boss && life < 1000)
            {
                int budget = Math.Max(1, damage + defense + life / 4);
                int threshold = context.DownedPlantera ? 100 : 80;
                if (budget < threshold)
                {
                    int factor = threshold / budget;
                    damage = windowsArithmetic ? (int)(damage * (double)factor * .9f) : (int)(damage * factor * .9f);
                    if (!row.Projectile)
                    {
                        defense *= factor;
                        life = (int)((double)(life * factor) * 1.1d);
                        money = (int)((double)(money * factor) * .8d);
                    }
                }
            }
            if (!row.Projectile)
            {
                life = windowsArithmetic ? (int)(life * (double)difficulty) : (int)(life * difficulty);
                money = VanillaNpcMoneyDefaults1458.ScaleBaseline((int)money, difficulty);
            }
            double damageMultiplier = DamageMultiplier(difficulty, windowsArithmetic);
            damage = windowsArithmetic ? (int)(damage * damageMultiplier) : (int)(damage * (float)damageMultiplier);
            knockback = windowsArithmetic ? (float)(knockback * Ramp(difficulty, 1f, 3f, .8f, true))
                : knockback * (float)Ramp(difficulty, 1f, 3f, .8f, false);
            float expertLife = type is 393 or 394 or 395 or 472 or 492 ? .85f : type == 551 ? .75f : 1f;
            float masterLife = row.OldOnes ? .85f : 1f;
            float expertDamage = type == 472 ? .8f : type == 551 ? .65f : 1f;
            float lifeRamp = (float)(Ramp(difficulty, 1f, 2f, expertLife, windowsArithmetic) *
                Ramp(difficulty, 2f, 3f, masterLife, windowsArithmetic));
            life = (int)Math.Round(windowsArithmetic ? life * (double)lifeRamp : life * lifeRamp);
            double damageRamp = Ramp(difficulty, 1f, 2f, expertDamage, windowsArithmetic);
            damage = (int)Math.Round(windowsArithmetic ? damage * damageRamp : damage * (float)damageRamp);
            if (difficulty >= 2f)
            {
                double balance = type == 472 ? 1d + (PlayerBalance(context.ActivePlayers, windowsArithmetic) - 1d) * .5d
                    : type == 551 ? PlayerBalance(context.ActivePlayers, windowsArithmetic)
                    : row.OldOnes ? 1d + (PlayerBalance(context.ActivePlayers, windowsArithmetic) - 1d) * .8571428656578064d
                    : row.Invasion is -2 or -1 ? 1d + (context.ActivePlayers - 1) * .2d : 1d;
                life = (int)Math.Round(life * balance);
            }
            if (!row.Projectile) life = Math.Max(6, life);
        }
        if (variant)
        {
            defense = (int)(windowsArithmetic ? defense * (double)scale : defense * scale);
            damage = (int)(windowsArithmetic ? damage * (double)scale : damage * scale);
            life = (int)(windowsArithmetic ? life * (double)scale : life * scale);
            money = (int)(windowsArithmetic ? money * (double)scale : money * scale);
            knockback = windowsArithmetic ? (float)(knockback * (2d - scale)) : knockback * (2f - scale);
        }
        defaults = new(new(width, height), scale, life, damage, defense)
            { KnockBackResist = knockback, Difficulty = difficulty, VerifiedMoneyValue = money };
        return true;
    }
}
