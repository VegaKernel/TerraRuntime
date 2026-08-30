using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Compatibility facade for verified vanilla NPC targeting/AI dispatch. The facade resolves one version-pinned
/// definition and delegates the state step to an explicit runtime-owned behavior family strategy. Family-specific
/// targeting geometry, world conditions and motion rules are kept outside this dispatcher. Boss NPC spawn side
/// effects are exposed separately as speculative intents and are committed only by RuntimeNpcAiStateExecutor
/// after the source state transition succeeds.
/// </summary>
public sealed class VanillaNpcTargetingAiStepper : INpcAiStateStepper, INpcAiSpawnIntentPlanner
{
    public const int MaximumPlayerCandidates = VanillaNpcBehaviorContext.MaximumPlayerCandidates;

    private const float EyeOfCthulhuServantCadenceThreshold = 110f;
    private const float EyeOfCthulhuServantSpeed = 5f;
    private const float EyeOfCthulhuServantSpawnLeadTicks = 10f;

    private readonly INpcAiStateStepper _inner;
    private readonly VanillaNpcBehaviorContext _context = new();
    private readonly IVanillaNpcBehaviorStrategy _slimeGround = new VanillaSlimeGroundNpcBehaviorStrategy();
    private readonly IVanillaNpcBehaviorStrategy _flyingEye = new VanillaFlyingEyeNpcBehaviorStrategy();
    private readonly IVanillaNpcBehaviorStrategy _groundFighter = new VanillaGroundFighterNpcBehaviorStrategy();
    private readonly IVanillaNpcBehaviorStrategy _eyeOfCthulhu = new VanillaEyeOfCthulhuNpcBehaviorStrategy();
    private readonly IVanillaNpcBehaviorStrategy _flyer = new VanillaServantOfCthulhuNpcBehaviorStrategy();

    public VanillaNpcTargetingAiStepper(INpcAiStateStepper inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public void EnableBlueSlimeMotion(double worldSurfaceTiles = double.PositiveInfinity) =>
        _context.EnableSlimeGround(worldSurfaceTiles);

    public void EnableZombieMotion(double worldSurfaceTiles) =>
        _context.EnableGroundFighter(worldSurfaceTiles);

    public void SetWorldConditions(bool dayTime, bool slimeRainActive) =>
        _context.SetWorldConditions(dayTime, slimeRainActive);

    public void SetCandidates(ReadOnlySpan<VanillaNpcTargetCandidate> candidates) =>
        _context.SetCandidates(candidates);

    public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
    {
        if (!NpcTypeId.TryCreate(npc.Type, out NpcTypeId npcType))
        {
            next = default;
            return false;
        }

        if (!VanillaNpcDefinitionCatalog.TryGet(npcType, out VanillaNpcDefinition definition))
            return _inner.TryStepState(in npc, out next);

        IVanillaNpcBehaviorStrategy? strategy = definition.BehaviorFamily switch
        {
            VanillaNpcBehaviorFamily.SlimeGround when _context.SlimeGroundEnabled => _slimeGround,
            VanillaNpcBehaviorFamily.FlyingEye => _flyingEye,
            VanillaNpcBehaviorFamily.GroundFighter when _context.GroundFighterEnabled => _groundFighter,
            VanillaNpcBehaviorFamily.EyeOfCthulhu => _eyeOfCthulhu,
            VanillaNpcBehaviorFamily.Flyer when definition.Type == VanillaNpcIds.ServantOfCthulhu => _flyer,
            _ => null
        };

        return strategy is null
            ? _inner.TryStepState(in npc, out next)
            : strategy.TryStep(in npc, in definition, _context, _inner, out next);
    }

    public bool TryPlanNpcSpawn(
        in NpcSnapshot source,
        in NpcStateUpdate proposed,
        out NpcAiSpawnIntent intent)
    {
        if (source.Type != VanillaNpcIds.EyeOfCthulhu.Value ||
            proposed.Type != VanillaNpcIds.EyeOfCthulhu.Value ||
            source.Ai.Ai0 != 0f ||
            source.Ai.Ai1 != 0f ||
            proposed.Ai.Ai0 != 0f ||
            proposed.Ai.Ai1 != 0f ||
            source.Ai.Ai3 < EyeOfCthulhuServantCadenceThreshold - 1f ||
            proposed.Ai.Ai3 != 0f ||
            proposed.Ai.Ai2 != source.Ai.Ai2 + 1f ||
            proposed.Ai.Ai2 >= 600f ||
            proposed.Target >= byte.MaxValue ||
            !_context.TryFindCandidate(checked((byte)proposed.Target), out VanillaNpcTargetCandidate target) ||
            !target.Active ||
            target.Dead ||
            target.Ghost ||
            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.EyeOfCthulhu, out VanillaNpcDefinition eye))
        {
            intent = default;
            return false;
        }

        float centerX = source.PositionX + eye.Width * 0.5f;
        float centerY = source.PositionY + eye.Height * 0.5f;
        float deltaX = target.CenterX - centerX;
        float deltaY = target.CenterY - centerY;
        float distance = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (!float.IsFinite(distance) || distance <= float.Epsilon)
        {
            intent = default;
            return false;
        }

        float scale = EyeOfCthulhuServantSpeed / distance;
        float velocityX = deltaX * scale;
        float velocityY = deltaY * scale;
        int bottomX = (int)(centerX + velocityX * EyeOfCthulhuServantSpawnLeadTicks);
        int bottomY = (int)(centerY + velocityY * EyeOfCthulhuServantSpawnLeadTicks);

        intent = new NpcAiSpawnIntent(
            Type: VanillaNpcIds.ServantOfCthulhu,
            BottomX: bottomX,
            BottomY: bottomY,
            VelocityX: velocityX,
            VelocityY: velocityY,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget);
        return true;
    }
}
