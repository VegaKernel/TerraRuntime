using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>World facts consumed by TerrariaServer 1.4.5.8 Queen Bee aiStyle 43.</summary>
public interface IVanillaQueenBeeEnvironment
{
    double WorldSurfacePixels { get; }

    float WorldCenterX { get; }

    bool IsPlayerInJungle(float playerCenterX, float playerCenterY);
}

/// <summary>
/// Source-backed server gameplay portion of TerrariaServer 1.4.5.8 Queen Bee aiStyle 43. Presentation-only
/// rotation, dust, sounds and netUpdate flags are excluded; attack-state timing, difficulty/enrage scaling,
/// retreat, target pursuit, defense mutation and source-owned localAI[0] are authoritative.
/// </summary>
internal sealed class VanillaQueenBeeNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    private const float PlayerWidth = VanillaNpcBehaviorContext.BasePlayerWidth;
    private const float PlayerHeight = VanillaNpcBehaviorContext.BasePlayerHeight;
    private readonly IVanillaNpcRandom random;
    private IVanillaQueenBeeEnvironment? environment;
    private IVanillaNpcProjectileEnvironment? projectileEnvironment;

    public VanillaQueenBeeNpcBehaviorStrategy(IVanillaNpcRandom random) =>
        this.random = random ?? throw new ArgumentNullException(nameof(random));

    public void SetEnvironment(IVanillaQueenBeeEnvironment value) =>
        environment = value ?? throw new ArgumentNullException(nameof(value));

    public void SetProjectileEnvironment(IVanillaNpcProjectileEnvironment value) =>
        projectileEnvironment = value ?? throw new ArgumentNullException(nameof(value));

    public bool TryStep(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner,
        out NpcStateUpdate next)
    {
        _ = inner;
        if (definition.AiStyle != VanillaNpcAiStyles.QueenBee ||
            npc.TypeIdentity != VanillaNpcIds.QueenBee ||
            environment is null)
        {
            next = default;
            return false;
        }

        NpcAiState ai = npc.Ai;
        NpcSimulationState simulation = npc.Simulation;
        NpcAiState localAi = simulation.LocalAi;
        float velocityX = npc.VelocityX;
        float velocityY = npc.VelocityY;
        ushort targetSlot = npc.Target;

        bool hasTarget = TryGetTarget(in npc, context, ref targetSlot, out VanillaNpcTargetCandidate target);
        if (!hasTarget)
        {
            ai = ai with { Ai0 = 5f };
            target = default;
        }

        int defense = definition.Defense;
        if (context.ExpertMode && simulation.LifeMax > 0)
            defense += (int)(20f * (1f - (float)simulation.Life / simulation.LifeMax));

        float enrage = hasTarget ? ComputeEnrage(in npc, in target, context, environment) : 0f;
        float distance = hasTarget ? DistanceToTarget(in npc, in definition, in target) : float.PositiveInfinity;
        int timeLeft = simulation.TimeLeft;

        if (ai.Ai0 != 5f)
        {
            if (timeLeft < 60)
                timeLeft = 60;
            if (hasTarget && distance > 3000f)
                ai = ai with { Ai0 = 4f };
        }

        if (!hasTarget)
            ai = ai with { Ai0 = 5f };

        switch ((int)ai.Ai0)
        {
            case 5:
                StepRetreat(in npc, environment, ref velocityX, ref velocityY, ref localAi, ref simulation, ref timeLeft);
                break;

            case -1:
                SelectNextAttack(ref ai);
                break;

            case 0 when hasTarget:
                StepCharge(in npc, in definition, in target, context, enrage, ref ai, ref localAi, ref simulation, ref velocityX, ref velocityY);
                break;

            case 2 when hasTarget:
                RefreshDirection(in npc, in definition, in target, ref simulation);
                StepBeeSummonApproach(in npc, in definition, in target, context.ExpertMode, ref ai, ref velocityX, ref velocityY);
                break;

            case 1 when hasTarget:
                RefreshDirection(in npc, in definition, in target, ref simulation);
                StepBeeSummon(in npc, in definition, in target, context, enrage, ref ai, ref localAi, ref simulation, ref velocityX, ref velocityY);
                break;

            case 3 when hasTarget:
                RefreshDirection(in npc, in definition, in target, ref simulation);
                StepStingerAttack(in npc, in definition, in target, context, enrage, ref ai, ref simulation, ref velocityX, ref velocityY);
                break;

            case 4 when hasTarget:
                StepReturn(in npc, in definition, in target, distance, ref ai, ref localAi, ref simulation, ref velocityX, ref velocityY);
                break;
        }

        simulation = simulation with
        {
            NoGravity = true,
            NoTileCollide = true,
            DefenseOverride = defense,
            TimeLeft = timeLeft,
            LocalAi = localAi,
            JustHit = false
        };
        next = new NpcStateUpdate(
            npc.Type,
            npc.NetId,
            npc.PositionX,
            npc.PositionY,
            velocityX,
            velocityY,
            targetSlot,
            ai,
            simulation);
        return true;
    }

    internal static float ComputeEnrage(
        in NpcSnapshot npc,
        in VanillaNpcTargetCandidate target,
        VanillaNpcBehaviorContext context,
        IVanillaQueenBeeEnvironment environment)
    {
        float enrage = 0f;
        if (npc.PositionY < environment.WorldSurfacePixels)
            enrage += 1f;
        if (!environment.IsPlayerInJungle(target.CenterX, target.CenterY))
            enrage += 1f;
        if (context.GoodWorld)
            enrage += 0.5f;
        return enrage;
    }

    internal static int GetBeeSummonThreshold(float enrage) => (int)(40f - 18f * enrage);

    internal static int GetStingerCadence(in NpcSnapshot npc, bool expertMode, float enrage)
    {
        int cadence = 40;
        if (expertMode)
        {
            cadence = npc.Simulation.LifeMax > 0 && (double)npc.Simulation.Life < npc.Simulation.LifeMax * 0.1
                ? 15
                : npc.Simulation.LifeMax > 0 && npc.Simulation.Life < npc.Simulation.LifeMax / 3
                    ? 25
                    : npc.Simulation.LifeMax > 0 && npc.Simulation.Life < npc.Simulation.LifeMax / 2
                        ? 30
                        : 35;
        }
        cadence -= (int)(5f * enrage);
        return Math.Max(1, cadence);
    }

    internal static bool TryGetTarget(
        in NpcSnapshot npc,
        VanillaNpcBehaviorContext context,
        ref ushort targetSlot,
        out VanillaNpcTargetCandidate target)
    {
        if (targetSlot < byte.MaxValue &&
            context.TryFindCandidate((byte)targetSlot, out target) &&
            target.Active && !target.Dead && !target.Ghost)
        {
            return true;
        }

        if (VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, out VanillaNpcDefinition definition) &&
            context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh refresh) &&
            refresh.HasTarget &&
            refresh.Target < byte.MaxValue &&
            context.TryFindCandidate((byte)refresh.Target, out target) &&
            target.Active && !target.Dead && !target.Ghost)
        {
            targetSlot = refresh.Target;
            return true;
        }

        target = default;
        return false;
    }

    private void SelectNextAttack(ref NpcAiState ai)
    {
        float previous = ai.Ai1;
        int selected;
        do
        {
            selected = random.NextInt32(0, 3);
            selected = selected switch
            {
                1 => 2,
                2 => 3,
                _ => 0
            };
        }
        while (selected == previous);

        ai = ai with { Ai0 = selected, Ai1 = 0f, Ai2 = 0f };
    }

    private static void StepRetreat(
        in NpcSnapshot npc,
        IVanillaQueenBeeEnvironment environment,
        ref float velocityX,
        ref float velocityY,
        ref NpcAiState localAi,
        ref NpcSimulationState simulation,
        ref int timeLeft)
    {
        velocityY *= 0.98f;
        int direction = velocityX < 0f ? -1 : 1;
        simulation = simulation with { DirectionX = direction, SpriteDirection = direction };
        if (npc.PositionX < environment.WorldCenterX)
        {
            if (velocityX > 0f)
                velocityX *= 0.98f;
            else
                localAi = localAi with { Ai0 = 1f };
            velocityX -= 0.08f;
        }
        else
        {
            if (velocityX < 0f)
                velocityX *= 0.98f;
            else
                localAi = localAi with { Ai0 = 1f };
            velocityX += 0.08f;
        }

        if (timeLeft < 0 || timeLeft > 10)
            timeLeft = 10;
    }

    private static void StepCharge(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        in VanillaNpcTargetCandidate target,
        VanillaNpcBehaviorContext context,
        float enrage,
        ref NpcAiState ai,
        ref NpcAiState localAi,
        ref NpcSimulationState simulation,
        ref float velocityX,
        ref float velocityY)
    {
        int charges = 2;
        if (context.ExpertMode && npc.Simulation.LifeMax > 0)
        {
            if (npc.Simulation.Life < npc.Simulation.LifeMax / 2) charges++;
            if (npc.Simulation.Life < npc.Simulation.LifeMax / 3) charges++;
            if (npc.Simulation.Life < npc.Simulation.LifeMax / 5) charges++;
        }
        charges += (int)enrage;

        if (ai.Ai1 > 2 * charges && ai.Ai1 % 2f == 0f)
        {
            ai = ai with { Ai0 = -1f, Ai1 = 0f, Ai2 = 0f };
            return;
        }

        float centerX = npc.PositionX + definition.Width * 0.5f;
        float centerY = npc.PositionY + definition.Height * 0.5f;
        if (ai.Ai1 % 2f == 0f)
        {
            RefreshDirection(in npc, in definition, in target, ref simulation);
            float verticalTolerance = 20f + 20f * enrage;
            if (MathF.Abs(centerY - target.CenterY) < verticalTolerance)
            {
                localAi = localAi with { Ai0 = 1f };
                ai = ai with { Ai1 = ai.Ai1 + 1f, Ai2 = 0f };
                float speed = 12f;
                if (context.ExpertMode)
                {
                    speed = 16f;
                    if (npc.Simulation.LifeMax > 0 && (double)npc.Simulation.Life < npc.Simulation.LifeMax * 0.75) speed += 2f;
                    if (npc.Simulation.LifeMax > 0 && (double)npc.Simulation.Life < npc.Simulation.LifeMax * 0.50) speed += 2f;
                    if (npc.Simulation.LifeMax > 0 && (double)npc.Simulation.Life < npc.Simulation.LifeMax * 0.25) speed += 2f;
                    if (npc.Simulation.LifeMax > 0 && (double)npc.Simulation.Life < npc.Simulation.LifeMax * 0.10) speed += 2f;
                }
                speed += 7f * enrage;
                SetVelocityToward(centerX, centerY, target.CenterX, target.CenterY, speed, ref velocityX, ref velocityY);
                simulation = simulation with { SpriteDirection = simulation.DirectionX };
                return;
            }

            localAi = localAi with { Ai0 = 0f };
            float maxVerticalSpeed = 12f;
            float verticalAcceleration = 0.15f;
            if (context.ExpertMode)
            {
                if (npc.Simulation.LifeMax > 0 && (double)npc.Simulation.Life < npc.Simulation.LifeMax * 0.75) { maxVerticalSpeed += 1f; verticalAcceleration += 0.05f; }
                if (npc.Simulation.LifeMax > 0 && (double)npc.Simulation.Life < npc.Simulation.LifeMax * 0.50) { maxVerticalSpeed += 1f; verticalAcceleration += 0.05f; }
                if (npc.Simulation.LifeMax > 0 && (double)npc.Simulation.Life < npc.Simulation.LifeMax * 0.25) { maxVerticalSpeed += 2f; verticalAcceleration += 0.05f; }
                if (npc.Simulation.LifeMax > 0 && (double)npc.Simulation.Life < npc.Simulation.LifeMax * 0.10) { maxVerticalSpeed += 2f; verticalAcceleration += 0.10f; }
            }
            maxVerticalSpeed += 3f * enrage;
            verticalAcceleration += 0.5f * enrage;
            velocityY += centerY < target.CenterY ? verticalAcceleration : -verticalAcceleration;
            velocityY = Math.Clamp(velocityY, -maxVerticalSpeed, maxVerticalSpeed);

            float horizontalDistance = MathF.Abs(centerX - target.CenterX);
            if (horizontalDistance > 600f)
                velocityX += 0.15f * simulation.DirectionX;
            else if (horizontalDistance < 300f)
                velocityX -= 0.15f * simulation.DirectionX;
            else
                velocityX *= 0.8f;
            velocityX = Math.Clamp(velocityX, -16f, 16f);
            simulation = simulation with { SpriteDirection = simulation.DirectionX };
            return;
        }

        int direction = velocityX < 0f ? -1 : 1;
        simulation = simulation with { DirectionX = direction, SpriteDirection = direction };
        int standOff = 600;
        if (context.ExpertMode && npc.Simulation.LifeMax > 0)
        {
            if ((double)npc.Simulation.Life < npc.Simulation.LifeMax * 0.10) standOff = 300;
            else if ((double)npc.Simulation.Life < npc.Simulation.LifeMax * 0.25) standOff = 450;
            else if ((double)npc.Simulation.Life < npc.Simulation.LifeMax * 0.50) standOff = 500;
            else if ((double)npc.Simulation.Life < npc.Simulation.LifeMax * 0.75) standOff = 550;
        }
        int targetSide = centerX < target.CenterX ? -1 : 1;
        standOff -= (int)(100f * enrage);
        bool transition = false;
        if (direction == targetSide && MathF.Abs(centerX - target.CenterX) > standOff)
        {
            ai = ai with { Ai2 = 1f };
            transition = true;
        }
        if (MathF.Abs(centerY - target.CenterY) > standOff * 1.5f)
        {
            ai = ai with { Ai2 = 1f };
            transition = true;
        }
        if (enrage > 0f && transition)
        {
            velocityX *= 0.5f;
            velocityY *= 0.5f;
        }

        if (ai.Ai2 == 1f)
        {
            RefreshDirection(in npc, in definition, in target, ref simulation);
            localAi = localAi with { Ai0 = 0f };
            velocityX *= 0.9f;
            velocityY *= 0.9f;
            float stopThreshold = 0.1f;
            if (context.ExpertMode && npc.Simulation.LifeMax > 0)
            {
                if (npc.Simulation.Life < npc.Simulation.LifeMax / 2) { velocityX *= 0.9f; velocityY *= 0.9f; stopThreshold += 0.05f; }
                if (npc.Simulation.Life < npc.Simulation.LifeMax / 3) { velocityX *= 0.9f; velocityY *= 0.9f; stopThreshold += 0.05f; }
                if (npc.Simulation.Life < npc.Simulation.LifeMax / 5) { velocityX *= 0.9f; velocityY *= 0.9f; stopThreshold += 0.05f; }
            }
            if (enrage > 0f)
            {
                velocityX *= 0.7f;
                velocityY *= 0.7f;
            }
            if (MathF.Abs(velocityX) + MathF.Abs(velocityY) < stopThreshold)
                ai = ai with { Ai2 = 0f, Ai1 = ai.Ai1 + 1f };
        }
        else
        {
            localAi = localAi with { Ai0 = 1f };
        }
    }

    private static void StepBeeSummonApproach(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        in VanillaNpcTargetCandidate target,
        bool expertMode,
        ref NpcAiState ai,
        ref float velocityX,
        ref float velocityY)
    {
        float centerX = npc.PositionX + definition.Width * 0.5f;
        float centerY = npc.PositionY + definition.Height * 0.5f;
        float dx = target.CenterX - centerX;
        float dy = target.CenterY - 200f - centerY;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        if (distance < 200f)
        {
            ai = ai with { Ai0 = 1f, Ai1 = 0f };
            return;
        }

        float acceleration = expertMode ? 0.1f : 0.07f;
        AccelerateTowardRaw(dx, dy, acceleration, ref velocityX, ref velocityY, doubleOnSignCrossing: false);
    }

    private void StepBeeSummon(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        in VanillaNpcTargetCandidate target,
        VanillaNpcBehaviorContext context,
        float enrage,
        ref NpcAiState ai,
        ref NpcAiState localAi,
        ref NpcSimulationState simulation,
        ref float velocityX,
        ref float velocityY)
    {
        localAi = localAi with { Ai0 = 0f };
        int direction = simulation.DirectionX == 0 ? (target.CenterX < npc.PositionX + definition.Width * 0.5f ? -1 : 1) : simulation.DirectionX;
        float spawnX = npc.PositionX + definition.Width / 2f + random.NextInt32(0, 20) * direction;
        float spawnY = npc.PositionY + definition.Height * 0.8f;
        float centerX = npc.PositionX + definition.Width * 0.5f;
        float centerY = npc.PositionY + definition.Height * 0.5f;
        float dxCenter = target.CenterX - centerX;
        float dyCenter = target.CenterY - centerY;
        float distance = MathF.Sqrt(dxCenter * dxCenter + dyCenter * dyCenter);

        float timer = ai.Ai1 + 1f;
        if (context.ExpertMode)
        {
            timer += context.CountActivePlayersWithin(centerX, centerY, 1000f) / 2;
            if (npc.Simulation.LifeMax > 0 && (double)npc.Simulation.Life < npc.Simulation.LifeMax * 0.75) timer += 0.25f;
            if (npc.Simulation.LifeMax > 0 && (double)npc.Simulation.Life < npc.Simulation.LifeMax * 0.50) timer += 0.25f;
            if (npc.Simulation.LifeMax > 0 && (double)npc.Simulation.Life < npc.Simulation.LifeMax * 0.25) timer += 0.25f;
            if (npc.Simulation.LifeMax > 0 && (double)npc.Simulation.Life < npc.Simulation.LifeMax * 0.10) timer += 0.25f;
        }

        float cycle = ai.Ai2;
        if (timer > GetBeeSummonThreshold(enrage))
        {
            timer = 0f;
            cycle += 1f;
        }
        ai = ai with { Ai1 = timer, Ai2 = cycle };

        bool canHit = CanHit(spawnX, spawnY - 30f, target);
        if (distance > 400f || !canHit)
        {
            float dx = target.CenterX - spawnX;
            float dy = target.CenterY - spawnY;
            AccelerateTowardRaw(dx, dy, 0.1f, ref velocityX, ref velocityY, doubleOnSignCrossing: false);
        }
        else
        {
            velocityX *= 0.9f;
            velocityY *= 0.9f;
        }

        simulation = simulation with { SpriteDirection = direction };
        if (cycle > 5f)
            ai = ai with { Ai0 = -1f, Ai1 = 1f };
    }

    private void StepStingerAttack(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        in VanillaNpcTargetCandidate target,
        VanillaNpcBehaviorContext context,
        float enrage,
        ref NpcAiState ai,
        ref NpcSimulationState simulation,
        ref float velocityX,
        ref float velocityY)
    {
        float speed = context.ExpertMode ? 6f : 4f;
        float acceleration = context.ExpertMode ? 0.075f : 0.05f;
        acceleration += 0.2f * enrage;
        speed += 6f * enrage;
        int direction = simulation.DirectionX == 0 ? (target.CenterX < npc.PositionX + definition.Width * 0.5f ? -1 : 1) : simulation.DirectionX;
        float spawnX = npc.PositionX + definition.Width / 2f + random.NextInt32(0, 20) * direction;
        float spawnY = npc.PositionY + definition.Height * 0.8f;
        float centerX = npc.PositionX + definition.Width * 0.5f;
        float centerY = npc.PositionY + definition.Height * 0.5f;
        float dx = target.CenterX - centerX;
        float dy = target.CenterY - 300f - centerY;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        float timer = ai.Ai1 + 1f;
        ai = ai with { Ai1 = timer };

        if (!CanHit(spawnX, spawnY - 30f, target))
        {
            speed = 14f;
            acceleration = enrage > 0f ? 0.5f : 0.1f;
            dx = target.CenterX - spawnX;
            dy = target.CenterY - spawnY;
            AccelerateTowardRaw(dx, dy, acceleration, ref velocityX, ref velocityY, doubleOnSignCrossing: false);
        }
        else if (distance > 100f)
        {
            AccelerateTowardRaw(dx, dy, acceleration, ref velocityX, ref velocityY, doubleOnSignCrossing: true);
        }

        _ = speed; // The source normalizes a desired vector but compares against raw deltas in this state.
        simulation = simulation with { SpriteDirection = direction };
        int cadence = GetStingerCadence(in npc, context.ExpertMode, enrage);
        float durationMultiplier = 20f - 5f * enrage;
        if (timer > cadence * durationMultiplier)
            ai = ai with { Ai0 = -1f, Ai1 = 3f };
    }

    private static void StepReturn(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        in VanillaNpcTargetCandidate target,
        float distance,
        ref NpcAiState ai,
        ref NpcAiState localAi,
        ref NpcSimulationState simulation,
        ref float velocityX,
        ref float velocityY)
    {
        localAi = localAi with { Ai0 = 1f };
        float centerX = npc.PositionX + definition.Width * 0.5f;
        float centerY = npc.PositionY + definition.Height * 0.5f;
        float desiredX = 0f;
        float desiredY = 0f;
        Normalize(target.CenterX - centerX, target.CenterY - centerY, 14f, ref desiredX, ref desiredY);
        velocityX = (velocityX * 14f + desiredX) / 15f;
        velocityY = (velocityY * 14f + desiredY) / 15f;
        int direction = velocityX < 0f ? -1 : 1;
        simulation = simulation with { DirectionX = direction, SpriteDirection = direction };
        if (distance < 2000f)
        {
            ai = ai with { Ai0 = -1f };
            localAi = localAi with { Ai0 = 0f };
        }
    }

    private bool CanHit(float sourceX, float sourceY, in VanillaNpcTargetCandidate target) =>
        projectileEnvironment?.CanHit(
            sourceX,
            sourceY,
            1,
            1,
            target.CenterX - PlayerWidth * 0.5f,
            target.CenterY - PlayerHeight * 0.5f,
            (int)PlayerWidth,
            (int)PlayerHeight) == true;

    private static void RefreshDirection(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        in VanillaNpcTargetCandidate target,
        ref NpcSimulationState simulation)
    {
        float centerX = npc.PositionX + definition.Width * 0.5f;
        float centerY = npc.PositionY + definition.Height * 0.5f;
        simulation = simulation with
        {
            DirectionX = target.CenterX < centerX ? -1 : 1,
            DirectionY = target.CenterY < centerY ? -1 : 1
        };
    }

    private static float DistanceToTarget(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        in VanillaNpcTargetCandidate target)
    {
        float dx = target.CenterX - (npc.PositionX + definition.Width * 0.5f);
        float dy = target.CenterY - (npc.PositionY + definition.Height * 0.5f);
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static void AccelerateTowardRaw(
        float dx,
        float dy,
        float acceleration,
        ref float velocityX,
        ref float velocityY,
        bool doubleOnSignCrossing)
    {
        if (velocityX < dx)
        {
            velocityX += acceleration;
            if (velocityX < 0f && dx > 0f)
                velocityX += acceleration * (doubleOnSignCrossing ? 2f : 1f);
        }
        else if (velocityX > dx)
        {
            velocityX -= acceleration;
            if (velocityX > 0f && dx < 0f)
                velocityX -= acceleration * (doubleOnSignCrossing ? 2f : 1f);
        }

        if (velocityY < dy)
        {
            velocityY += acceleration;
            if (velocityY < 0f && dy > 0f)
                velocityY += acceleration * (doubleOnSignCrossing ? 2f : 1f);
        }
        else if (velocityY > dy)
        {
            velocityY -= acceleration;
            if (velocityY > 0f && dy < 0f)
                velocityY -= acceleration * (doubleOnSignCrossing ? 2f : 1f);
        }
    }

    private static void SetVelocityToward(
        float sourceX,
        float sourceY,
        float targetX,
        float targetY,
        float speed,
        ref float velocityX,
        ref float velocityY) =>
        Normalize(targetX - sourceX, targetY - sourceY, speed, ref velocityX, ref velocityY);

    private static void Normalize(float x, float y, float speed, ref float outputX, ref float outputY)
    {
        float length = MathF.Sqrt(x * x + y * y);
        if (length <= 0.0001f)
        {
            outputX = 0f;
            outputY = 0f;
            return;
        }
        float scale = speed / length;
        outputX = x * scale;
        outputY = y * scale;
    }
}
