using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Core.Npcs;

internal sealed class VanillaFishNpcBehaviorStrategy : IVanillaNpcBehaviorStrategy
{
    private readonly IVanillaNpcRandom random;
    private IVanillaFishEnvironment1458? environment;

    public VanillaFishNpcBehaviorStrategy(IVanillaNpcRandom random) =>
        this.random = random ?? throw new ArgumentNullException(nameof(random));

    public void SetEnvironment(IVanillaFishEnvironment1458 value) =>
        environment = value ?? throw new ArgumentNullException(nameof(value));

    public bool TryStep(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        INpcAiStateStepper inner,
        out NpcStateUpdate next)
    {
        if (definition.AiStyle != VanillaNpcAiStyles.Fish ||
            !VanillaFishNpcCatalog1458.TryGetDefinition(definition.Type, out _) ||
            !definition.TryResolveHitbox(npc.Simulation.Scale, out VanillaNpcHitboxSize hitbox) ||
            environment is null)
        {
            next = default;
            return false;
        }

        VanillaBlueSlimeTargetRefresh closest =
            context.TrySelectClosestTarget(in npc, in definition, out VanillaBlueSlimeTargetRefresh selected)
                ? selected
                : default;
        bool pursuingWetTarget = false;
        bool targetTopBelowNpcTop = false;
        if (closest.HasTarget &&
            context.TryFindCandidate(checked((byte)closest.Target), out VanillaNpcTargetCandidate target))
        {
            targetTopBelowNpcTop =
                target.CenterY - VanillaPlayerHitboxFacts.BaseHeight * 0.5f > npc.PositionY;
            if (!VanillaFishNpcCatalog1458.IsPassiveFish(definition.Type) && target.Wet)
            {
                pursuingWetTarget = environment.CanHit(
                    npc.PositionX,
                    npc.PositionY,
                    hitbox.Width,
                    hitbox.Height,
                    target.CenterX - VanillaPlayerHitboxFacts.BaseWidth * 0.5f,
                    target.CenterY - VanillaPlayerHitboxFacts.BaseHeight * 0.5f,
                    (int)VanillaPlayerHitboxFacts.BaseWidth,
                    (int)VanillaPlayerHitboxFacts.BaseHeight);
            }
        }

        NpcSimulationState simulation = npc.Simulation;
        int dolphinWaitThreshold = 300;
        int dolphinNextState = 1;
        bool dolphinCanHitLineAbove = true;
        bool dolphinWillUseSpecialState = false;
        if (definition.Type == VanillaNpcIds.Dolphin)
        {
            dolphinCanHitLineAbove = environment.CanHitLineStraightAbove(
                npc.PositionX,
                npc.PositionY,
                hitbox.Width,
                hitbox.Height,
                distance: 128f);
            if (npc.Ai.Ai2 == 0f)
            {
                dolphinWaitThreshold = random.NextInt32(300, 1200);
                if (npc.Ai.Ai3 + 1f >= dolphinWaitThreshold)
                {
                    dolphinNextState = random.NextInt32(1, 3);
                    dolphinWillUseSpecialState = true;
                }
            }
            else if (npc.Ai.Ai2 is 1f or 2f)
            {
                dolphinWillUseSpecialState = true;
            }
        }

        bool pufferfishWillUseSpecialState =
            definition.Type == VanillaNpcIds.Pufferfish &&
            (simulation.JustHit ||
             (npc.Ai.Ai2 == 1f && simulation.LocalAi.Ai0 - 1f > 0f));
        bool needsGroundedLaunch =
            !simulation.Wet &&
            npc.VelocityY == 0f &&
            !dolphinWillUseSpecialState &&
            !pufferfishWillUseSpecialState &&
            !VanillaFishNpcCatalog1458.UsesGroundedHorizontalDamping(definition.Type);
        float launchVelocityY = 0f;
        float launchVelocityX = 0f;
        int launchDirection = 0;
        if (needsGroundedLaunch)
        {
            launchVelocityY = random.NextInt32(-50, -20) * 0.1f;
            launchVelocityX = random.NextInt32(-20, 20) * 0.1f;
            launchDirection = random.NextInt32(0, 2) == 0 ? 1 : -1;
        }

        bool hasWaterLine = environment.TryGetWaterLineAtTop(
            npc.PositionX,
            npc.PositionY,
            hitbox.Width,
            out float waterLineHeight);

        var input = new VanillaFishMotionInput1458(
            npc.VelocityX,
            npc.VelocityY,
            npc.PositionY,
            npc.PositionY + hitbox.Height * 0.5f,
            simulation.DirectionX,
            simulation.DirectionY,
            npc.Target,
            npc.Ai,
            simulation.LocalAi,
            simulation.JustHit,
            simulation.Wet,
            simulation.CollideX,
            simulation.CollideY,
            environment.GetBottomSlope(npc.PositionX, npc.PositionY, hitbox.Width, hitbox.Height),
            environment.HasDeepLiquidAboveAndActiveTileBelow(
                npc.PositionX,
                npc.PositionY,
                hitbox.Width,
                hitbox.Height),
            closest,
            pursuingWetTarget,
            targetTopBelowNpcTop,
            needsGroundedLaunch,
            launchVelocityX,
            launchVelocityY,
            launchDirection,
            dolphinWaitThreshold,
            dolphinNextState,
            dolphinCanHitLineAbove,
            hasWaterLine,
            waterLineHeight);
        if (!VanillaFishMotion1458.TryStep(definition.Type, in input, out VanillaFishMotionResult1458 result))
        {
            next = default;
            return false;
        }

        next = new NpcStateUpdate(
            definition.Type.Value,
            npc.NetId,
            npc.PositionX,
            npc.PositionY,
            result.VelocityX,
            result.VelocityY,
            result.Target,
            result.Ai,
            simulation with
            {
                DirectionX = result.DirectionX,
                DirectionY = result.DirectionY,
                NoGravity = true,
                NoTileCollide = false,
                LocalAi = result.LocalAi
            });
        return true;
    }
}
