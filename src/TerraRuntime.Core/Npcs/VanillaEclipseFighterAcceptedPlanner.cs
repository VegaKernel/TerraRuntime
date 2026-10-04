using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

/// <summary>Accepted AI_003 tails that must finish before terrain and outer NPC physics.</summary>
public interface INpcAiAcceptedWorldMotionPlanner
{
    bool TryPlanBeforeWorldMotion(in NpcSnapshot before, in NpcSnapshot accepted,
        Span<NpcAiProjectileIntent> shots, out int count, out NpcStateUpdate next);
}

internal static class VanillaEclipseFighterAcceptedPlanner
{
    public static bool IsSupported(NpcTypeId type) => type == VanillaNpcIds.Nailhead || type == VanillaNpcIds.DrManFly || type == VanillaNpcIds.Frankenstein;
    public static bool TryPlan(in NpcSnapshot before, in NpcSnapshot accepted, VanillaNpcBehaviorContext context,
        IVanillaNpcRandom random, IVanillaNpcProjectileEnvironment? environment,
        Span<NpcAiProjectileIntent> shots, out int count, out NpcStateUpdate next)
    {
        count = 0;
        next = new(accepted.Type, accepted.NetId, accepted.PositionX, accepted.PositionY,
            accepted.VelocityX, accepted.VelocityY, accepted.Target, accepted.Ai, accepted.Simulation);
        if (before.TypeIdentity != accepted.TypeIdentity || !IsSupported(accepted.TypeIdentity) || shots.Length < 5 ||
            !VanillaNpcDefinitionCatalog.TryGet(accepted.TypeIdentity, accepted.NetIdentity, out var definition) ||
            !definition.TryResolveHitbox(accepted.Simulation, out var body)) return false;
        if (accepted.TypeIdentity == VanillaNpcIds.Frankenstein)
        {
            bool discouraged = context.DayTime && !context.EclipseActive && before.PositionY < context.WorldSurfacePixels;
            if (!discouraged && accepted.Ai.Ai3 < 60f)
            {
                // Dedicated-server SoundEngine is inert, but AI_003 still consumes both source sound rolls.
                random.NextInt32(0, 1000);
                random.NextInt32(0, 500);
            }
            return true;
        }
        if (accepted.TypeIdentity == VanillaNpcIds.Nailhead)
        {
            var local = accepted.Simulation.LocalAi;
            float cooldown = local.Ai3 > 0f ? local.Ai3 - 1f : local.Ai3;
            if (before.Simulation.JustHit && cooldown <= 0f && random.NextInt32(0, 3) == 0)
            {
                cooldown = 30f;
                int volley = random.NextInt32(3, 6);
                Span<VanillaNpcTargetCandidate> targets = stackalloc VanillaNpcTargetCandidate[5];
                int targetCount = 0;
                if (environment is IVanillaNpcProjectileLineEnvironment line)
                {
                    for (int slot = 0; slot < byte.MaxValue && targetCount < volley; slot++)
                        if (context.TryFindCandidate((byte)slot, out var target) && target.Active && !target.Dead &&
                            line.CanHitLine(accepted.PositionX, accepted.PositionY, body.Width, body.Height,
                                target.CenterX - target.Width * .5f, target.CenterY - target.Height * .5f, (int)target.Width, (int)target.Height))
                            targets[targetCount++] = target;
                }
                else return false;
                if (targetCount > 1)
                    for (int i = 0; i < 100; i++)
                    {
                        int a = random.NextInt32(0, targetCount), b = a;
                        while (b == a) b = random.NextInt32(0, targetCount);
                        (targets[a], targets[b]) = (targets[b], targets[a]);
                    }
                float x = accepted.PositionX + body.Width * .5f, y = accepted.PositionY + body.Height * .5f;
                int damage = (int)(15f + Math.Clamp((accepted.Simulation.SpawnDifficulty ?? 1f) - 1f, 0f, 1f) * 10f);
                for (int i = 0; i < volley; i++)
                {
                    float speed = random.NextInt32(8, 13);
                    float vx = random.NextInt32(-100, 101), vy = random.NextInt32(-100, 101);
                    // The source's retained-NPC bias is always overwritten for targeted shots; it has no RNG effects.
                    bool finite = NormalizeNail(ref vx, ref vy, speed);
                    if (targetCount > 0)
                    {
                        var target = targets[--targetCount];
                        vx = target.CenterX - x;
                        vy = target.CenterY - y;
                        finite = NormalizeNail(ref vx, ref vy, speed);
                    }
                    if (finite) shots[count++] = new(VanillaProjectileIds.Nail, x, accepted.PositionY + body.Width / 4, vx, vy, damage, 1f);
                }
            }
            next = next with { Simulation = accepted.Simulation with { LocalAi = local with { Ai3 = cooldown } } };
            return true;
        }
        float timer = accepted.Ai.Ai1, mode = accepted.Ai.Ai2, velocityX = accepted.VelocityX;
        ushort targetSlot = accepted.Target;
        var simulation = accepted.Simulation;
        if (timer > 0f) timer--;
        if (before.Simulation.JustHit)
        {
            timer = 30f;
            mode = 0f;
        }
        if (simulation.Confused)
        {
            timer = 0f;
            mode = 0f;
        }
        if (mode > 0f)
        {
            if (context.TrySelectClosestTarget(in accepted, in definition, out var refresh) &&
                refresh.Target < byte.MaxValue && context.TryFindCandidate((byte)refresh.Target, out var target))
            {
                targetSlot = refresh.Target;
                simulation = simulation with { DirectionX = refresh.DirectionX, DirectionY = refresh.DirectionY };
                if (timer == 35f)
                {
                    float x = accepted.PositionX + body.Width * .5f, y = accepted.PositionY + body.Height * .5f;
                    float dx = target.CenterX - x;
                    float lead = MathF.Abs(dx) * random.NextInt32(10, 50) * .01f;
                    float vx = dx + random.NextInt32(-40, 41), vy = target.CenterY - y - lead + random.NextInt32(-40, 41);
                    if (ScaleAim(ref vx, ref vy, 7.5f)) shots[count++] = new(VanillaProjectileIds.DrManFlyFlask, x + vx, y + vy, vx, vy, 50, 0f);
                    mode = Category(vx, vy);
                }
            }
            if (accepted.VelocityY != 0f || timer <= 0f)
            {
                mode = 0f;
                timer = 0f;
            }
            else
            {
                velocityX *= .9f;
                simulation = simulation with { SpriteDirection = simulation.DirectionX };
            }
        }
        if (context.EclipseActive && mode <= 0f && accepted.VelocityY == 0f && timer <= 0f && targetSlot < byte.MaxValue &&
            context.TryFindCandidate((byte)targetSlot, out var preparation) && !preparation.Dead && environment is not null &&
            environment.CanHit(accepted.PositionX, accepted.PositionY, body.Width, body.Height,
                preparation.CenterX - preparation.Width * .5f, preparation.CenterY - preparation.Height * .5f, (int)preparation.Width, (int)preparation.Height) &&
            !(preparation.Stealth == 0f && preparation.ItemAnimation == 0))
        {
            float dx = preparation.CenterX - (accepted.PositionX + body.Width * .5f);
            float vx = dx + random.NextInt32(-40, 41), vy = preparation.CenterY - (accepted.PositionY + body.Height * .5f) - MathF.Abs(dx) * .1f + random.NextInt32(-40, 41);
            if ((float)Math.Sqrt(vx * vx + vy * vy) < 400f)
            {
                velocityX *= .5f;
                mode = Category(vx, vy);
                timer = 70f;
            }
        }
        if (mode <= 0f || !context.EclipseActive)
        {
            if (velocityX < -1f || velocityX > 1f)
            {
                if (accepted.VelocityY == 0f) velocityX *= .8f;
            }
            else if (velocityX < 1f && simulation.DirectionX == 1) velocityX = MathF.Min(1f, velocityX + .07f);
            else if (velocityX > -1f && simulation.DirectionX == -1) velocityX = MathF.Max(-1f, velocityX - .07f);
        }
        next = next with { VelocityX = velocityX, Target = targetSlot, Ai = accepted.Ai with { Ai1 = timer, Ai2 = mode }, Simulation = simulation };
        return true;
    }
    private static bool NormalizeNail(ref float x, ref float y, float speed)
    {
        float reciprocal = 1f / (float)Math.Sqrt(x * x + y * y);
        x *= reciprocal;
        y *= reciprocal;
        x *= speed;
        y *= speed;
        return float.IsFinite(x) && float.IsFinite(y);
    }
    private static bool ScaleAim(ref float x, ref float y, float speed)
    {
        float ratio = speed / (float)Math.Sqrt(x * x + y * y);
        x *= ratio;
        y *= ratio;
        return float.IsFinite(x) && float.IsFinite(y);
    }
    private static float Category(float x, float y)
    {
        if (MathF.Abs(y) > MathF.Abs(x) * 2f)
            return y > 0f ? 1f : 5f;
        if (MathF.Abs(x) > MathF.Abs(y) * 2f)
            return 3f;
        return y > 0f ? 2f : 4f;
    }
}
