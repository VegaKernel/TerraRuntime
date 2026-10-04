using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Core.Npcs;

internal interface IVanillaSlimeContainedEnvironment1458
{
    bool TryCapture(in NpcSnapshot parent, in VanillaNpcTargetCandidate target,
        out IVanillaSlimeContainedWorld1458 world);
}

internal interface IVanillaSlimeContainedWorld1458
{
    bool IsCurrent { get; }
    bool CanHit { get; }
    bool TryReadBirthWet(in NpcSnapshot birth, out bool wet);
}

internal sealed partial class VanillaSlimeGroundNpcBehaviorStrategy
{
    private RuntimeNpcStore? containedStore;
    private Func<VanillaSlimeContainedFacts1458>? containedFacts;
    private IVanillaSlimeContainedEnvironment1458? containedEnvironment;
    private ContainedPlan? containedPlan;

    private sealed record ContainedPlan(NpcSnapshot Before, NpcStateUpdate Update,
        RuntimeNpcStore.AiSpawnPreview Preview, VanillaNpcBehaviorContext Context,
        VanillaNpcTargetCandidate[] Candidates, PlayerStateSnapshot[] Players,
        VanillaSlimeContainedFacts1458 Facts, bool DayTime, IVanillaSlimeContainedWorld1458 World,
        NpcAiProjectileIntent? Trap);

    internal void SetContainedOwner(RuntimeNpcStore store, Func<VanillaSlimeContainedFacts1458> facts,
        IVanillaSlimeContainedEnvironment1458 environment)
    {
        containedStore = store ?? throw new ArgumentNullException(nameof(store));
        containedFacts = facts ?? throw new ArgumentNullException(nameof(facts));
        containedEnvironment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    internal bool HasContainedPlan(in NpcSnapshot source) => containedPlan?.Before == source;
    internal void CancelContainedPlan() => containedPlan = null;

    private bool TryRetainContained(in NpcSnapshot npc, in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context, INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        containedPlan = null;
        next = default;
        if (random is not SystemVanillaNpcRandom trusted || containedStore is null ||
            containedFacts is null || containedEnvironment is null ||
            !definition.TryResolveHitbox(npc.Simulation, out var body) ||
            npc.Simulation.MoneyValue is not float money || npc.Ai.Ai1 != (int)npc.Ai.Ai1 ||
            npc.Target >= byte.MaxValue || !context.TryFindCandidate((byte)npc.Target, out var target) ||
            !target.Active || target.Dead || target.Ghost || target.NoAggro || target.Aggro < 0 ||
            !context.TrySelectClosestTarget(in npc, in definition, out _))
            return false;

        var candidates = context.Candidates.ToArray();
        var players = new List<PlayerStateSnapshot>(candidates.Length);
        foreach (var candidate in candidates)
        {
            if (!candidate.Active)
                continue;
            if (!context.TryGetOwnedPlayer(candidate.Slot, out var player) || !MatchesContainedBody(candidate, in player))
                return false;
            players.Add(player);
        }

        var facts = containedFacts() with
        {
            HasHeartSlime = context.HasNpcPeerWithAi1(VanillaNpcIds.BlueSlime, 29f)
        };
        if (!containedStore.TryCreateAiSpawnPreview(in npc, trusted.SourceRandom, out var preview))
            return false;

        var speculativeRandom = new SystemVanillaNpcRandom(preview!.Random);
        VanillaSlimeContainedInitializer1458.ObserveBeforeSelection(npc.Type, (int)npc.Ai.Ai1, speculativeRandom);
        var input = new VanillaSlimeContainedInput1458(npc.Type, npc.NetId, npc.PositionY, body.Height,
            money, (int)npc.Ai.Ai1, npc.Ai.Ai0 == -999f);
        if (!VanillaSlimeContainedInitializer1458.TrySelect(in input, in facts, speculativeRandom,
                out var selection) || !selection.Admitted)
            return false;

        var effects = VanillaSlimeContainedInitializer1458.ObserveContents(selection.Item,
            facts.GoodWorld, facts.NoTrapsWorld, speculativeRandom);
        IVanillaSlimeContainedWorld1458 world = NoContainedProducerWorld.Instance;
        if ((effects.Trap || effects.HiveType != 0) &&
            (!containedEnvironment.TryCapture(in npc, in target, out world) || !world.IsCurrent))
            return false;

        NpcAiProjectileIntent? trap = null;
        if (effects.Trap && world.CanHit)
        {
            if (!VanillaDefinitionCatalog.TryGet(VanillaProjectileIds.ContainedSlimeTrap, out VanillaProjectileDefinition projectile))
                return false;
            trap = new NpcAiProjectileIntent(VanillaProjectileIds.ContainedSlimeTrap,
                (int)(npc.PositionX + body.Width * .5f) - projectile.Width * .5f,
                npc.PositionY + body.Height * .5f - projectile.Height * .5f,
                npc.Simulation.DirectionX * 12f, 0f, 20, 2f);
        }

        if (effects.HiveType != 0 && world.CanHit)
        {
            int type = speculativeRandom.NextInt32(VanillaNpcIds.Bee.Value, VanillaNpcIds.SmallBee.Value + 1);
            var intent = new NpcAiSpawnIntent(new(type),
                (int)(npc.PositionX + body.Width * .5f), (int)(npc.PositionY + body.Height * .5f),
                0f, 0f, byte.MaxValue);
            bool allocated = preview.TryStageHiveChild(in intent, out var birth);
            float velocityX = speculativeRandom.NextInt32(-20, 21) * .1f;
            float inverse = 1f / (float)Math.Sqrt(velocityX * velocityX + 1f);
            float childX = velocityX * inverse * 3f;
            float childY = -inverse * 3f;
            if (allocated && (!world.TryReadBirthWet(in birth, out bool wet) ||
                !preview.TryFinishHiveChild(in birth, childX, childY, wet)))
                return false;
        }

        var staged = npc with { Ai = npc.Ai with { Ai1 = selection.Item } };
        var mechanical = new VanillaSlimeGroundNpcBehaviorStrategy(speculativeRandom);
        if (!mechanical.TryStepMechanical(in staged, in definition, context, inner,
                out var update, containedInitializationObserved: true) || !RuntimeNpcStore.IsValid(in update))
            return false;

        var plan = new ContainedPlan(npc, update, preview, context, candidates, players.ToArray(),
            facts, context.DayTime, world, trap);
        if (!ContainedInputsCurrent(plan) || !preview.IsBeforeCurrent())
            return false;
        containedPlan = plan;
        next = ContainedState(in npc);
        return true;
    }

    internal int PlanContainedTrap(in NpcSnapshot source, Span<NpcAiProjectileIntent> destination)
    {
        if (containedPlan is not { } plan || plan.Before != source || plan.Trap is not { } trap)
            return 0;
        if (!destination.IsEmpty)
            destination[0] = trap;
        return 1;
    }

    internal bool TryGetContainedPlan(in NpcSnapshot before, in NpcSnapshot accepted,
        out NpcStateUpdate planned)
    {
        planned = default;
        if (containedPlan is not { } plan || plan.Before != before ||
            !ContainedInputsCurrent(plan) || !plan.Preview.IsCurrent(in accepted))
            return false;
        planned = plan.Update;
        return true;
    }

    internal NpcSnapshot CompleteContainedPlan(in NpcSnapshot before, in NpcSnapshot accepted,
        in NpcStateUpdate final)
    {
        if (containedPlan is not { } plan || plan.Before != before || !ContainedInputsCurrent(plan) ||
            !plan.Preview.TryAdopt(in accepted, in final, out var completed))
        {
            containedPlan = null;
            return default;
        }
        containedPlan = null;
        return completed;
    }

    private bool ContainedInputsCurrent(ContainedPlan plan)
    {
        if (containedFacts is null || !plan.World.IsCurrent ||
            plan.Context.DayTime != plan.DayTime ||
            !plan.Context.Candidates.SequenceEqual(plan.Candidates) ||
            (containedFacts() with
            {
                HasHeartSlime = plan.Context.HasNpcPeerWithAi1(VanillaNpcIds.BlueSlime, 29f)
            }) != plan.Facts)
            return false;
        foreach (var expected in plan.Players)
            if (!plan.Context.TryGetOwnedPlayer(expected.Player.Slot.Value, out var current) || current != expected)
                return false;
        return plan.World.IsCurrent;
    }

    // No selected producer reads LOS or constructor liquid state. Mechanical terrain remains owned
    // by the world-motion wrapper, so ordinary ticks need no producer-only tile-section capture.
    private sealed class NoContainedProducerWorld : IVanillaSlimeContainedWorld1458
    {
        internal static readonly NoContainedProducerWorld Instance = new();
        public bool IsCurrent => true;
        public bool CanHit => false;
        public bool TryReadBirthWet(in NpcSnapshot birth, out bool wet)
        {
            wet = false;
            return false;
        }
    }

    private static bool MatchesContainedBody(VanillaNpcTargetCandidate candidate, in PlayerStateSnapshot player)
    {
        var size = player.HasMount ? VanillaPlayerMountHitbox1458.Resolve(player.MountType) : (20f, 42f);
        return candidate.Width == size.Item1 && candidate.Height == size.Item2 &&
            candidate.CenterX == player.PositionX + size.Item1 * .5f &&
            candidate.CenterY == player.PositionY + size.Item2 * .5f && candidate.Dead == player.IsDead;
    }

    private static NpcStateUpdate ContainedState(in NpcSnapshot npc) => new(npc.Type, npc.NetId,
        npc.PositionX, npc.PositionY, npc.VelocityX, npc.VelocityY, npc.Target, npc.Ai, npc.Simulation);
}
