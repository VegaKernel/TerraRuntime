using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

internal sealed partial class VanillaGroundFighterNpcBehaviorStrategy
{
    private static NpcSnapshot PrepareFritz(in NpcSnapshot npc, in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context)
    {
        if (npc.VelocityY != 0f || MathF.Abs(npc.VelocityX) <= 3f || npc.Target >= byte.MaxValue ||
            !context.TryFindCandidate((byte)npc.Target, out var target))
            return npc;

        if (!definition.TryResolveHitbox(npc.Simulation, out var body))
            return npc;
        float centerX = npc.PositionX + body.Width * .5f;
        float centerY = npc.PositionY + body.Height * .5f;
        float dx = target.CenterX - centerX;
        float dy = target.CenterY - centerY;
        if ((float)Math.Sqrt(dx * dx + dy * dy) >= 150f ||
            !((npc.VelocityX < 0f && centerX > target.CenterX) ||
              (npc.VelocityX > 0f && centerX < target.CenterX)))
            return npc;

        float velocityX = npc.VelocityX * 1.75f;
        float velocityY = npc.VelocityY - 4.5f;
        if (centerY - target.CenterY > 20f)
            velocityY -= .5f;
        if (centerY - target.CenterY > 40f)
            velocityY--;
        if (centerY - target.CenterY > 80f)
            velocityY -= 1.5f;
        if (centerY - target.CenterY > 100f)
            velocityY -= 1.5f;
        velocityX = Math.Clamp(velocityX, -7f, 7f);
        return npc with { VelocityX = velocityX, VelocityY = velocityY };
    }

    private static bool TryPreparePossessed(in NpcSnapshot npc, in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context, out NpcSnapshot prepared, out bool flying)
    {
        prepared = npc;
        flying = false;
        if (context.ProjectileEnvironment is not IVanillaNpcBackgroundWallEnvironment walls ||
            !definition.TryResolveHitbox(npc.Simulation, out var body))
            return false;

        float centerX = npc.PositionX + body.Width * .5f;
        float centerY = npc.PositionY + body.Height * .5f;
        int tileX = (int)centerX / 16;
        int tileY = (int)centerY / 16;
        bool nearWall = false;
        for (int x = tileX - 1; x <= tileX + 1 && !nearWall; x++)
            for (int y = tileY - 1; y <= tileY + 1; y++)
                if (walls.HasBackgroundWall(x, y))
                {
                    nearWall = true;
                    break;
                }

        float mode = npc.Ai.Ai2;
        float knockback = .45f * (1f +
            (Math.Clamp(npc.Simulation.SpawnDifficulty ?? 1f, 1f, 3f) - 1f) * (.8f - 1f) / 2f);
        if (mode == 1f)
            knockback = 0f;
        float velocityX = npc.VelocityX;
        float velocityY = npc.VelocityY;
        VanillaNpcTargetCandidate target = default;
        bool hasTarget = npc.Target < byte.MaxValue &&
            context.TryFindCandidate((byte)npc.Target, out target);
        if (mode == 0f && nearWall)
        {
            if (velocityY == 0f)
            {
                velocityY = -4.6f;
                velocityX *= 1.3f;
            }
            else if (velocityY > 0f && hasTarget && !target.Dead)
                mode = 1f;
        }

        if (nearWall && mode == 1f && hasTarget && !target.Dead &&
            context.ProjectileEnvironment.CanHit(centerX, centerY, 1, 1, target.CenterX, target.CenterY, 1, 1))
        {
            float dx = target.CenterX - centerX;
            float dy = target.CenterY - centerY;
            float distance = (float)Math.Sqrt(dx * dx + dy * dy);
            float reciprocal = 1f / distance;
            dx *= reciprocal;
            dy *= reciprocal;
            float speed = 4.5f + distance / 300f;
            dx *= speed;
            dy *= speed;
            velocityX = (velocityX * 29f + dx) / 30f;
            velocityY = (velocityY * 29f + dy) / 30f;
            if (!float.IsFinite(velocityX) || !float.IsFinite(velocityY))
                return false;
            flying = true;
            mode = 1f;
        }
        else
            mode = 0f;

        prepared = npc with
        {
            VelocityX = velocityX,
            VelocityY = velocityY,
            Ai = npc.Ai with { Ai2 = mode },
            Simulation = npc.Simulation with { NoGravity = flying, KnockBackResist = knockback }
        };
        return true;
    }
}
