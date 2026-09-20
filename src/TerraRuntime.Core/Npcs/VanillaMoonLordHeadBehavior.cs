using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Core.Npcs;

// TerrariaServer 1.4.5.8 AI_079. Head placement precedes aiming and projectile creation;
// localAI[3] is the authoritative eyelid damage gate, not an owner identifier.
internal static class VanillaMoonLordHeadBehavior
{
    internal static int Phase(float timer, out int state, out int duration)
    {
        ReadOnlySpan<int> states = [3, 0, 2, 3, 1];
        ReadOnlySpan<int> durations = [180, 30, 435, 180, 375];
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
        in NpcSnapshot root, VanillaNpcBehaviorContext context, IVanillaNpcRandom random, out NpcStateUpdate next)
    {
        float cx = root.PositionX + 23f, cy = root.PositionY + 33f - 400f;
        float x = cx - definition.Width * .5f, y = cy - definition.Height * .5f;
        var ai = npc.Ai;
        var local = npc.Simulation.LocalAi;
        float timer = ai.Ai1 + 1f;
        if (timer >= 1200f) timer = 0f;
        int elapsed = Phase(timer, out int state, out int duration);
        ai = ai with { Ai1 = timer, Ai0 = ai.Ai0 >= 0f ? state : ai.Ai0 };
        ushort target = npc.Target;
        var simulation = npc.Simulation with
        {
            DontTakeDamage = local.Ai3 >= 15f,
            NoGravity = true, NoTileCollide = true, JustHit = false
        };
        int mouthGoal = 0, lidGoal = 0;
        if (ai.Ai0 == -2f && root.Ai.Ai0 == 2f)
        {
            next = new NpcStateUpdate(npc.Type, npc.NetId, x, y, 0, 0, target, ai with { Ai0 = -3f }, simulation);
            return true;
        }
        if (ai.Ai0 is -2f or -3f)
        {
            simulation = simulation with { DontTakeDamage = true, DamageOverride = 0 };
            float deathFrame = ai.Ai2 + 1f;
            if (deathFrame >= 32f || deathFrame < 0f) deathFrame = 0f;
            ai = ai with { Ai2 = deathFrame };
            if (ai.Ai0 == -3f && local.Ai2 < 14f) local = local with { Ai2 = local.Ai2 + 1f };
        }
        else
        {
            // TargetClosest uses the already attached head center, with original target retention.
            if (state == 0 || state == 1 && elapsed == 3 || state == 3 && elapsed == 1)
            {
                var attached = npc with { PositionX = x, PositionY = y };
                if (context.TrySelectClosestTarget(in attached, in definition, out var chosen) && chosen.HasTarget)
                    target = chosen.Target;
                if (target >= byte.MaxValue) target = 0;
            }
            var player = Player(context, target);
            if (state == 0)
            {
                lidGoal = 15;
                float dx = player.CenterX - cx, dy = player.CenterY - cy - -22f;
                float closeness = MathF.Min(1f, Length(dx, dy) / 500f);
                local = local with { Ai0 = Angle(dx, dy), Ai1 = MathF.Min(1f, (1f - closeness) * 2f),
                    Ai2 = local.Ai2 + (1f - local.Ai2) * .2f };
            }
            else if (state == 1)
            {
                if (elapsed < 180)
                {
                    local = local with { Ai1 = MathF.Max(0f, local.Ai1 - .05f) };
                    // Dedicated-server Dust.NewDust exits, but these angle draws precede it.
                    if (elapsed >= 60)
                        for (int i = 0; i < (elapsed >= 120 ? 2 : 1); i++) _ = random.NextDouble();
                }
                else if (elapsed < duration - 15)
                {
                    if (elapsed == 180)
                    {
                        Laser(cx, cy, player, out float vx, out float vy, out float sign);
                        ai = ai with { Ai2 = (Angle(vx, vy) + MathF.PI * 3f) * sign };
                    }
                    float sign2 = ai.Ai2 >= 0f ? 1f : -1f;
                    float angle = MathF.Abs(ai.Ai2) + MathF.PI * -3f;
                    angle += sign2 * (MathF.PI * 2f) / 540f;
                    local = local with { Ai0 = angle, Ai1 = MathF.Min(1f, local.Ai1 + .05f) };
                    ai = ai with { Ai2 = (angle + MathF.PI * 3f) * sign2 };
                }
                else
                {
                    local = local with { Ai1 = MathF.Max(0f, local.Ai1 - .07f) };
                    lidGoal = 15;
                }
            }
            else if (state == 2) { mouthGoal = 14; lidGoal = 15; }
            else if (state == 3)
            {
                if (elapsed < duration - 19)
                    local = local with
                    {
                        Ai0 = VanillaMoonLordHandBehavior.AngleLerp(local.Ai0,
                            Angle(player.CenterX + player.VelocityX * 20f - cx, player.CenterY + player.VelocityY * 20f - cy)),
                        Ai1 = local.Ai1 + .05f
                    };
                local = local with { Ai1 = MathF.Min(1f, local.Ai1) };
            }
        }
        float mouth = ApproachFrame(local.Ai2, mouthGoal);
        float lid = ApproachFrame(local.Ai3, lidGoal);
        mouth = Math.Clamp(mouth, 0f, 14f);
        // Preserve AI_079's asymmetric out-of-range correction (it writes localAI[2]).
        if (lid < 0f) mouth = 0f;
        if (lid > 15f) mouth = 15f;
        simulation = simulation with { LocalAi = local with { Ai2 = mouth, Ai3 = lid } };
        next = new NpcStateUpdate(npc.Type, npc.NetId, x, y, 0, 0, target, ai, simulation);
        return true;
    }

    internal static int PlanProjectiles(in NpcSnapshot source, in NpcStateUpdate proposed,
        VanillaNpcBehaviorContext context, IVanillaNpcSolidTileEnvironment? solidTiles,
        IVanillaNpcRandom random, Span<NpcAiProjectileIntent> destination)
    {
        if (proposed.Ai.Ai0 < 0f) return 0;
        int elapsed = Phase(proposed.Ai.Ai1, out int state, out int duration);
        float cx = proposed.PositionX + 19f, cy = proposed.PositionY + 28f;
        var player = Player(context, proposed.Target);
        // AI_079's final Deathray wind-down emits a boulder burst only in getGoodWorld. The local scale
        // crosses below zero once per attack cycle; testing the incoming value preserves that strict source gate.
        if (state == 1 && elapsed >= duration - 15 && source.Simulation.LocalAi.Ai1 < .07f &&
            context.GoodWorld && solidTiles is not null)
        {
            const int burstCount = 30;
            if (destination.Length < burstCount)
                return destination.Length + 1;

            int tileX = (int)(cx / 16f);
            int tileY = (int)(cy / 16f);
            int count = 0;
            for (int i = 0; i < burstCount; i++)
            {
                // Source asks WorldGen.SolidTile inside the loop. Keep that order so no RNG draw occurs for
                // any solid observation, including a world mutation performed by an intervening extension.
                if (solidTiles.IsSolidTile(tileX, tileY))
                    continue;
                destination[count++] = Projectile(VanillaProjectileIds.MoonBoulder, cx, cy,
                    random.NextInt32(-1599, 1600) * .01f,
                    random.NextInt32(-1599, 1) * .01f,
                    70, default) with { KnockBack = 10f };
            }
            return count;
        }
        if (state == 1 && elapsed == 180)
        {
            if (destination.IsEmpty) return 1;
            Laser(cx, cy, player, out float vx, out float vy, out float sign);
            destination[0] = Projectile(VanillaProjectileIds.PhantasmalDeathray, cx, cy, vx, vy, 75,
                new ProjectileAiState(sign * (MathF.PI * 2f) / 540f, source.Handle.Slot, 0f));
            return 1;
        }
        if (state == 2 && elapsed == 0)
        {
            float mouthY = cy + 216f;
            float dx = player.CenterX - cx, dy = player.CenterY - mouthY;
            if (dx != 0f || dy != 0f) Normalize(ref dx, ref dy);
            int count = 0;
            for (int i = 0; i < context.CandidateCount; i++)
            {
                var candidate = context.GetCandidateAt(i);
                if (!candidate.Active || candidate.Dead || Length(candidate.CenterX - cx, candidate.CenterY - mouthY) > 3000f) continue;
                if (count >= destination.Length) return count + 1;
                destination[count++] = Projectile(VanillaProjectileIds.MoonLeech, cx, mouthY, dx, dy, 0,
                    new ProjectileAiState(source.Handle.Slot + 1f, candidate.Slot, 0f));
            }
            return count;
        }
        if (state == 3 && (elapsed == duration - 14 || elapsed == duration - 7))
        {
            if (destination.IsEmpty) return 1;
            var local = proposed.Simulation.LocalAi;
            VanillaMoonLordHandBehavior.EyeOffset(local, out float dx, out float dy, 27f, 59f);
            destination[0] = Projectile(VanillaProjectileIds.PhantasmalBolt, cx + dx, cy + dy,
                (float)Math.Cos(local.Ai0) * 8f, (float)Math.Sin(local.Ai0) * 8f, 30, default);
            return 1;
        }
        return 0;
    }

    private static NpcAiProjectileIntent Projectile(ProjectileTypeId type, float cx, float cy, float vx, float vy,
        int damage, ProjectileAiState ai)
    {
        TerraRuntime.Gameplay.Projectiles.VanillaDefinitionCatalog.TryGet(type, out var definition);
        return new NpcAiProjectileIntent(type, cx - definition.Width * .5f, cy - definition.Height * .5f, vx, vy, damage, 0f)
        { InitialAi = ai };
    }

    private static VanillaNpcTargetCandidate Player(VanillaNpcBehaviorContext context, ushort target) =>
        context.TryFindCandidate((byte)target, out var player) ? player : new VanillaNpcTargetCandidate((byte)target,
            VanillaPlayerHitboxFacts.BaseWidth * .5f, VanillaPlayerHitboxFacts.BaseHeight * .5f, 0, false, false, false, false);

    private static void Laser(float cx, float cy, VanillaNpcTargetCandidate player, out float vx, out float vy, out float sign)
    {
        float dx = player.CenterX - cx, dy = player.CenterY - cy;
        Normalize(ref dx, ref dy);
        sign = dx < 0f ? 1f : -1f;
        float angle = -sign * (MathF.PI * 2f) / 6f;
        float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
        vx = dx * cos - dy * sin; vy = dx * sin + dy * cos;
    }

    private static void Normalize(ref float x, ref float y) { float inverse = 1f / Length(x, y); x *= inverse; y *= inverse; }
    private static float Length(float x, float y) => (float)Math.Sqrt(x * x + y * y);
    private static float Angle(float x, float y) => (float)Math.Atan2(y, x);
    private static float ApproachFrame(float frame, int goal)
    {
        if (frame < goal) frame++;
        if (frame > goal) frame--;
        return frame;
    }
}
