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
    private NpcSnapshot completedContainedBefore;
    private NpcSnapshot completedContainedAfter;
    private bool completedContainedInactive;

    private sealed record ContainedPlan(NpcSnapshot Before, NpcStateUpdate Update,
        RuntimeNpcStore.AiSpawnPreview Preview, VanillaNpcBehaviorContext Context,
        VanillaNpcTargetCandidate[] Candidates, PlayerStateSnapshot[] Players,
        VanillaSlimeContainedFacts1458 Facts, bool DayTime, IVanillaSlimeContainedWorld1458 World,
        NpcAiProjectileIntent? Trap, NpcRawPlayerSlotSnapshot1458? RawTarget,
        NpcRawPlayerSlotSnapshot1458? RawTracking);

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
        completedContainedInactive = false;
        next = default;
        if (random is not SystemVanillaNpcRandom trusted || containedStore is null ||
            containedFacts is null || containedEnvironment is null ||
            !definition.TryResolveHitbox(npc.Simulation, out var body) ||
            npc.Simulation.MoneyValue is not float money || npc.Ai.Ai1 != (int)npc.Ai.Ai1)
            return false;

        NpcRawPlayerSlotSnapshot1458? rawTarget = null;
        NpcRawPlayerSlotSnapshot1458? rawTracking = null;
        if (context.HasRawPlayerSlots)
        {
            if (npc.Target > byte.MaxValue || !context.TryCaptureRawPlayer((byte)npc.Target, out var captured))
                return false;
            rawTarget = captured;
            if (npc.Target == byte.MaxValue)
            {
                if (!context.TryCaptureRawPlayer(0, out var fallback))
                    return false;
                rawTracking = fallback;
            }
        }
        if (!context.TrySelectSlimeClosestTarget(in npc, in definition, out _))
            return false;

        // NewNPC uses target 255. AI001 initializes contents before its first TargetClosest;
        // source producer gates read the permanently inactive server sentinel at this stage.
        // Main's player array has a constructor-only server sentinel at 255. Neither its
        // player update loop nor authenticated player allocation includes that slot. Hive reads
        // this body's LOS even while inactive; the dart separately requires an active player.
        VanillaNpcTargetCandidate target = new(byte.MaxValue,
            VanillaPlayerHitboxFacts.BaseWidth * .5f, VanillaPlayerHitboxFacts.BaseHeight * .5f,
            0, false, false, false, false);
        bool producerTargetEligible = false;
        if (rawTarget is { } ownedRaw)
        {
            target = ownedRaw.Facts.Candidate;
            if (target.Ghost || target.NoAggro || target.Aggro < 0)
                return false;
            producerTargetEligible = target.Active && !target.Dead;
        }
        else if (npc.Target < byte.MaxValue)
        {
            if (!context.TryFindCandidate((byte)npc.Target, out target) ||
                target.Ghost || target.NoAggro || target.Aggro < 0)
                return false;
            producerTargetEligible = target.Active && !target.Dead;
        }
        else if (npc.Target != byte.MaxValue)
        {
            return false;
        }

        var candidates = context.Candidates.ToArray();
        var players = new List<PlayerStateSnapshot>(candidates.Length);
        foreach (var candidate in candidates)
        {
            if (!candidate.Active)
                continue;
            if (candidate.Ghost || candidate.NoAggro || candidate.Aggro < 0 ||
                !context.TryGetOwnedPlayer(candidate.Slot, out var player) || !MatchesContainedBody(candidate, in player))
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
        if (((producerTargetEligible && effects.Trap) || effects.HiveType != 0) &&
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
            bool childHasLivingTarget = false;
            foreach (var candidate in candidates)
                childHasLivingTarget |= candidate.Active && !candidate.Dead && !candidate.Ghost;
            if (context.HasRawPlayerSlots && !childHasLivingTarget && context.BeeChildAdmission is null)
                return false;
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
            // A higher physical slot is updated later in this same source pass. Its typed
            // AI005 target search must already have an owned peer/player boundary.
            if (allocated && context.BeeChildAdmission is { } admit && !admit(birth))
                return false;
        }

        var staged = npc with { Ai = npc.Ai with { Ai1 = selection.Item } };
        var mechanical = new VanillaSlimeGroundNpcBehaviorStrategy(speculativeRandom);
        if (!mechanical.TryStepMechanical(in staged, in definition, context, inner,
                out var update, containedInitializationObserved: true) || !RuntimeNpcStore.IsValid(in update))
            return false;

        var plan = new ContainedPlan(npc, update, preview, context, candidates, players.ToArray(),
            facts, context.DayTime, world, trap, rawTarget, rawTracking);
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
        if (containedPlan is not { } plan || plan.Before != before || !ContainedInputsCurrent(plan))
        {
            containedPlan = null;
            return default;
        }
        var completedState = final;
        bool despawnAfterCompletion = false;
        if (plan.RawTarget is not null)
        {
            if (!VanillaNpcDefinitionCatalog.TryGet(before.TypeIdentity, before.NetIdentity, out var definition) ||
                !definition.TryResolveHitbox(final.Simulation, out var body))
            {
                containedPlan = null;
                return default;
            }
            Span<VanillaNpcRawPlayer1458> players = stackalloc VanillaNpcRawPlayer1458[plan.Players.Length];
            for (int index = 0; index < plan.Players.Length; index++)
            {
                var player = plan.Players[index];
                var size = player.HasMount ? VanillaPlayerMountHitbox1458.Resolve(player.MountType) : (20f, 42f);
                players[index] = new(player.Player.Slot.Value, true, player.IsDead,
                    (player.MovementFlags & VanillaPlayerHealthContext1458.GhostMovementFlag) != 0,
                    player.PositionX, player.PositionY, (int)size.Item1, (int)size.Item2,
                    0, false, player.ItemAnimation ?? 0);
            }
            if (!VanillaOrdinarySlimeCheckActive1458.TryStep(final.PositionX, final.PositionY,
                body.Width, body.Height, final.Simulation.TimeLeft, players, out int lifetime, out bool despawn))
            {
                containedPlan = null;
                return default;
            }
            completedState = final with
            {
                Simulation = final.Simulation with { TimeLeft = lifetime }
            };
            despawnAfterCompletion = despawn;
        }
        if (!ContainedInputsCurrent(plan) || !plan.Preview.TryAdopt(in accepted, in completedState, out var completed))
        {
            containedPlan = null;
            return default;
        }
        containedPlan = null;
        completedContainedBefore = before;
        completedContainedAfter = completed;
        completedContainedInactive = despawnAfterCompletion;
        return completed;
    }

    internal bool DeactivatesContainedAfterCompletion(in NpcSnapshot before, in NpcSnapshot completed) =>
        completedContainedInactive && completedContainedBefore == before && completedContainedAfter == completed;

    private bool ContainedInputsCurrent(ContainedPlan plan)
    {
        if (containedFacts is null || !plan.World.IsCurrent ||
            (plan.RawTarget is { } raw && !plan.Context.IsRawPlayerCurrent(in raw)) ||
            (plan.RawTracking is { } tracking && !plan.Context.IsRawPlayerCurrent(in tracking)) ||
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
