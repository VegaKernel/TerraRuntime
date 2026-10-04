using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

/// <summary>Accepted AI_022 for the three explicitly admitted hover identities.</summary>
internal sealed class VanillaGhostHoverNpcBehaviorStrategy1458 : IVanillaNpcBehaviorStrategy
{
    private IVanillaGhostHoverEnvironment1458? environment;

    public void SetEnvironment(IVanillaGhostHoverEnvironment1458 value) =>
        environment = value ?? throw new ArgumentNullException(nameof(value));

    public bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context, INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        _ = inner;
        next = default;
        if (environment is null || npc.Simulation.LiquidContact == NpcLiquidContactKind.Shimmer ||
            (npc.TypeIdentity == VanillaNpcIds.Wraith && context.RemixWorld) ||
            !VanillaGhostHoverNpcCatalog1458.IsSupported(npc.TypeIdentity) ||
            !definition.TryResolveHitbox(npc.Simulation, out var body) ||
            !TryResolveTarget(in npc, in definition, context, out _, out _, out _))
            return false;

        int centerTileX = (int)((npc.PositionX + body.Width * .5f) / 16f);
        int bottomTileY = (int)((npc.PositionY + body.Height) / 16f);
        int depth = npc.TypeIdentity == VanillaNpcIds.Gastropod ? 8 : 3;
        for (int y = bottomTileY; y < bottomTileY + depth; y++)
            if (!environment.TryHasObstacle(centerTileX - 2, y, out _) ||
                !environment.TryHasObstacle(centerTileX + 2, y, out _))
                return false;

        // An unchanged speculative proposal admits the exact revision. AI/RNG completes only after acceptance.
        next = State(in npc);
        return true;
    }

    public bool TryComplete(in NpcSnapshot before, in NpcSnapshot accepted, VanillaNpcBehaviorContext context,
        IVanillaNpcRandom random, Span<NpcAiProjectileIntent> shots, out int count, out NpcStateUpdate next)
    {
        count = 0;
        next = default;
        if (before.TypeIdentity != accepted.TypeIdentity || environment is null || shots.IsEmpty ||
            before.Simulation.LiquidContact == NpcLiquidContactKind.Shimmer ||
            (before.TypeIdentity == VanillaNpcIds.Wraith && context.RemixWorld) ||
            !VanillaGhostHoverNpcCatalog1458.IsSupported(before.TypeIdentity) ||
            !VanillaNpcDefinitionCatalog.TryGet(before.TypeIdentity, before.NetIdentity, out var definition) ||
            !definition.TryResolveHitbox(before.Simulation, out var body) ||
            !TryResolveTarget(in before, in definition, context, out var target, out int directionX, out int directionY))
            return false;

        if (before.TypeIdentity == VanillaNpcIds.Wraith || before.TypeIdentity == VanillaNpcIds.Reaper)
        {
            // NPC.UpdateNPC calls IdleSounds immediately before AI, including on dedicated servers.
            if (random.NextInt32(0, 700) == 0)
                random.NextInt32(81, 84);
        }
        bool retreat = before.TypeIdentity == VanillaNpcIds.Reaper && !context.EclipseActive;
        bool collideX = before.Simulation.CollideX;
        float vx = before.VelocityX;
        float vy = before.VelocityY;
        var ai = before.Ai;
        var local = before.Simulation.LocalAi;
        float stuck = before.Simulation.JustHit ? 0f : ai.Ai2;
        if (retreat)
        {
            if (vx == 0f)
                vx = random.NextInt32(-1, 2) * 1.5f;
        }
        else if (stuck >= 0f)
        {
            bool stalledX = (before.PositionX > ai.Ai0 - 16f && before.PositionX < ai.Ai0 + 16f) ||
                (vx < 0f && before.Simulation.DirectionX > 0) || (vx > 0f && before.Simulation.DirectionX < 0);
            bool stalledY = before.PositionY > ai.Ai1 - 40f && before.PositionY < ai.Ai1 + 40f;
            if (stalledX && stalledY)
            {
                stuck++;
                if (stuck >= 60f)
                {
                    stuck = -200f;
                    // Source reverses before its subsequent TargetClosest replaces the facing.
                    vx *= -1f;
                    if (target.NoAggro && before.Simulation.DirectionX != 0)
                        directionX = before.Simulation.Confused ? before.Simulation.DirectionX : -before.Simulation.DirectionX;
                    collideX = false;
                }
            }
            else
            {
                ai = ai with { Ai0 = before.PositionX, Ai1 = before.PositionY };
                stuck = 0f;
            }
        }
        else if (before.TypeIdentity == VanillaNpcIds.Reaper)
            stuck += 2f;
        else
        {
            stuck++;
            directionX = target.CenterX > before.PositionX + body.Width * .5f ? -1 : 1;
        }

        float centerX = before.PositionX + body.Width * .5f;
        float centerY = before.PositionY + body.Height * .5f;
        if (before.TypeIdentity == VanillaNpcIds.Gastropod)
        {
            float attack = ai.Ai3;
            float clock = local.Ai1;
            if (before.Simulation.JustHit)
            {
                attack = 0f;
                clock = 0f;
            }
            if (attack == 32f && !target.NoAggro)
            {
                float dx = target.CenterX - centerX;
                float dy = target.CenterY - centerY;
                float factor = 7f / (float)Math.Sqrt(dx * dx + dy * dy);
                dx *= factor;
                dy *= factor;
                double angleRange = .0125f * ((float)Math.PI * 2f);
                double angle = random.NextDouble() * angleRange - random.NextDouble() * angleRange;
                float cosine = (float)Math.Cos(angle);
                float sine = (float)Math.Sin(angle);
                float shotX = dx * cosine - dy * sine;
                float shotY = dx * sine + dy * cosine;
                if (float.IsFinite(shotX) && float.IsFinite(shotY))
                    // NewProjectile anchors the source 4x4 laser around its center argument.
                    shots[count++] = new NpcAiProjectileIntent(VanillaProjectileIds.ProbePinkLaser,
                        centerX - 2f, centerY - 2f, shotX, shotY, 25, 0f);
            }
            if (attack > 0f && ++attack >= 64f)
                attack = 0f;
            if (attack == 0f)
            {
                clock++;
                if (clock > 120f)
                {
                    clock = 0f;
                    if (VanillaNpcGlobalFiringDistance.Contains(centerX, centerY, target.CenterX, target.CenterY) &&
                        environment.CanHit(before.PositionX, before.PositionY, body.Width, body.Height,
                            target.CenterX - target.Width * .5f, target.CenterY - target.Height * .5f,
                            (int)target.Width, (int)target.Height) && !target.NoAggro)
                        attack = 1f;
                }
            }
            ai = ai with { Ai3 = attack };
            local = local with { Ai1 = clock };
        }

        int scanX = (int)(centerX / 16f) + directionX * 2;
        int scanY = (int)((before.PositionY + body.Height) / 16f);
        int depth = before.TypeIdentity == VanillaNpcIds.Gastropod ? 8 : 3;
        bool descend = true;
        if (before.PositionY + body.Height > target.CenterY - target.Height * .5f)
        {
            for (int y = scanY; y < scanY + depth; y++)
            {
                if (!environment.TryHasObstacle(scanX, y, out bool obstacle))
                    return false;
                if (obstacle)
                {
                    descend = false;
                    break;
                }
            }
        }
        if (target.NoAggro)
        {
            bool obstacle = false;
            for (int y = scanY; y < scanY + depth - 2; y++)
            {
                if (!environment.TryHasObstacle(scanX, y, out bool current))
                    return false;
                if (current)
                {
                    obstacle = true;
                    break;
                }
            }
            directionY = obstacle ? -1 : 1;
        }
        if (descend)
            vy = Math.Min(vy + .1f, 3f);
        else
        {
            if (directionY < 0 && vy > 0f)
                vy -= .1f;
            vy = Math.Max(vy, -4f);
        }
        if (collideX)
        {
            vx = before.Simulation.OldVelocityX * -.4f;
            if (directionX == -1 && vx > 0f && vx < 1f)
                vx = 1f;
            if (directionX == 1 && vx < 0f && vx > -1f)
                vx = -1f;
        }
        if (before.Simulation.CollideY)
        {
            vy = before.Simulation.OldVelocityY * -.25f;
            if (vy > 0f && vy < 1f)
                vy = 1f;
            if (vy < 0f && vy > -1f)
                vy = -1f;
        }
        float maximum = before.TypeIdentity == VanillaNpcIds.Reaper ? 4f : 2f;
        if (directionX == -1 && vx > -maximum)
        {
            vx -= .1f;
            if (vx > maximum)
                vx -= .1f;
            else if (vx > 0f)
                vx += .05f;
            vx = Math.Max(vx, -maximum);
        }
        else if (directionX == 1 && vx < maximum)
        {
            vx += .1f;
            if (vx < -maximum)
                vx += .1f;
            else if (vx < 0f)
                vx -= .05f;
            vx = Math.Min(vx, maximum);
        }
        if (directionY == -1 && vy > -1.5f)
        {
            vy -= .04f;
            if (vy > 1.5f)
                vy -= .05f;
            else if (vy > 0f)
                vy += .03f;
            vy = Math.Max(vy, -1.5f);
        }
        else if (directionY == 1 && vy < 1.5f)
        {
            vy += .04f;
            if (vy < -1.5f)
                vy += .05f;
            else if (vy < 0f)
                vy -= .03f;
            vy = Math.Min(vy, 1.5f);
        }
        next = State(in before) with
        {
            VelocityX = vx,
            VelocityY = vy,
            Target = target.Slot,
            Ai = ai with { Ai2 = stuck },
            Simulation = before.Simulation with
            {
                LocalAi = local,
                DirectionX = directionX,
                DirectionY = directionY,
                CollideX = collideX
            }
        };
        return true;
    }

    private static bool TryResolveTarget(in NpcSnapshot npc, in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context, out VanillaNpcTargetCandidate target, out int directionX, out int directionY)
    {
        directionX = npc.Simulation.DirectionX;
        directionY = npc.Simulation.DirectionY;
        ushort slot = npc.Target;
        bool retreat = npc.TypeIdentity == VanillaNpcIds.Reaper && !context.EclipseActive;
        bool refresh = !retreat && (npc.Simulation.JustHit || npc.Ai.Ai2 >= 0f || npc.TypeIdentity == VanillaNpcIds.Reaper);
        if (refresh && context.TrySelectClosestTarget(in npc, in definition, out var closest))
        {
            slot = closest.Target;
            if (!context.TryFindCandidate((byte)slot, out var selected) ||
                !definition.TryResolveHitbox(npc.Simulation, out var body))
            {
                target = default;
                return false;
            }
            // TargetClosest scores float centers, then faces the integer player rectangle and integer NPC halves.
            int playerCenterX = (int)(selected.CenterX - selected.Width * .5f) + (int)selected.Width / 2;
            int playerCenterY = (int)(selected.CenterY - selected.Height * .5f) + (int)selected.Height / 2;
            directionX = playerCenterX < npc.PositionX + body.Width / 2 ? -1 : 1;
            directionY = playerCenterY < npc.PositionY + body.Height / 2 ? -1 : 1;
            if (selected.NoAggro && npc.Simulation.DirectionX != 0)
            {
                directionX = npc.Simulation.DirectionX;
                directionY = npc.Simulation.DirectionY;
            }
        }
        if (refresh && npc.Simulation.Confused)
            directionX *= -1;
        target = default;
        return slot < byte.MaxValue && context.TryFindCandidate((byte)slot, out target) &&
            target.Active && !target.Dead && !target.Ghost && !(target.Aggro < 0 && target.ItemAnimation == 0);
    }

    public static bool RequiresImmediateSync(in NpcSnapshot before, in NpcSnapshot finalized,
        VanillaNpcBehaviorContext context)
    {
        if (!VanillaGhostHoverNpcCatalog1458.IsSupported(before.TypeIdentity) ||
            before.TypeIdentity != finalized.TypeIdentity)
            return false;
        if (before.TypeIdentity == VanillaNpcIds.Reaper && !context.EclipseActive)
            return before.VelocityX == 0f;
        float stuck = before.Simulation.JustHit ? 0f : before.Ai.Ai2;
        if (stuck >= 0f)
        {
            bool stalledX = (before.PositionX > before.Ai.Ai0 - 16f && before.PositionX < before.Ai.Ai0 + 16f) ||
                (before.VelocityX < 0f && before.Simulation.DirectionX > 0) ||
                (before.VelocityX > 0f && before.Simulation.DirectionX < 0);
            bool stalledY = before.PositionY > before.Ai.Ai1 - 40f && before.PositionY < before.Ai.Ai1 + 40f;
            if (!(stalledX && stalledY) || stuck + 1f >= 60f)
                return true;
        }
        if ((before.Simulation.JustHit || before.Ai.Ai2 >= 0f || before.TypeIdentity == VanillaNpcIds.Reaper) &&
            !before.Simulation.CollideX && !before.Simulation.CollideY &&
            VanillaNpcDefinitionCatalog.TryGet(before.TypeIdentity, before.NetIdentity, out var definition) &&
            TryResolveTarget(in before, in definition, context, out var target, out int directionX, out int directionY) &&
            (directionX != before.Simulation.DirectionX || directionY != before.Simulation.DirectionY || target.Slot != before.Target))
            return true;
        return before.TypeIdentity == VanillaNpcIds.Gastropod && before.Ai.Ai3 != 1f && finalized.Ai.Ai3 == 1f;
    }
    private static NpcStateUpdate State(in NpcSnapshot npc) => new(npc.Type, npc.NetId,
        npc.PositionX, npc.PositionY, npc.VelocityX, npc.VelocityY, npc.Target, npc.Ai, npc.Simulation);
}
