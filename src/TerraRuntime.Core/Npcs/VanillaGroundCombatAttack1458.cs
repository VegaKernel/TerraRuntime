using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Core.Npcs;

// AI003's shared ranged tail, after target/stuck processing and before terrain
// jumps and outer physics. It only plans state and shots on the caller's preview.
internal static class VanillaGroundCombatAttack1458
{
    internal static bool TryPlan(in NpcSnapshot before, in NpcStateUpdate common,
        in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context, IVanillaNpcRandom random,
        IVanillaNpcProjectileEnvironment environment, Span<NpcAiProjectileIntent> shots,
        out int count, out NpcStateUpdate next)
    {
        count = 0; next = default;
        int type = before.Type;
        if (!VanillaGroundCombatCohort1458.IsRanged(before.TypeIdentity) || shots.Length < 1 ||
            !definition.TryResolveHitbox(common.Simulation, out var body) || environment is null)
            return false;
        if (type == 389)
            return TryPlanGigaZapper(in before, in common, in body, random, shots, out count, out next);
        bool moving = type is 381 or 382 or 520;
        var line = environment as IVanillaNpcProjectileLineEnvironment;
        if (moving && line is null) return false;
        float windup = type is 379 or 380 or 381 or 382 ? 80f : type == 481 ? 100f : type == 520 ? 15f : 70f;
        float fireAt = (int)windup / 2;
        float timer = common.Ai.Ai1, mode = common.Ai.Ai2, stuck = common.Ai.Ai3;
        float x = common.VelocityX, y = common.VelocityY;
        ushort target = common.Target;
        int direction = common.Simulation.DirectionX, vertical = common.Simulation.DirectionY;
        int sprite = common.Simulation.SpriteDirection;
        NpcAiState local = common.Simulation.LocalAi;
        bool sync = common.RequiresImmediateSync;
        if (timer > 0f) timer--;
        if (before.Simulation.JustHit) { timer = 30f; mode = 0f; sync = true; }
        if (common.Simulation.Confused) { timer = 0f; mode = 0f; }
        if (mode > 0f)
        {
            var actor = before with { Target = target, Ai = common.Ai,
                Simulation = common.Simulation with { DirectionX = direction, DirectionY = vertical } };
            if (!context.TrySelectRawClosestTarget(in actor, in definition, out var refresh) || !refresh.HasTarget ||
                !context.TryCaptureRawPlayer((byte)refresh.Target, out var captured) || !context.IsRawPlayerCurrent(in captured))
                return false;
            var raw = captured.Facts;
            if (!raw.IsValid) return false;
            bool retain = raw.Dead || raw.NoAggro && direction != 0 || raw.ItemAnimation == 0 && raw.Aggro < 0 && target < byte.MaxValue;
            int facing = retain ? direction : (int)raw.PositionX + raw.Width / 2 < common.PositionX + body.Width / 2 ? -1 : 1;
            int facingY = retain ? vertical : (int)raw.PositionY + raw.Height <= common.PositionY + body.Height ? -1 : 1;
            if (common.Simulation.Confused) facing = -facing;
            if (!common.Simulation.CollideX && !common.Simulation.CollideY && (target != refresh.Target || direction != facing || vertical != facingY)) sync = true;
            target = refresh.Target; direction = facing; vertical = facingY;
            if (timer == fireAt)
            {
                float centerX = common.PositionX + body.Width * .5f;
                float centerY = type == 520 ? common.PositionY + 20f : common.PositionY + body.Height * .5f + (type == 481 ? -14f : type is 381 or 382 ? 6f : 0f);
                float dx = raw.PositionX + raw.Width * .5f - centerX;
                float lift = moving ? 0f : type is 379 or 380 ? Math.Abs(dx) * .1f : type == 481
                    ? Math.Abs(dx) * random.NextInt32(-10, 11) * .0035f
                    : Math.Abs(dx) * random.NextInt32(10, 50) * .01f;
                float dy = raw.PositionY + raw.Height * .5f - centerY - lift;
                if (moving)
                {
                    dx += random.NextInt32(-100, 101) * .4f; dy += random.NextInt32(-100, 101) * .4f;
                    dx *= random.NextInt32(85, 116) * .01f; dy *= random.NextInt32(85, 116) * .01f;
                    if (type == 520)
                    {
                        dx += random.NextInt32(-100, 101) * .6f; dy += random.NextInt32(-100, 101) * .6f;
                        dx *= random.NextInt32(85, 116) * .015f; dy *= random.NextInt32(85, 116) * .015f;
                    }
                }
                else
                {
                    float jitter = type == 481 ? .4f : 1f;
                    dx += random.NextInt32(-40, 41) * jitter; dy += random.NextInt32(-40, 41) * jitter;
                }
                float speed = type is 379 or 380 or 381 ? 11f : type == 382 ? 7f : type is 481 or 520 ? 8f : 7f;
                if (!Normalize(ref dx, ref dy, speed)) return false;
                int projectile = type is 379 or 380 ? 81 : type == 381 ? 436 : type == 382 ? 438 : type == 481 ? 508 : type == 520 ? 592 : 471;
                int damage = type is 379 or 380 ? 40 : type == 381 ? 24 : type == 382 ? 30 : type == 481 ? 18 : type == 520 ? 35 : 15;
                if (type is 381 or 382)
                {
                    if (before.Simulation.SpawnDifficulty is not { } difficulty || !float.IsFinite(difficulty)) return false;
                    damage = (int)(damage + (damage * .8f - damage) * Math.Clamp(difficulty - 1f, 0f, 1f));
                }
                if (!VanillaDefinitionCatalog.TryGet(new(projectile), out var shape)) return false;
                // NewProjectile receives a center, then subtracts its actual post-SetDefaults body.
                shots[0] = new(new(projectile), centerX + dx - shape.Width * .5f, centerY + dy - shape.Height * .5f, dx, dy, damage, 0f);
                count = 1; mode = Aim(dx, dy); sync = true;
            }
            if (y != 0f || timer <= 0f) { mode = 0f; timer = 0f; }
            else if (!moving) { x *= .9f; sprite = direction; }
        }
        if (!ReadTarget(target, context, out var current)) return false;
        if ((mode <= 0f || moving) && y == 0f && timer <= 0f && !current.Dead)
        {
            bool visible = type == 520
                ? line!.CanHitLine(common.PositionX + body.Width * .5f, common.PositionY + 20f, 0, 0,
                    current.PositionX, current.PositionY, current.Width, current.Height)
                : environment.CanHit(common.PositionX, common.PositionY, body.Width, body.Height,
                    current.PositionX, current.PositionY, current.Width, current.Height);
            if (current.Stealth is not { } stealth) return false;
            if (stealth == 0f && current.ItemAnimation == 0) visible = false;
            if (visible)
            {
                float aimX = current.PositionX + current.Width * .5f - (common.PositionX + body.Width * .5f);
                float aimY = current.PositionY + current.Height * .5f - (common.PositionY + body.Height * .5f) - Math.Abs(aimX) * .1f;
                aimX += random.NextInt32(-40, 41); aimY += random.NextInt32(-40, 41);
                float distance = (float)Math.Sqrt(aimX * aimX + aimY * aimY);
                float range = type is >= 449 and <= 452 ? 200f : type == 481 ? 400f : 700f;
                if (distance < range)
                {
                    if (!Normalize(ref aimX, ref aimY, 10f)) return false;
                    sync = true; x *= .5f; timer = windup; mode = Aim(aimX, aimY);
                }
            }
        }
        if (mode <= 0f || moving)
        {
            float maximum = type == 520 ? 4f : moving ? 2f : 1f;
            float acceleration = type == 520 ? 1f : moving ? .5f : .07f;
            float damping = type == 520 ? .7f : .8f;
            float dx = current.PositionX + current.Width * .5f - (common.PositionX + body.Width * .5f);
            float dy = current.PositionY + current.Height * .5f - (common.PositionY + body.Height * .5f);
            bool close = moving && (float)Math.Sqrt(dx * dx + dy * dy) < (type == 520 ? 400f : 300f) &&
                line!.CanHitLine(common.PositionX + body.Width * .5f, common.PositionY + body.Height * .5f, 0, 0,
                    current.PositionX + current.Width * .5f, current.PositionY + current.Height * .5f, 0, 0);
            if (close) stuck = 0f;
            if (x < -maximum || x > maximum || close)
            { if (y == 0f) { x *= damping; y *= damping; } }
            else if (x < maximum && direction == 1) x = Math.Min(x + acceleration, maximum);
            else if (x > -maximum && direction == -1) x = Math.Max(x - acceleration, -maximum);
        }
        if (type == 520)
        {
            float turn = local.Ai2 + 1f;
            if (turn >= 6f)
            {
                turn = 0f;
                float dx = current.PositionX + current.Width * .5f - (common.PositionX + body.Width * .5f);
                float dy = current.PositionY + current.Height * .5f - (common.PositionY + 20f);
                if (!Normalize(ref dx, ref dy, 1f)) return false;
                local = local with { Ai3 = (float)Math.Atan2(dy, dx) };
            }
            local = local with { Ai2 = turn };
        }
        next = common with { Target = target, VelocityX = x, VelocityY = y,
            Ai = common.Ai with { Ai1 = timer, Ai2 = mode, Ai3 = stuck },
            Simulation = common.Simulation with { DirectionX = direction, DirectionY = vertical, SpriteDirection = sprite, LocalAi = local },
            RequiresImmediateSync = sync };
        return RuntimeNpcStore.IsValid(in next);
    }

    private static bool TryPlanGigaZapper(in NpcSnapshot before, in NpcStateUpdate common,
        in VanillaNpcHitboxSize body, IVanillaNpcRandom random, Span<NpcAiProjectileIntent> shots,
        out int count, out NpcStateUpdate next)
    {
        count = 0;
        float timer = common.Ai.Ai2;
        if (common.Simulation.Confused) timer = -60f;
        else
        {
            if (timer < 20f) timer++;
            if (before.Simulation.JustHit) timer = -30f;
            if (timer == 20f)
            {
                // AI003 does not request NPC synchronization at this firing edge.
                // The caller adopts the projectile and the single reset roll together.
                timer = -10f - random.NextInt32(0, 3) * 10f;
                if (!VanillaDefinitionCatalog.TryGet(new(437), out var projectile))
                { next = default; return false; }
                shots[0] = new(new(437), common.PositionX + body.Width * .5f - projectile.Width * .5f,
                    common.PositionY + body.Height * .5f + 8f - projectile.Height * .5f,
                    common.Simulation.DirectionX * 6f, 0f, 25, 1f);
                count = 1;
            }
        }
        next = common with { Ai = common.Ai with { Ai2 = timer } };
        return RuntimeNpcStore.IsValid(in next);
    }

    private static bool ReadTarget(ushort slot, VanillaNpcBehaviorContext context, out VanillaNpcRawPlayer1458 player)
    {
        player = default;
        if (slot > byte.MaxValue || !context.TryCaptureRawPlayer((byte)slot, out var captured) ||
            !context.IsRawPlayerCurrent(in captured) || !captured.Facts.IsValid) return false;
        player = captured.Facts; return true;
    }

    private static bool Normalize(ref float x, ref float y, float speed)
    {
        float length = (float)Math.Sqrt(x * x + y * y);
        if (!(length > 0f) || !float.IsFinite(length)) return false;
        float scale = speed / length; x *= scale; y *= scale;
        return float.IsFinite(x) && float.IsFinite(y);
    }

    private static float Aim(float x, float y) => Math.Abs(y) > Math.Abs(x) * 2f
        ? y > 0f ? 1f : 5f : Math.Abs(x) > Math.Abs(y) * 2f ? 3f : y > 0f ? 2f : 4f;
}
