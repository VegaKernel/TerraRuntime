using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

internal interface IVanillaNpcPhaseWorld1458 : IVanillaNpcProjectileEnvironment,
    IVanillaNpcProjectileLineEnvironment, IVanillaGroundCritterTerrain1458, IVanillaFishEnvironment1458, IDisposable
{
    bool IsCurrent { get; }
    GroundCritterDanger1458 Danger { get; }
    bool ShelterWeather { get; }
    bool TryStageIgnoreSpamLimitUpdate(bool aiAlreadyRequested);
    void FindRestingSpot(int homeX, int homeY, bool dayTime, out int x, out int y);
    bool TryStageStatus(RuntimeNpcStore.AiSpawnPreview preview, IVanillaNpcRandom random, out NpcSnapshot staged);
    bool TryStageTransform(RuntimeNpcStore.AiSpawnPreview preview, in NpcSnapshot working,
        NpcTypeId target, in NpcAiState suppliedTransformAi, out NpcSnapshot transformed);
    bool TryStageProjectiles(ReadOnlySpan<NpcAiProjectileIntent> shots);
    bool TryFinishMotion(RuntimeNpcStore.AiSpawnPreview preview, in NpcSnapshot before, in NpcStateUpdate ai,
        in VanillaNpcRawPlayer1458 rawTarget, out NpcStateUpdate final);
    bool TryPrepareAdoption(in NpcStateUpdate final);
    bool TryAdoptStatus(in NpcSnapshot accepted);
    void PublishStatus();
}

internal interface IVanillaNpcPhaseEnvironment1458
{
    bool TryCapture(in NpcSnapshot source, VanillaNpcBehaviorContext context,
        out IVanillaNpcPhaseWorld1458 world);
}

// Reuses the NPC store's generation/RNG transaction. The ordinary fighter strategy and
// application collision owner run once on this preview, before any live entropy is adopted.
internal sealed partial class VanillaNpcPhaseAccepted1458
{
    internal static bool SupportsIdentity(NpcTypeId type, NpcNetId net) =>
        VanillaGroundCombatCohort1458.IsSupported(type, net) || net.Value == type.Value &&
            (VanillaNpcTransformation1458.IsNewDefinition(type) ||
             type == VanillaNpcIds.Lihzahrd || VanillaNpcTransformation1458.IsRetainedFish(type));

    private RuntimeNpcStore? store;
    private SystemVanillaNpcRandom? random;
    private IVanillaNpcPhaseEnvironment1458? environment;
    private Plan? pending;
    private NpcSnapshot completedBefore, completedAfter;
    private bool completedForce, completedInactive;
    private bool completing;

    private sealed record Plan(NpcSnapshot Before, NpcStateUpdate Ai, RuntimeNpcStore.AiSpawnPreview Preview,
        IVanillaNpcPhaseWorld1458 World, VanillaNpcBehaviorContext Context,
        VanillaNpcTargetCandidate[] Candidates, NpcRawPlayerSlotSnapshot1458[] Players,
        bool Day, bool Eclipse, bool Expert, bool Good, bool Remix, int? Invasion, double Surface, double Width);

    internal void Configure(RuntimeNpcStore owner, IVanillaNpcPhaseEnvironment1458 world, IVanillaNpcRandom source)
    { Cancel(); store = owner; environment = world; random = source as SystemVanillaNpcRandom; }

    internal bool HasPlan(in NpcSnapshot before) => pending?.Before == before;
    internal bool IsConfigured => store is not null && random is not null && environment is not null;
    internal void Cancel()
    {
        var old = pending;
        pending = null;
        old?.World.Dispose();
    }

    internal bool TryStep(in NpcSnapshot before, in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context, VanillaGroundFighterNpcBehaviorStrategy strategy, VanillaFishNpcBehaviorStrategy fish,
        INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        // Publication observers may mutate stores, but cannot begin another
        // combat plan before this phase's notification and disposal finish.
        if (completing) { next = default; return false; }
        Cancel();
        completedForce = completedInactive = false;
        next = default;
        if (store is null || random is null || environment is null ||
            !SupportsIdentity(before.TypeIdentity, before.NetIdentity) ||
            before.Simulation.Life <= 0 || before.Simulation.LifeMax <= 0 ||
            before.Simulation.ShimmerTransparency != 0f || before.Simulation.LiquidContact == NpcLiquidContactKind.Shimmer ||
            !context.HasRawPlayerSlots ||
            !store.TryCreateAiSpawnPreview(in before, random.SourceRandom, out var preview) ||
            !environment.TryCapture(in before, context, out var world))
            return false;

        bool retained = false;
        try
        {
            if (!world.IsCurrent) return false;
            var players = new List<NpcRawPlayerSlotSnapshot1458>();
            if (!CapturePlayer(byte.MaxValue) || !CapturePlayer(0)) return false;
            if (before.Target < byte.MaxValue && !CapturePlayer((byte)before.Target)) return false;
            foreach (var candidate in context.Candidates)
            {
                if (!CapturePlayer(candidate.Slot)) return false;
                var raw = players.Find(player => player.Facts.Slot == candidate.Slot).Facts;
                if (candidate.Active != raw.Active || candidate.Dead != raw.Dead || candidate.Ghost != raw.Ghost ||
                    candidate.NoAggro != raw.NoAggro || candidate.Aggro != raw.Aggro ||
                    candidate.Width != raw.Width || candidate.Height != raw.Height ||
                    candidate.CenterX != raw.PositionX + raw.Width * .5f ||
                    candidate.CenterY != raw.PositionY + raw.Height * .5f) return false;
            }

            var detached = new SystemVanillaNpcRandom(preview!.Random);
            Span<NpcAiProjectileIntent> shots = stackalloc NpcAiProjectileIntent[1];
            if (!world.TryStageStatus(preview, detached, out var staged) ||
                !VanillaNpcDefinitionCatalog.TryGet(staged.TypeIdentity, staged.NetIdentity, out var workingDefinition))
                return false;
            NpcStateUpdate ai;
            int count = 0;
            int style = staged.Simulation.AiStyleOverride?.Value ?? workingDefinition.AiStyle.Value;
            bool stepped = style switch
            {
                3 => strategy.TryStepGroundCombatPreview(in staged, in workingDefinition, context, inner, detached,
                    world, preview, shots, out count, out ai),
                7 => TryStepGroundCritter(in staged, in workingDefinition, context, world, detached, out ai),
                16 => fish.TryStepPreview(in staged, in workingDefinition, context, detached, world, out ai),
                _ => Refuse(out ai)
            };
            if (!stepped || !TryFinishCatchProtection(in ai, out ai) ||
                !RuntimeNpcStore.IsValid(in ai) || !world.TryStageProjectiles(shots[..count])) return false;
            var plan = new Plan(before, ai, preview, world, context, context.Candidates.ToArray(), players.ToArray(),
                context.DayTime, context.EclipseActive, context.ExpertMode, context.GoodWorld, context.RemixWorld,
                context.InvasionType, context.WorldSurfacePixels, context.WorldWidthPixels);
            if (!InputsCurrent(plan) || !preview.IsBeforeCurrent()) return false;
            pending = plan;
            retained = true;
            next = State(in before) with { VanillaNetworkPhase = true, RequiresLowPrioritySync = before.Simulation.JustHit };
            return true;

            bool CapturePlayer(byte slot)
            {
                foreach (var saved in players) if (saved.Facts.Slot == slot) return true;
                if (!context.TryCaptureRawPlayer(slot, out var player) || !context.IsRawPlayerCurrent(in player)) return false;
                players.Add(player);
                return true;
            }
        }
        finally { if (!retained) world.Dispose(); }
    }

    internal NpcSnapshot Complete(in NpcSnapshot before, in NpcSnapshot accepted)
    {
        if (completing) return default;
        if (pending is not { } plan || plan.Before != before) { Cancel(); return default; }
        pending = null;
        completing = true;
        try
        {
            VanillaNpcRawPlayer1458 target = default;
            foreach (var player in plan.Players)
                if (player.Facts.Slot == plan.Ai.Target) { target = player.Facts; break; }
            if (!InputsCurrent(plan) || !target.IsValid || !plan.Preview.IsCurrent(in accepted) ||
                !plan.World.TryFinishMotion(plan.Preview, in before, plan.Ai, in target, out var motion) ||
                !VanillaNpcDefinitionCatalog.TryGet(new(motion.Type), new(motion.NetId), out var definition) ||
                !definition.TryResolveHitbox(motion.Simulation, out var body)) return default;
            Span<VanillaNpcRawPlayer1458> players = stackalloc VanillaNpcRawPlayer1458[plan.Players.Length];
            int count = 0;
            foreach (var player in plan.Players) if (player.Facts.Active) players[count++] = player.Facts;
            if (!VanillaOrdinarySlimeCheckActive1458.TryStep(motion.PositionX, motion.PositionY, body.Width, body.Height,
                    motion.Simulation.TimeLeft, players[..count], out int timeLeft, out bool inactive)) return default;
            var final = motion with { Simulation = motion.Simulation with { TimeLeft = timeLeft } };
            if (!plan.World.TryPrepareAdoption(in final) || !InputsCurrent(plan) || !plan.Preview.TryAdopt(in accepted, in final,
                    () => InputsCurrent(plan), out var completed)) return default;
            if (!plan.World.TryAdoptStatus(in completed))
                throw new InvalidOperationException("An accepted ground combat phase lost a prepared owner without a callback.");
            completedBefore = before; completedAfter = completed;
            // Motion includes the collision-time Transform boundary, whose immediate23
            // can consume requests made earlier in AI. Its resulting offer is authoritative.
            completedForce = motion.RequiresImmediateSync;
            completedInactive = inactive;
            plan.World.PublishStatus();
            return completed;
        }
        finally
        {
            if (ReferenceEquals(pending, plan)) pending = null;
            completing = false;
            plan.World.Dispose();
        }
    }

    internal bool RequiresForcedUpdate(in NpcSnapshot before, in NpcSnapshot after) =>
        completedBefore == before && completedAfter == after && completedForce;
    internal bool Deactivates(in NpcSnapshot before, in NpcSnapshot after) =>
        completedBefore == before && completedAfter == after && completedInactive;

    private static bool InputsCurrent(Plan plan)
    {
        if (!plan.World.IsCurrent || plan.Context.DayTime != plan.Day || plan.Context.EclipseActive != plan.Eclipse ||
            plan.Context.ExpertMode != plan.Expert || plan.Context.GoodWorld != plan.Good ||
            plan.Context.RemixWorld != plan.Remix || plan.Context.InvasionType != plan.Invasion ||
            plan.Context.WorldSurfacePixels != plan.Surface ||
            plan.Context.WorldWidthPixels != plan.Width ||
            !plan.Context.Candidates.SequenceEqual(plan.Candidates)) return false;
        foreach (var player in plan.Players) if (!plan.Context.IsRawPlayerCurrent(in player)) return false;
        return plan.World.IsCurrent;
    }

    private static NpcStateUpdate State(in NpcSnapshot source) => new(source.Type, source.NetId,
        source.PositionX, source.PositionY, source.VelocityX, source.VelocityY, source.Target, source.Ai, source.Simulation);

    private static bool Refuse(out NpcStateUpdate state) { state = default; return false; }

    private static bool TryFinishCatchProtection(in NpcStateUpdate ai, out NpcStateUpdate next)
    {
        next = ai;
        if (!VanillaNpcTransformation1458.HasPhaseCatchProtection(new(ai.Type))) return true;
        if (ai.Simulation.CatchableNpcTempImmunityCounter is not (>= 0 and <= 90) ||
            ai.Simulation.ReleaseOwner is not (>= 0 and <= 255)) return false;
        int counter = ai.Simulation.CatchableNpcTempImmunityCounter.Value;
        if (ai.Simulation.ReleaseOwner != 255 || ai.Simulation.SpawnedFromStatue) counter = 0;
        bool friendly = counter > 0;
        if (counter > 0) counter--;
        next = ai with { Simulation = ai.Simulation with { Friendly = friendly, CatchableNpcTempImmunityCounter = counter },
            RequiresImmediateSync = ai.RequiresImmediateSync || friendly != ai.Simulation.Friendly };
        return true;
    }
}
