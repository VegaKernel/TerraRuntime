using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

/// <summary>Tile/LOS facts consumed by TerrariaServer 1.4.5.8 AI_054 teleport selection.</summary>
public interface IVanillaBrainOfCthulhuEnvironment
{
    bool IsSolidTile(int tileX, int tileY);

    bool CanHit(
        float sourcePositionX,
        float sourcePositionY,
        int sourceWidth,
        int sourceHeight,
        float targetPositionX,
        float targetPositionY,
        int targetWidth,
        int targetHeight);
}

/// <summary>
/// Source-backed AI_054 gameplay state for Brain of Cthulhu. Child count, invulnerability, both teleport
/// machines, Good World speed and the authoritative player ZoneCrimson escape gate are owned here.
/// Sound/dust/gore remain presentation-only and outside the dedicated-server slice.
/// </summary>
internal sealed class VanillaBrainOfCthulhuNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    private const int TileSize = 16;
    private const float PlayerWidth = 20f;
    private const float PlayerHeight = 42f;
    private const float DespawnDistance = 6000f;

    private readonly IVanillaNpcRandom _random;
    private IVanillaBrainOfCthulhuEnvironment? _environment;

    public VanillaBrainOfCthulhuNpcBehaviorStrategy(IVanillaNpcRandom random) =>
        _random = random ?? throw new ArgumentNullException(nameof(random));

    public void SetEnvironment(IVanillaBrainOfCthulhuEnvironment environment) =>
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public bool TryStep(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner,
        out NpcStateUpdate next)
    {
        _ = inner;
        if (definition.AiStyle != VanillaNpcAiStyles.BrainOfCthulhu ||
            npc.TypeIdentity != VanillaNpcIds.BrainOfCthulhu)
        {
            next = default;
            return false;
        }

        NpcSimulationState simulation = npc.Simulation;
        NpcAiState localAi = simulation.LocalAi;
        NpcAiState ai = npc.Ai;
        float positionX = npc.PositionX;
        float positionY = npc.PositionY;
        float velocityX = npc.VelocityX;
        float velocityY = npc.VelocityY;
        ushort targetSlot = npc.Target;

        bool initializing = localAi.Ai0 == 0f;
        if (initializing)
            localAi = localAi with { Ai0 = 1f };

        if (!TryRefreshClosest(in npc, in definition, context, out VanillaNpcTargetCandidate target, out targetSlot))
        {
            StepEscape(ref ai, ref localAi, ref simulation, ref velocityY);
            next = Build(in npc, positionX, positionY, velocityX, velocityY, targetSlot, in ai, in simulation, in localAi);
            return true;
        }

        float centerX = positionX + definition.Width * 0.5f;
        float centerY = positionY + definition.Height * 0.5f;
        float manhattan = MathF.Abs(centerX - target.CenterX) + MathF.Abs(centerY - target.CenterY);
        if (manhattan > DespawnDistance)
        {
            simulation = simulation with { Life = 0, TimeLeft = 0, JustHit = false, LocalAi = localAi };
            next = Build(in npc, positionX, positionY, velocityX, velocityY, targetSlot, in ai, in simulation, in localAi);
            return true;
        }

        if (ai.Ai0 < 0f)
        {
            simulation = simulation with { DontTakeDamage = false };
            SmoothToward(target.CenterX - centerX, target.CenterY - centerY, 8f, 50f, ref velocityX, ref velocityY);

            if (ai.Ai0 == -1f)
            {
                float timer = localAi.Ai1 + 1f;
                if (simulation.JustHit)
                    timer -= _random.NextInt32(0, 5);

                int threshold = 60 + _random.NextInt32(0, 120) + _random.NextInt32(30, 90);
                if (timer >= threshold && TryChooseTeleport(in target, 10, 12, requireLineOfSight: false, out int tileX, out int tileY))
                {
                    timer = 0f;
                    ai = new NpcAiState(-2f, tileX, tileY, 0f);
                }
                localAi = localAi with { Ai1 = timer };
            }
            else if (ai.Ai0 == -2f)
            {
                velocityX *= 0.9f;
                velocityY *= 0.9f;
                float fade = MathF.Min(255f, ai.Ai3 + 15f);
                ai = ai with { Ai3 = fade };
                simulation = simulation with { Alpha = (int)fade };
                if (fade >= 255f)
                {
                    positionX = ai.Ai1 * TileSize - definition.Width * 0.5f;
                    positionY = ai.Ai2 * TileSize - definition.Height * 0.5f;
                    ai = ai with { Ai0 = -3f };
                }
            }
            else if (ai.Ai0 == -3f)
            {
                float fade = MathF.Max(0f, ai.Ai3 - 15f);
                ai = ai with { Ai3 = fade };
                simulation = simulation with { Alpha = (int)fade };
                if (fade <= 0f)
                    ai = ai with { Ai0 = -1f };
            }
        }
        else
        {
            MoveDirect(target.CenterX - centerX, target.CenterY - centerY, context.GoodWorld ? 3f : 1f, ref velocityX, ref velocityY);

            if (ai.Ai0 == 0f)
            {
                if (!initializing && context.CountNpcPeers(VanillaNpcIds.BrainCreeper) == 0)
                {
                    ai = ai with { Ai0 = -1f };
                    localAi = localAi with { Ai1 = 0f };
                    simulation = simulation with { Alpha = 0 };
                }

                float timer = localAi.Ai1 + 1f;
                int threshold = 120 + _random.NextInt32(0, 300);
                if (ai.Ai0 == 0f &&
                    timer >= threshold &&
                    TryChooseTeleport(in target, 12, 40, requireLineOfSight: true, out int tileX, out int tileY))
                {
                    timer = 0f;
                    ai = new NpcAiState(1f, tileX, tileY, 0f);
                }
                localAi = localAi with { Ai1 = timer };
            }
            else if (ai.Ai0 == 1f)
            {
                int alpha = Math.Min(255, simulation.Alpha + 5);
                simulation = simulation with { Alpha = alpha };
                if (alpha >= 255)
                {
                    positionX = ai.Ai1 * TileSize - definition.Width * 0.5f;
                    positionY = ai.Ai2 * TileSize - definition.Height * 0.5f;
                    ai = ai with { Ai0 = 2f };
                }
            }
            else if (ai.Ai0 == 2f)
            {
                int alpha = Math.Max(0, simulation.Alpha - 5);
                simulation = simulation with { Alpha = alpha };
                if (alpha <= 0)
                    ai = ai with { Ai0 = 0f };
            }
        }

        if (target.HasBiomeZoneFacts && !target.ZoneCrimson)
            StepEscape(ref ai, ref localAi, ref simulation, ref velocityY);
        else if (localAi.Ai3 > 0f)
            localAi = localAi with { Ai3 = localAi.Ai3 - 1f };

        simulation = simulation with
        {
            NoGravity = true,
            NoTileCollide = true,
            LocalAi = localAi,
            JustHit = false
        };
        next = Build(in npc, positionX, positionY, velocityX, velocityY, targetSlot, in ai, in simulation, in localAi);
        return true;
    }

    private bool TryChooseTeleport(
        in VanillaNpcTargetCandidate target,
        int minimumTiles,
        int maximumTiles,
        bool requireLineOfSight,
        out int tileX,
        out int tileY)
    {
        if (_environment is null)
        {
            tileX = 0;
            tileY = 0;
            return false;
        }

        for (int attempt = 1; attempt <= 101; attempt++)
        {
            int offsetX = _random.NextInt32(minimumTiles, maximumTiles + 1);
            int offsetY = _random.NextInt32(minimumTiles, maximumTiles + 1);
            if (_random.NextInt32(0, 2) == 0)
                offsetX *= -1;
            if (_random.NextInt32(0, 2) == 0)
                offsetY *= -1;

            float vectorX = offsetX * TileSize;
            float vectorY = offsetY * TileSize;
            AddVelocityLead(in target, ref vectorX, ref vectorY);

            tileX = (int)(target.CenterX / TileSize) + (int)(vectorX / TileSize);
            tileY = (int)(target.CenterY / TileSize) + (int)(vectorY / TileSize);
            if (attempt > 100)
                return true;
            if (_environment.IsSolidTile(tileX, tileY))
                continue;
            if (!requireLineOfSight || attempt > 75 ||
                _environment.CanHit(
                    tileX * TileSize,
                    tileY * TileSize,
                    1,
                    1,
                    target.CenterX - PlayerWidth * 0.5f,
                    target.CenterY - PlayerHeight * 0.5f,
                    (int)PlayerWidth,
                    (int)PlayerHeight))
            {
                return true;
            }
        }

        tileX = 0;
        tileY = 0;
        return false;
    }

    private static void AddVelocityLead(in VanillaNpcTargetCandidate target, ref float vectorX, ref float vectorY)
    {
        float vectorLength = MathF.Sqrt(vectorX * vectorX + vectorY * vectorY);
        float playerSpeed = MathF.Sqrt(target.VelocityX * target.VelocityX + target.VelocityY * target.VelocityY);
        if (vectorLength <= float.Epsilon || playerSpeed <= float.Epsilon)
            return;

        float dot =
            target.VelocityX / playerSpeed * (vectorX / vectorLength) +
            target.VelocityY / playerSpeed * (vectorY / vectorLength);
        if (dot <= 0f)
            return;

        vectorX += vectorX / vectorLength * TileSize * playerSpeed;
        vectorY += vectorY / vectorLength * TileSize * playerSpeed;
    }

    private static bool TryRefreshClosest(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        out VanillaNpcTargetCandidate target,
        out ushort targetSlot)
    {
        if (context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh closest) &&
            closest.Target < byte.MaxValue &&
            context.TryFindCandidate(checked((byte)closest.Target), out target) &&
            target.Active && !target.Dead && !target.Ghost)
        {
            targetSlot = closest.Target;
            return true;
        }

        target = default;
        targetSlot = npc.Target;
        return false;
    }

    private static void StepEscape(
        ref NpcAiState ai,
        ref NpcAiState localAi,
        ref NpcSimulationState simulation,
        ref float velocityY)
    {
        float timer = MathF.Min(120f, localAi.Ai3 + 1f);
        if (timer > 60f)
            velocityY += (timer - 60f) * 0.25f;
        localAi = localAi with { Ai3 = timer };
        ai = ai with { Ai0 = 2f };
        simulation = simulation with
        {
            Alpha = 10,
            LocalAi = localAi,
            JustHit = false
        };
    }

    private static void MoveDirect(
        float deltaX,
        float deltaY,
        float speed,
        ref float velocityX,
        ref float velocityY)
    {
        float distance = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (!float.IsFinite(distance) || distance <= float.Epsilon)
        {
            velocityX = 0f;
            velocityY = 0f;
            return;
        }

        if (distance < speed)
        {
            velocityX = deltaX;
            velocityY = deltaY;
            return;
        }

        float scale = speed / distance;
        velocityX = deltaX * scale;
        velocityY = deltaY * scale;
    }

    private static void SmoothToward(
        float deltaX,
        float deltaY,
        float speed,
        float inertia,
        ref float velocityX,
        ref float velocityY)
    {
        float distance = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (!float.IsFinite(distance) || distance <= float.Epsilon)
            return;
        float scale = speed / distance;
        velocityX = (velocityX * inertia + deltaX * scale) / (inertia + 1f);
        velocityY = (velocityY * inertia + deltaY * scale) / (inertia + 1f);
    }

    private static NpcStateUpdate Build(
        in NpcSnapshot npc,
        float positionX,
        float positionY,
        float velocityX,
        float velocityY,
        ushort target,
        in NpcAiState ai,
        in NpcSimulationState simulation,
        in NpcAiState localAi) =>
        new(
            npc.Type,
            npc.NetId,
            positionX,
            positionY,
            velocityX,
            velocityY,
            target,
            ai,
            simulation with { LocalAi = localAi });
}

/// <summary>Source-backed AI_055 orbit/charge behavior for Brain Creepers.</summary>
internal sealed class VanillaBrainCreeperNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    private readonly IVanillaNpcRandom _random;

    public VanillaBrainCreeperNpcBehaviorStrategy(IVanillaNpcRandom random) =>
        _random = random ?? throw new ArgumentNullException(nameof(random));

    public bool TryStep(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner,
        out NpcStateUpdate next)
    {
        _ = inner;
        if (definition.AiStyle != VanillaNpcAiStyles.BrainCreeper ||
            npc.TypeIdentity != VanillaNpcIds.BrainCreeper)
        {
            next = default;
            return false;
        }

        NpcSimulationState simulation = npc.Simulation;
        if (!context.TryFindFirstNpcPeer(VanillaNpcIds.BrainOfCthulhu, out NpcSnapshot brain) ||
            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.BrainOfCthulhu, out VanillaNpcDefinition brainDefinition))
        {
            simulation = simulation with { Life = 0, TimeLeft = 0, JustHit = false };
            next = new NpcStateUpdate(
                npc.Type, npc.NetId, npc.PositionX, npc.PositionY, npc.VelocityX, npc.VelocityY,
                npc.Target, npc.Ai, simulation);
            return true;
        }

        float centerX = npc.PositionX + definition.Width * 0.5f;
        float centerY = npc.PositionY + definition.Height * 0.5f;
        float brainCenterX = brain.PositionX + brainDefinition.Width * 0.5f;
        float brainCenterY = brain.PositionY + brainDefinition.Height * 0.5f;
        float velocityX = npc.VelocityX;
        float velocityY = npc.VelocityY;
        NpcAiState ai = npc.Ai;
        ushort targetSlot = npc.Target;

        if (ai.Ai0 == 0f)
        {
            ai = ai with { Ai1 = 0f };
            float deltaX = brainCenterX - centerX;
            float deltaY = brainCenterY - centerY;
            float distance = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
            if (distance > 90f)
            {
                SmoothToward(deltaX, deltaY, 8f, 15f, ref velocityX, ref velocityY);
                simulation = simulation with { JustHit = false };
                next = Build(in npc, velocityX, velocityY, targetSlot, in ai, in simulation);
                return true;
            }

            if (MathF.Abs(velocityX) + MathF.Abs(velocityY) < 8f)
            {
                velocityX *= 1.05f;
                velocityY *= 1.05f;
            }

            bool charge = context.ExpertMode && _random.NextInt32(0, 100) == 0;
            if (!charge)
                charge = _random.NextInt32(0, 200) == 0;
            if (charge &&
                context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh closest) &&
                closest.Target < byte.MaxValue &&
                context.TryFindCandidate(checked((byte)closest.Target), out VanillaNpcTargetCandidate target) &&
                target.Active && !target.Dead && !target.Ghost)
            {
                targetSlot = closest.Target;
                SetSpeed(target.CenterX - centerX, target.CenterY - centerY, 8f, ref velocityX, ref velocityY);
                ai = ai with { Ai0 = 1f };
            }

            simulation = simulation with { JustHit = false };
            next = Build(in npc, velocityX, velocityY, targetSlot, in ai, in simulation);
            return true;
        }

        if (context.ExpertMode &&
            targetSlot < byte.MaxValue &&
            context.TryFindCandidate(checked((byte)targetSlot), out VanillaNpcTargetCandidate current) &&
            current.Active && !current.Dead && !current.Ghost)
        {
            float speed = context.GoodWorld ? 12f : 9f;
            float inertia = context.GoodWorld ? 49f : 99f;
            SmoothToward(current.CenterX - centerX, current.CenterY - centerY, speed, inertia, ref velocityX, ref velocityY);
        }

        float brainDeltaX = brainCenterX - centerX;
        float brainDeltaY = brainCenterY - centerY;
        float brainDistance = MathF.Sqrt(brainDeltaX * brainDeltaX + brainDeltaY * brainDeltaY);
        if (brainDistance > 700f || simulation.JustHit)
            ai = ai with { Ai0 = 0f };

        simulation = simulation with { JustHit = false };
        next = Build(in npc, velocityX, velocityY, targetSlot, in ai, in simulation);
        return true;
    }

    private static void SmoothToward(
        float deltaX,
        float deltaY,
        float speed,
        float inertia,
        ref float velocityX,
        ref float velocityY)
    {
        float distance = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (!float.IsFinite(distance) || distance <= float.Epsilon)
            return;
        float scale = speed / distance;
        velocityX = (velocityX * inertia + deltaX * scale) / (inertia + 1f);
        velocityY = (velocityY * inertia + deltaY * scale) / (inertia + 1f);
    }

    private static void SetSpeed(
        float deltaX,
        float deltaY,
        float speed,
        ref float velocityX,
        ref float velocityY)
    {
        float distance = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (!float.IsFinite(distance) || distance <= float.Epsilon)
            return;
        float scale = speed / distance;
        velocityX = deltaX * scale;
        velocityY = deltaY * scale;
    }

    private static NpcStateUpdate Build(
        in NpcSnapshot npc,
        float velocityX,
        float velocityY,
        ushort target,
        in NpcAiState ai,
        in NpcSimulationState simulation) =>
        new(
            npc.Type,
            npc.NetId,
            npc.PositionX,
            npc.PositionY,
            velocityX,
            velocityY,
            target,
            ai,
            simulation with { NoGravity = true, NoTileCollide = true });
}
