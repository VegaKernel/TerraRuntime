namespace TerraRuntime.Gameplay.Npcs;

public readonly partial record struct VanillaNpcSpawnDefaults
{
    // NPC.SetDefaults(6/24/49/58), including Remix body/stat changes before ScaleStats.
    private static VanillaNpcSpawnDefaults ResolveEarlyHostiles(in VanillaNpcDefinition definition,
        in VanillaNpcSpawnContext context, bool windowsArithmetic)
    {
        var ordinary = definition;
        if (definition.Type.Value == 6 && (context.RemixWorld || context.GoodWorld))
        {
            float scale = definition.Scale * 1.3f;
            int width = (int)(windowsArithmetic ? definition.Width * (double)scale : definition.Width * scale);
            int height = (int)(windowsArithmetic ? definition.Height * (double)scale : definition.Height * scale);
            if (!context.GoodWorld && context.TenthAnniversaryWorld)
            {
                width = (int)(windowsArithmetic ? width * (double)scale : width * scale);
                height = (int)(windowsArithmetic ? height * (double)scale : height * scale);
            }
            ordinary = definition with { SourceHitboxAtSpawn = new(width, height), Scale = scale };
            if (context.RemixWorld)
                ordinary = ordinary with
                {
                    Damage = (int)(windowsArithmetic ? definition.Damage * (double)scale : definition.Damage * scale),
                    Defense = (int)(windowsArithmetic ? definition.Defense * (double)scale : definition.Defense * scale),
                    LifeMax = (int)(windowsArithmetic ? definition.LifeMax * (double)scale : definition.LifeMax * scale)
                };
        }
        return ResolveOrdinary(in ordinary, in context, windowsArithmetic) with { Difficulty = context.Difficulty };
    }
}
