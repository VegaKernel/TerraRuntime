using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 Expert Eye of Cthulhu rapid-dash extension. The ordinary Eye strategy
/// remains authoritative for classic, phase-one, transformation and deterministic phase-two motion; this decorator
/// owns only the RNG-shaped phase-two boundaries that require live player velocity and Main.rand-equivalent order.
/// Good World remains deliberately fail-closed in the wrapped strategy.
/// </summary>
internal sealed class VanillaEyeOfCthulhuExpertRapidDashNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    private const float LowLifeFraction = 0.12f;
    private const float CriticalLifeFraction = 0.04f;
    private const float RapidEntryLifeFraction = 0.5f;
    private const float DirectDashSlowdown = 0.97f;
    private const float ExpertDirectDashExtraSlowdown = 0.98f;
    private const float RapidDashSlowdown = 0.95f;
    private const float RapidDashSpeed = 20f;
    private const float StateFiveSpeed = 9f;
    private const float StateFiveAcceleration = 0.3f;
    private const float StateFiveVerticalOffset = 600f;
    private const float VelocityStopEpsilon = 0.1f;
    private const float PlayerWidth = 20f;
    private const float PlayerHeight = 42f;

    private readonly IVanillaNpcBehaviorStrategy _inner = new VanillaEyeOfCthulhuNpcBehaviorStrategy();
    private readonly IVanillaNpcRandom _random;

    public VanillaEyeOfCthulhuExpertRapidDashNpcBehaviorStrategy(IVanillaNpcRandom random) =>
        _random = random ?? throw new ArgumentNullException(nameof(random));

    public bool TryStep(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner,
        out NpcStateUpdate next)
    {
        if (definition.AiStyle != VanillaNpcAiStyles.EyeOfCthulhu ||
            !definition.IsBoss ||
            !context.ExpertMode ||
            context.GoodWorld ||
            context.DayTime)
        {
            return _inner.TryStep(in npc, in definition, context, inner, out next);
        }

        int lifeMax = npc.Simulation.LifeMax > 0 ? npc.Simulation.LifeMax : definition.LifeMax;
        int life = npc.Simulation.LifeMax > 0 ? npc.Simulation.Life : definition.LifeMax;
        if (lifeMax <= 0 || life <= 0)
            return _inner.TryStep(in npc, in definition, context, inner, out next);

        if (!TryResolveCurrentTarget(in npc, in definition, context, out NpcSnapshot targeted, out VanillaNpcTargetCandidate current))
            return _inner.TryStep(in npc, in definition, context, inner, out next);

        float ai0 = targeted.Ai.Ai0;
        float ai1 = targeted.Ai.Ai1;
        if (ai0 != 3f)
            return _inner.TryStep(in targeted, in definition, context, inner, out next);

        if (ai1 == 2f &&
            targeted.Ai.Ai2 >= 89f &&
            targeted.Ai.Ai3 >= 2f &&
            (float)life < lifeMax * RapidEntryLifeFraction)
        {
            next = CompleteThirdDirectDashAndSeedRapid(in targeted, in definition);
            return true;
        }

        if (ai1 == 3f)
            return TryLaunchRapidDash(in targeted, in definition, in current, life, lifeMax, context, out next);

        if (ai1 == 4f)
        {
            next = StepRapidDash(in targeted, in definition, in current, life, lifeMax);
            return true;
        }

        if (ai1 == 5f && targeted.Ai.Ai2 >= 69f)
            return TryCompleteStateFive(in targeted, in definition, life, lifeMax, context, out next);

        return _inner.TryStep(in targeted, in definition, context, inner, out next);
    }

    private NpcStateUpdate CompleteThirdDirectDashAndSeedRapid(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition)
    {
        float velocityX = npc.VelocityX;
        float velocityY = npc.VelocityY;
        float ai2 = npc.Ai.Ai2 + 1f;
        if (ai2 >= 50f)
        {
            velocityX *= DirectDashSlowdown * ExpertDirectDashExtraSlowdown;
            velocityY *= DirectDashSlowdown * ExpertDirectDashExtraSlowdown;
            StopSmall(ref velocityX, ref velocityY);
        }

        ushort target = npc.Target;
        float ai1 = npc.Ai.Ai1;
        float ai3 = npc.Ai.Ai3;
        if (ai2 >= 90f)
        {
            target = VanillaNpcDefinitionCatalog.DefaultTarget;
            ai1 = 3f;
            ai2 = 0f;
            ai3 = _random.NextInt32(1, 4);
        }

        return Build(in npc, in definition, velocityX, velocityY, target, ai1, ai2, ai3);
    }

    private bool TryLaunchRapidDash(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        in VanillaNpcTargetCandidate current,
        int life,
        int lifeMax,
        VanillaNpcBehaviorContext context,
        out NpcStateUpdate next)
    {
        bool lowLife = (float)life < lifeMax * LowLifeFraction;
        bool criticalLife = (float)life < lifeMax * CriticalLifeFraction;
        float centerX = npc.PositionX + definition.Width * 0.5f;
        float centerY = npc.PositionY + definition.Height * 0.5f;

        if (npc.Ai.Ai3 == 4f && lowLife && centerY > current.CenterY)
        {
            ushort resetTarget = ResolveClosestTarget(in npc, in definition, context, out _) ?? npc.Target;
            next = Build(
                in npc,
                in definition,
                npc.VelocityX,
                npc.VelocityY,
                resetTarget,
                ai1: 0f,
                ai2: 0f,
                ai3: 0f);
            return true;
        }

        ushort? selectedSlot = ResolveClosestTarget(in npc, in definition, context, out VanillaNpcTargetCandidate selected);
        if (selectedSlot is null ||
            !float.IsFinite(selected.CenterX) ||
            !float.IsFinite(selected.CenterY) ||
            !float.IsFinite(selected.VelocityX) ||
            !float.IsFinite(selected.VelocityY))
        {
            next = default;
            return false;
        }

        float speed = RapidDashSpeed;
        float prediction = MathF.Abs(selected.VelocityX) + MathF.Abs(selected.VelocityY) / 4f;
        prediction += 10f - prediction;
        prediction = Math.Clamp(prediction, 5f, 15f);
        if (npc.Ai.Ai2 == -1f && !criticalLife)
        {
            prediction *= 4f;
            speed *= 1.3f;
        }
        if (criticalLife)
            prediction *= 2f;

        float deltaX = selected.CenterX - centerX - selected.VelocityX * prediction;
        float deltaY = selected.CenterY - centerY - selected.VelocityY * prediction / 4f;
        deltaX *= 1f + _random.NextInt32(-10, 11) * 0.01f;
        deltaY *= 1f + _random.NextInt32(-10, 11) * 0.01f;
        if (criticalLife)
        {
            deltaX *= 1f + _random.NextInt32(-10, 11) * 0.01f;
            deltaY *= 1f + _random.NextInt32(-10, 11) * 0.01f;
        }

        float perturbedDistance = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (!float.IsFinite(perturbedDistance) || perturbedDistance <= float.Epsilon)
        {
            next = default;
            return false;
        }

        float scale = speed / perturbedDistance;
        float velocityX = deltaX * scale + _random.NextInt32(-20, 21) * 0.1f;
        float velocityY = deltaY * scale + _random.NextInt32(-20, 21) * 0.1f;

        if (criticalLife)
        {
            velocityX += _random.NextInt32(-50, 51) * 0.1f;
            velocityY += _random.NextInt32(-50, 51) * 0.1f;
            float absoluteX = MathF.Abs(velocityX);
            float absoluteY = MathF.Abs(velocityY);
            if (centerX > selected.CenterX)
                absoluteY *= -1f;
            if (centerY > selected.CenterY)
                absoluteX *= -1f;
            velocityX += absoluteY;
            velocityY += absoluteX;
            if (!Normalize(ref velocityX, ref velocityY, speed))
            {
                next = default;
                return false;
            }
            velocityX += _random.NextInt32(-20, 21) * 0.1f;
            velocityY += _random.NextInt32(-20, 21) * 0.1f;
        }
        else if (perturbedDistance < 100f)
        {
            if (MathF.Abs(velocityX) > MathF.Abs(velocityY))
            {
                float absoluteX = MathF.Abs(velocityX);
                float absoluteY = MathF.Abs(velocityY);
                if (centerX > selected.CenterX)
                    absoluteY *= -1f;
                if (centerY > selected.CenterY)
                    absoluteX *= -1f;
                velocityX = absoluteY;
                velocityY = absoluteX;
            }
        }
        else if (MathF.Abs(velocityX) > MathF.Abs(velocityY))
        {
            float average = (MathF.Abs(velocityX) + MathF.Abs(velocityY)) * 0.5f;
            float signedX = centerX > selected.CenterX ? -average : average;
            float signedY = centerY > selected.CenterY ? -average : average;
            velocityX = signedX;
            velocityY = signedY;
        }

        next = Build(
            in npc,
            in definition,
            velocityX,
            velocityY,
            selectedSlot.Value,
            ai1: 4f,
            ai2: npc.Ai.Ai2,
            ai3: npc.Ai.Ai3);
        return true;
    }

    private NpcStateUpdate StepRapidDash(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        in VanillaNpcTargetCandidate target,
        int life,
        int lifeMax)
    {
        bool criticalLife = (float)life < lifeMax * CriticalLifeFraction;
        float duration = criticalLife ? 10f : 20f;
        float ai2 = npc.Ai.Ai2 + 1f;
        float targetLeft = target.CenterX - PlayerWidth * 0.5f;
        float targetTop = target.CenterY - PlayerHeight * 0.5f;
        float dx = npc.PositionX - targetLeft;
        float dy = npc.PositionY - targetTop;
        float topLeftDistance = MathF.Sqrt(dx * dx + dy * dy);
        if (ai2 == duration && topLeftDistance < 200f)
            ai2--;

        float velocityX = npc.VelocityX;
        float velocityY = npc.VelocityY;
        if (ai2 >= duration)
        {
            velocityX *= RapidDashSlowdown;
            velocityY *= RapidDashSlowdown;
            StopSmall(ref velocityX, ref velocityY);
        }

        float ai1 = 4f;
        float ai3 = npc.Ai.Ai3;
        if (ai2 >= duration + 13f)
        {
            ai3++;
            ai2 = 0f;
            if (ai3 >= 5f)
            {
                ai1 = 0f;
                ai3 = 0f;
            }
            else
            {
                ai1 = 3f;
            }
        }

        return Build(in npc, in definition, velocityX, velocityY, npc.Target, ai1, ai2, ai3);
    }

    private bool TryCompleteStateFive(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        int life,
        int lifeMax,
        VanillaNpcBehaviorContext context,
        out NpcStateUpdate next)
    {
        ushort? selectedSlot = ResolveClosestTarget(in npc, in definition, context, out VanillaNpcTargetCandidate selected);
        if (selectedSlot is null)
        {
            next = default;
            return false;
        }

        float centerX = npc.PositionX + definition.Width * 0.5f;
        float centerY = npc.PositionY + definition.Height * 0.5f;
        float deltaX = selected.CenterX - centerX;
        float deltaY = selected.CenterY + StateFiveVerticalOffset - centerY;
        float distance = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (!float.IsFinite(distance) || distance <= float.Epsilon)
        {
            next = default;
            return false;
        }

        float scale = StateFiveSpeed / distance;
        float desiredX = deltaX * scale;
        float desiredY = deltaY * scale;
        float velocityX = npc.VelocityX;
        float velocityY = npc.VelocityY;
        ApproachAxis(ref velocityX, desiredX, StateFiveAcceleration);
        ApproachAxis(ref velocityY, desiredY, StateFiveAcceleration);

        float ai2 = npc.Ai.Ai2 + 1f;
        if (ai2 < 70f)
        {
            next = Build(in npc, in definition, velocityX, velocityY, npc.Target, 5f, ai2, npc.Ai.Ai3);
            return true;
        }

        next = Build(
            in npc,
            in definition,
            velocityX,
            velocityY,
            selectedSlot.Value,
            ai1: 3f,
            ai2: -1f,
            ai3: _random.NextInt32(-3, 1));
        return true;
    }

    private static bool TryResolveCurrentTarget(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        out NpcSnapshot targeted,
        out VanillaNpcTargetCandidate candidate)
    {
        targeted = npc;
        if (npc.Target < byte.MaxValue &&
            context.TryFindCandidate(checked((byte)npc.Target), out candidate) &&
            candidate.Active &&
            !candidate.Dead &&
            !candidate.Ghost)
        {
            return true;
        }

        if (!context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh closest) ||
            !context.TryFindCandidate(checked((byte)closest.Target), out candidate) ||
            !candidate.Active ||
            candidate.Dead ||
            candidate.Ghost)
        {
            candidate = default;
            return false;
        }

        targeted = npc with { Target = closest.Target };
        return true;
    }

    private static ushort? ResolveClosestTarget(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        out VanillaNpcTargetCandidate candidate)
    {
        if (context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh closest) &&
            context.TryFindCandidate(checked((byte)closest.Target), out candidate) &&
            candidate.Active &&
            !candidate.Dead &&
            !candidate.Ghost)
        {
            return closest.Target;
        }

        candidate = default;
        return null;
    }

    private static NpcStateUpdate Build(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        float velocityX,
        float velocityY,
        ushort target,
        float ai1,
        float ai2,
        float ai3) =>
        new(
            definition.Type.Value,
            npc.NetId,
            npc.PositionX,
            npc.PositionY,
            velocityX,
            velocityY,
            target,
            new NpcAiState(3f, ai1, ai2, ai3),
            npc.Simulation with
            {
                NoGravity = true,
                NoTileCollide = true
            });

    private static bool Normalize(ref float x, ref float y, float magnitude)
    {
        float length = MathF.Sqrt(x * x + y * y);
        if (!float.IsFinite(length) || length <= float.Epsilon)
            return false;
        float scale = magnitude / length;
        x *= scale;
        y *= scale;
        return true;
    }

    private static void ApproachAxis(ref float velocity, float desired, float acceleration)
    {
        if (velocity < desired)
        {
            velocity += acceleration;
            if (velocity < 0f && desired > 0f)
                velocity += acceleration;
        }
        else if (velocity > desired)
        {
            velocity -= acceleration;
            if (velocity > 0f && desired < 0f)
                velocity -= acceleration;
        }
    }

    private static void StopSmall(ref float velocityX, ref float velocityY)
    {
        if (velocityX > -VelocityStopEpsilon && velocityX < VelocityStopEpsilon)
            velocityX = 0f;
        if (velocityY > -VelocityStopEpsilon && velocityY < VelocityStopEpsilon)
            velocityY = 0f;
    }
}
