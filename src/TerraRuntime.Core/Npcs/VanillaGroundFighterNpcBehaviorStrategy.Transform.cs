using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

internal sealed partial class VanillaGroundFighterNpcBehaviorStrategy
{
    private static bool TryPrepareMerman(in NpcSnapshot before, in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context, IVanillaNpcRandom random, IVanillaNpcProjectileEnvironment environment,
        out NpcSnapshot staged, out bool swimming, out bool syncRequested)
    {
        staged = before;
        swimming = false;
        bool targetSync = false;
        syncRequested = false;
        var targetDefinition = definition;
        ushort oldTarget = before.Target;
        int oldDirectionX = before.Simulation.DirectionX, oldDirectionY = before.Simulation.DirectionY;
        if (!definition.TryResolveHitbox(before.Simulation, out var body)) return false;
        if (before.Simulation.Alpha == 255)
        {
            if (!Refresh(ref staged)) return false;
            staged = staged with { VelocityY = -6f,
                Simulation = staged.Simulation with { SpriteDirection = staged.Simulation.DirectionX } };
            Offers(35);
        }
        int alpha = Math.Max(0, staged.Simulation.Alpha - 15);
        if (alpha != 0) Offers(2);
        if (random.NextInt32(0, 3) == 0) _ = (float)random.NextDouble();
        var simulation = staged.Simulation with { Alpha = alpha, NoGravity = before.Simulation.Wet };
        // NPC.Center uses fractional halves; the source assignments subtract
        // integer halves, including the half-pixel retained by an odd body.
        staged = staged with { PositionX = staged.PositionX + body.Width * .5f - body.Width / 2,
            PositionY = staged.PositionY + body.Height * .5f - body.Height / 2, Simulation = simulation };
        if (before.Simulation.Wet)
        {
            if (!Refresh(ref staged) || staged.Target > byte.MaxValue ||
                !context.TryCaptureRawPlayer((byte)staged.Target, out var target) ||
                !context.IsRawPlayerCurrent(in target)) return false;
            float vx = before.Simulation.CollideX ? -before.Simulation.OldVelocityX : staged.VelocityX;
            int direction = vx < 0f ? -1 : vx > 0f ? 1 : staged.Simulation.DirectionX;
            float targetX = target.Facts.PositionX + target.Facts.Width * .5f;
            float targetY = target.Facts.PositionY + target.Facts.Height * .5f;
            float dx = targetX - (staged.PositionX + body.Width * .5f);
            float dy = targetY - (staged.PositionY + body.Height * .5f);
            bool visible = environment.CanHit(staged.PositionX, staged.PositionY, body.Width, body.Height,
                targetX, targetY, 1, 1);
            var velocity = VanillaCreatureFromDeepMotion1458.SteerMerman(vx, staged.VelocityY, dx, dy, direction, visible);
            staged = staged with { VelocityX = velocity.X, VelocityY = velocity.Y,
                Ai = staged.Ai with { Ai3 = VanillaCreatureFromDeepMotion1458.SwimmingClock },
                Simulation = staged.Simulation with { DirectionX = direction, KnockBackResist = 0f } };
            swimming = true;
        }
        else if (staged.Ai.Ai3 == VanillaCreatureFromDeepMotion1458.SwimmingClock)
        {
            var velocity = VanillaCreatureFromDeepMotion1458.ExitVelocity(staged.VelocityX, staged.VelocityY, 15f);
            int direction = velocity.X < 0f ? -1 : velocity.X > 0f ? 1 : staged.Simulation.DirectionX;
            staged = staged with { VelocityX = velocity.X, VelocityY = velocity.Y,
                Ai = staged.Ai with { Ai3 = 0f }, Simulation = staged.Simulation with
                    { DirectionX = direction, SpriteDirection = direction } };
        }
        syncRequested = targetSync;
        return float.IsFinite(staged.VelocityX) && float.IsFinite(staged.VelocityY);

        bool Refresh(ref NpcSnapshot actor)
        {
            if (!context.TrySelectRawClosestTarget(in actor, in targetDefinition, out var target) ||
                !context.TryCaptureRawPlayer((byte)target.Target, out var raw) ||
                raw.Facts.Slot != target.Target || !raw.Facts.IsValid || !context.IsRawPlayerCurrent(in raw)) return false;
            var player = raw.Facts;
            // SetTargetTrackingValues uses the truncated player rectangle and
            // the live fractional NPC body. oldTarget is the outer phase's
            // capture, including across alpha255's two TargetClosest calls.
            bool retain = player.Dead || player.NoAggro && actor.Simulation.DirectionX != 0 ||
                player.ItemAnimation == 0 && player.Aggro < 0 && oldTarget < byte.MaxValue;
            target = target with
            {
                DirectionX = retain ? actor.Simulation.DirectionX :
                    (int)player.PositionX + player.Width / 2 < actor.PositionX + body.Width / 2 ? -1 : 1,
                DirectionY = retain ? actor.Simulation.DirectionY :
                    (int)player.PositionY + player.Height <= actor.PositionY + body.Height ? -1 : 1
            };
            targetSync |= !actor.Simulation.CollideX && !actor.Simulation.CollideY &&
                (target.Target != oldTarget || target.DirectionX != oldDirectionX ||
                 target.DirectionY != oldDirectionY);
            actor = actor with { Target = target.Target, Simulation = actor.Simulation with
                { DirectionX = target.DirectionX, DirectionY = target.DirectionY } };
            return true;
        }
        void Offers(int count)
        {
            // Dust.NewDustDirect is a dedicated no-op; its caller's scale and
            // fade arguments still consume the original shared physical stream.
            for (int i = 0; i < count; i++) { _ = (float)random.NextDouble(); _ = (float)random.NextDouble(); }
        }
    }
}
