using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Production adapter for the verified TerrariaServer 1.4.5.8 King Slime aiStyle 15 primitive. Mutable world
/// traversal remains behind IVanillaKingSlimeEnvironment; the returned AI/localAI/scale/visibility transition is
/// still committed atomically through the ordinary generation-safe NPC state executor.
/// </summary>
internal sealed class VanillaKingSlimeNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    private IVanillaKingSlimeEnvironment? _environment;

    public VanillaKingSlimeNpcBehaviorStrategy(IVanillaKingSlimeEnvironment? environment) =>
        _environment = environment;

    public void SetEnvironment(IVanillaKingSlimeEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _environment = environment;
    }

    public bool TryStep(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner,
        out NpcStateUpdate next)
    {
        IVanillaKingSlimeEnvironment? environment = _environment;
        if (definition.Type != VanillaNpcIds.KingSlime ||
            definition.AiStyle != VanillaNpcAiStyles.KingSlime ||
            !definition.IsBoss ||
            environment is null ||
            !definition.TryResolveHitbox(npc.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
        {
            next = default;
            return false;
        }

        VanillaNpcTargetCandidate current = default;
        if (npc.Target < byte.MaxValue)
            context.TryFindCandidate(checked((byte)npc.Target), out current);

        VanillaNpcTargetCandidate closest = default;
        if (context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh selected))
            context.TryFindCandidate(checked((byte)selected.Target), out closest);

        float centerX = npc.PositionX + hitbox.Width * 0.5f;
        float centerY = npc.PositionY + hitbox.Height * 0.5f;
        bool canHitTarget = current.Active && !current.Ghost &&
                            environment.CanHitLine(centerX, centerY, current.CenterX, current.CenterY);
        NpcSimulationState simulation = npc.Simulation;
        var input = new VanillaKingSlimeMotionInput(
            PositionX: npc.PositionX,
            PositionY: npc.PositionY,
            VelocityX: npc.VelocityX,
            VelocityY: npc.VelocityY,
            DirectionX: simulation.DirectionX,
            Target: npc.Target,
            Ai: npc.Ai,
            LocalAi: new VanillaKingSlimeLocalAi(
                simulation.LocalAi.Ai0,
                simulation.LocalAi.Ai1,
                simulation.LocalAi.Ai2,
                simulation.LocalAi.Ai3),
            Life: simulation.LifeMax > 0 ? simulation.Life : definition.LifeMax,
            LifeMax: simulation.LifeMax > 0 ? simulation.LifeMax : definition.LifeMax,
            TimeLeft: simulation.TimeLeft >= 0 ? simulation.TimeLeft : VanillaNpcDefinitionCatalog.DefaultTimeLeft,
            Scale: simulation.Scale,
            GoodWorld: context.GoodWorld,
            CanHitTarget: canHitTarget,
            TargetCandidate: current,
            ClosestCandidate: closest,
            HasTeleportDestination: false,
            TeleportBottomX: 0f,
            TeleportBottomY: 0f,
            WorldPixelWidth: environment.WorldPixelWidth,
            WorldPixelHeight: environment.WorldPixelHeight);

        if (VanillaKingSlimeMotion.RequiresTeleportDestination(in input, out bool antiCheese))
        {
            VanillaNpcTargetCandidate teleportTarget = current.Active && !current.Ghost ? current : closest;
            if (!environment.TryResolveTeleport(
                    in npc,
                    in definition,
                    in teleportTarget,
                    antiCheese,
                    out VanillaKingSlimeTeleportDestination destination) ||
                !destination.IsFinite)
            {
                next = default;
                return false;
            }

            input = input with
            {
                HasTeleportDestination = true,
                TeleportBottomX = destination.BottomX,
                TeleportBottomY = destination.BottomY
            };
        }

        if (!VanillaKingSlimeMotion.TryStep(in input, out VanillaKingSlimeMotionResult result))
        {
            next = default;
            return false;
        }

        next = new NpcStateUpdate(
            definition.Type.Value,
            npc.NetId,
            result.PositionX,
            result.PositionY,
            result.VelocityX,
            result.VelocityY,
            result.Target,
            result.Ai,
            simulation with
            {
                DirectionX = result.DirectionX,
                NoGravity = false,
                NoTileCollide = false,
                TimeLeft = result.TimeLeft,
                Scale = result.Scale,
                LocalAi = new NpcAiState(
                    result.LocalAi.TeleportPressure,
                    result.LocalAi.TeleportBottomX,
                    result.LocalAi.TeleportBottomY,
                    result.LocalAi.Initialized),
                Hidden = result.Hidden,
                DontTakeDamage = result.DontTakeDamage
            });
        return true;
    }
}
