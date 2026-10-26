namespace TerraRuntime.Gameplay.Npcs;

public readonly partial record struct VanillaNpcSpawnDefaults
{
    private static bool TryResolveQueenBee(in VanillaNpcDefinition definition,
        in VanillaNpcSpawnContext context, bool windowsArithmetic, out VanillaNpcSpawnDefaults defaults)
    {
        defaults = default;
        if (!context.IsValid) return false;
        float scale = definition.Scale;
        int width = definition.Width, height = definition.Height;
        if (context.GoodWorld) scale *= 1.2f;
        else if (context.TenthAnniversaryWorld) scale *= .5f;
        if (context.GoodWorld || context.TenthAnniversaryWorld)
        {
            width = (int)(windowsArithmetic ? width * (double)scale : width * scale);
            height = (int)(windowsArithmetic ? height * (double)scale : height * scale);
        }
        float difficulty = context.Difficulty;
        int life = windowsArithmetic ? (int)(definition.LifeMax * (double)difficulty) : (int)(definition.LifeMax * difficulty);
        double damageMultiplier = DamageMultiplier(difficulty, windowsArithmetic);
        int damage = windowsArithmetic ? (int)(definition.Damage * damageMultiplier) : (int)(definition.Damage * (float)damageMultiplier);
        float lifeRamp = (float)(Ramp(difficulty, 1f, 2f, .7f, windowsArithmetic) *
            Ramp(difficulty, 2f, 3f, .85f, windowsArithmetic));
        life = (int)Math.Round(windowsArithmetic ? life * (double)lifeRamp : life * lifeRamp);
        double damageRamp = Ramp(difficulty, 1f, 2f, .9f, windowsArithmetic);
        damage = (int)Math.Round(windowsArithmetic ? damage * damageRamp : damage * (float)damageRamp);
        if (difficulty >= 2f) life = (int)Math.Round(life * (double)PlayerBalance(context.ActivePlayers, windowsArithmetic));
        float knockback = windowsArithmetic
            ? (float)(definition.KnockBackResist * Ramp(difficulty, 1f, 3f, .8f, true))
            : definition.KnockBackResist * (float)Ramp(difficulty, 1f, 3f, .8f, false);
        if (!VanillaNpcMoneyDefaults1458.TryResolve(definition.Type, new((short)definition.Type.Value), difficulty, out float money)) return false;
        defaults = new(new(width,height),scale,Math.Max(6,life),damage,definition.Defense)
            { KnockBackResist=knockback,Difficulty=difficulty,VerifiedMoneyValue=money };
        return true;
    }
}
