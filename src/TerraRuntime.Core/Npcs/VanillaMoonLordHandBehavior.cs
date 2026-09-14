using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

// AI_078_MoonLordHands, TerrariaServer 1.4.5.8. The frame clock is a combat gate,
// and position clamping precedes the outer NPC movement pass.
internal static class VanillaMoonLordHandBehavior
{
    internal static int Phase(float timer, bool left, out int state, out int duration)
    {
        ReadOnlySpan<int> states = left ? [0, 1, 2, 0, 3] : [1, 0, 3, 0, 2];
        ReadOnlySpan<int> durations = left ? [50, 70, 330, 60, 90] : [70, 50, 90, 60, 330];
        int start = 0;
        for (int i = 0; i < states.Length; i++)
        {
            if (timer < start + durations[i])
            {
                state = states[i]; duration = durations[i];
                return (int)timer - start;
            }
            start += durations[i];
        }
        state = states[0]; duration = durations[0];
        return 0;
    }

    internal static bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition,
        in NpcSnapshot root, VanillaNpcBehaviorContext context, out NpcStateUpdate next)
    {
        bool left = npc.Ai.Ai2 == 0f;
        float side = left ? -1f : 1f;
        float cx = npc.PositionX + definition.Width * .5f, cy = npc.PositionY + definition.Height * .5f;
        float rootX = root.PositionX + 23f, rootY = root.PositionY + 33f;
        float vx = npc.VelocityX, vy = npc.VelocityY;
        NpcAiState ai = npc.Ai, local = npc.Simulation.LocalAi;
        bool retired = ai.Ai0 == -2f;
        float timer = ai.Ai1 + 1f;
        if (retired ? timer >= 32f || timer < 0f : timer >= 600f) timer = 0f;
        int elapsed = Phase(timer, left, out int state, out int duration);
        if (retired) state = -2;
        ai = ai with { Ai0 = state, Ai1 = timer };
        int frameGoal = 0;
        ushort target = npc.Target;

        if (state is -2 or 0)
        {
            if (!retired) { frameGoal = 21; local = local with { Ai1 = MathF.Max(0f, local.Ai1 - .05f) }; }
            float dx = rootX + 350f * side - cx, dy = rootY - 100f - cy;
            float distance = Length(dx, dy);
            if (distance > 20f)
            {
                float inverse = 1f / distance;
                vx = FlyAxis(vx, dx * inverse * 6f);
                vy = FlyAxis(vy, dy * inverse * 6f);
            }
        }
        else if (state == 1)
        {
            float radius = elapsed >= 56 ? MathF.Max(0f, local.Ai1 - .07f)
                : MathF.Min(.75f, local.Ai1 + (elapsed >= 28 ? .05f : .02f));
            float angle = local.Ai0;
            if (elapsed < 56)
            {
                float phase = MathF.PI * 2f * (elapsed % 28) / 28f - MathF.PI / 2f;
                angle = (float)Math.Atan2((float)Math.Sin(phase) * 66f, (float)Math.Cos(phase) * 30f);
            }
            local = local with { Ai0 = angle, Ai1 = radius };
        }
        else if (state == 2)
        {
            local = local with { Ai1 = MathF.Max(0f, local.Ai1 - .05f) };
            float originX = (220f * side + rootX) + side * 100f;
            float originY = (-60f + rootY) - 50f;
            if (elapsed < 30 || elapsed >= 300)
                Approach(originX - cx, originY - cy, 8f, .2f, true, ref vx, ref vy);
            else if (elapsed < 210)
            {
                frameGoal = 7;
                float amount = (elapsed - 30f) / 180f;
                Approach(Smooth(originX, originX + 400f * side, amount) - cx,
                    Smooth(originY, originY - 60f, amount) - cy, 20f, .5f, false, ref vx, ref vy);
            }
            else if (elapsed < 292)
            {
                frameGoal = elapsed < 282 ? 0 : elapsed < 287 ? 7 : 14;
                vx *= .9f; vy *= .9f;
            }
            else
            {
                frameGoal = 21;
                float amount = 1f - (elapsed - 270f) / 30f;
                Approach(Smooth(originX, originX + 400f * side, amount) - cx,
                    Smooth(originY, originY - 60f, amount) - cy, 14f, .1f, false, ref vx, ref vy);
            }
            if (elapsed >= 300) frameGoal = 21;
        }
        else if (state == 3)
        {
            if (elapsed == 0 && context.TrySelectClosestTarget(in npc, in definition, out var chosen) && chosen.HasTarget)
                target = chosen.Target;
            if (target < byte.MaxValue && context.TryFindCandidate((byte)target, out var player) && elapsed < duration - 19)
            {
                float angle = (float)Math.Atan2(player.CenterY + player.VelocityY * 20f - cy,
                    player.CenterX + player.VelocityX * 20f - cx);
                local = local with { Ai0 = AngleLerp(local.Ai0, angle), Ai1 = local.Ai1 + .05f };
            }
            local = local with { Ai1 = MathF.Min(1f, local.Ai1) };
        }

        float edgeX = (220f * side + rootX) + side * 110f;
        float edgeY = (-60f + rootY) - 150f;
        float clampedX = Math.Clamp(cx + vx, MathF.Min(edgeX, edgeX + side * 370f), MathF.Max(edgeX, edgeX + side * 370f));
        float clampedY = Math.Clamp(cy + vy, edgeY, edgeY + 150f);
        float x = npc.PositionX, y = npc.PositionY;
        if (clampedX != cx + vx || clampedY != cy + vy)
        {
            x = clampedX - vx - definition.Width * .5f;
            y = clampedY - vy - definition.Height * .5f;
        }
        double frame = npc.Simulation.FrameCounter;
        if (frameGoal > frame) frame++;
        if (frameGoal < frame) frame--;
        var simulation = npc.Simulation with
        {
            LocalAi = local, FrameCounter = Math.Clamp(frame, 0d, 21d), SpriteDirection = (int)side,
            DontTakeDamage = retired || npc.Simulation.FrameCounter >= 21d,
            DamageOverride = retired ? 0 : npc.Simulation.DamageOverride,
            NoGravity = true, NoTileCollide = true, JustHit = false
        };
        next = new NpcStateUpdate(npc.Type, npc.NetId, x, y, vx, vy, target, ai, simulation);
        return true;
    }

    internal static int PlanProjectiles(in NpcSnapshot before, in NpcStateUpdate after,
        Span<NpcAiProjectileIntent> destination, IVanillaNpcRandom random)
    {
        if (after.Ai.Ai0 == -2f) return 0;
        bool left = before.Ai.Ai2 == 0f;
        float side = left ? -1f : 1f;
        int elapsed = Phase(after.Ai.Ai1, left, out int state, out int duration);
        float x = before.PositionX + 23f, y = before.PositionY + 33f;
        float vx, vy, knockback = 0f;
        int damage = 30;
        ProjectileTypeId type;
        ProjectileAiState projectileAi = default;
        if (state == 1 && elapsed >= 28 && elapsed < 56 && elapsed % 4 == 0)
        {
            if (destination.IsEmpty) return 1;
            EyeOffset(after.Simulation.LocalAi, out float dx, out float dy);
            float inverse = 1f / Length(dx, dy);
            dx *= inverse; dy *= inverse;
            float reach = Length(30f, 66f) * .4f;
            x = (x + dx * reach) - side; y = (y + dy * reach) + 3f;
            vx = dx * 8f; vy = dy * 8f;
            type = VanillaProjectileIds.PhantasmalEye;
            float angularVelocity = (MathF.PI * 2f * (float)random.NextDouble() - MathF.PI) / 30f + MathF.PI / 180f * side;
            projectileAi = new ProjectileAiState(0f, angularVelocity, 0f);
        }
        else if (state == 2 && elapsed >= 30 && elapsed < 210 && (elapsed - 30) % 30 == 0)
        {
            if (destination.IsEmpty) return 1;
            int volley = (elapsed - 30) / 30;
            vx = (5f * side + (volley - 3.5f) * side * 3f) * 1.2f;
            vy = (-8f + (volley - 4.5f)) * 1.2f;
            type = VanillaProjectileIds.PhantasmalSphere;
            damage = 40; knockback = 1f;
            projectileAi = new ProjectileAiState(0f, before.Handle.Slot, 0f);
        }
        else if (state == 3 && (elapsed == duration - 14 || elapsed == duration - 7 || elapsed == duration))
        {
            if (destination.IsEmpty) return 1;
            EyeOffset(after.Simulation.LocalAi, out float dx, out float dy);
            x += dx; y += dy;
            vx = (float)Math.Cos(after.Simulation.LocalAi.Ai0) * 8f;
            vy = (float)Math.Sin(after.Simulation.LocalAi.Ai0) * 8f;
            type = VanillaProjectileIds.PhantasmalBolt;
        }
        else return 0;
        // NewProjectile takes a center, while the runtime intent stores the resulting top-left position.
        if (!TerraRuntime.Gameplay.Projectiles.VanillaDefinitionCatalog.TryGet(type, out var definition)) return 0;
        destination[0] = new NpcAiProjectileIntent(type, x - definition.Width * .5f, y - definition.Height * .5f,
            vx, vy, damage, knockback) { InitialAi = projectileAi };
        return 1;
    }

    private static void EyeOffset(NpcAiState local, out float x, out float y)
    {
        x = (float)Math.Cos(local.Ai0); y = (float)Math.Sin(local.Ai0);
        float inverse = 1f / Length(x, y);
        x *= inverse; y *= inverse;
        float width = 30f * local.Ai1, height = 66f * local.Ai1;
        if (width == 0f && height == 0f) { x = y = 0f; return; }
        float sizeInverse = 1f / Length(width, height);
        x *= 1f / (width * sizeInverse); y *= 1f / (height * sizeInverse);
        inverse = 1f / Length(x, y);
        x = (x * inverse) * width / 2f; y = (y * inverse) * height / 2f;
    }

    private static float Length(float x, float y) => (float)Math.Sqrt(x * x + y * y);

    private static float FlyAxis(float velocity, float desired)
    {
        float changed = velocity;
        if (changed < desired) { changed += .3f; if (changed < 0f && desired > 0f) changed += .3f; }
        else if (changed > desired) { changed -= .3f; if (changed > 0f && desired < 0f) changed -= .3f; }
        return velocity + (changed - velocity) * .5f;
    }

    private static void Approach(float dx, float dy, float speed, float amount, bool smooth, ref float vx, ref float vy)
    {
        float distance = Length(dx, dy);
        if (distance == 0f) return;
        float inverse = 1f / distance;
        float x = dx * inverse * MathF.Min(speed, distance), y = dy * inverse * MathF.Min(speed, distance);
        vx = smooth ? Smooth(vx, x, amount) : vx + (x - vx) * amount;
        vy = smooth ? Smooth(vy, y, amount) : vy + (y - vy) * amount;
    }

    private static float Smooth(float start, float end, float amount)
    {
        double t = Math.Clamp(amount, 0f, 1f);
        if (t == 0d) return start;
        if (t == 1d) return end;
        // Zero-tangent Hermite in double precision, as used by the original FNA SmoothStep.
        return (float)((2d * start - 2d * end) * (t * t * t) + (3d * end - 3d * start) * (t * t) + start);
    }

    private static float AngleLerp(float current, float target)
    {
        float other = target < current ? target + MathF.PI * 2f : target - MathF.PI * 2f;
        if (MathF.Abs(other - current) < MathF.Abs(target - current)) target = other;
        float result = current + (target - current) * .5f;
        result %= MathF.PI * 2f;
        if (result <= -MathF.PI) result += MathF.PI * 2f;
        if (result > MathF.PI) result -= MathF.PI * 2f;
        return result;
    }
}
