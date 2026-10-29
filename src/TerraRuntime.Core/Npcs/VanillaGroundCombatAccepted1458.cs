using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

internal interface IVanillaGroundCombatWorld1458 : IVanillaNpcProjectileEnvironment,
    IVanillaNpcProjectileLineEnvironment, IDisposable
{
    bool IsCurrent { get; }
    bool TryStageStatus(IVanillaNpcRandom random, out NpcSnapshot staged);
    bool TryStageProjectiles(ReadOnlySpan<NpcAiProjectileIntent> shots);
    bool TryFinishMotion(in NpcSnapshot before, in NpcStateUpdate ai,
        in VanillaNpcRawPlayer1458 rawTarget, out NpcStateUpdate final);
    bool TryAdoptStatus(in NpcSnapshot accepted);
    void PublishStatus();
}

internal interface IVanillaGroundCombatEnvironment1458
{
    bool TryCapture(in NpcSnapshot source, VanillaNpcBehaviorContext context,
        out IVanillaGroundCombatWorld1458 world);
}

// Reuses the NPC store's generation/RNG transaction. The ordinary fighter strategy and
// application collision owner run once on this preview, before any live entropy is adopted.
internal sealed class VanillaGroundCombatAccepted1458
{
    private RuntimeNpcStore? store;
    private SystemVanillaNpcRandom? random;
    private IVanillaGroundCombatEnvironment1458? environment;
    private Plan? pending;
    private NpcSnapshot completedBefore, completedAfter;
    private bool completedForce, completedInactive;
    private bool completing;

    private sealed record Plan(NpcSnapshot Before, NpcStateUpdate Ai, RuntimeNpcStore.AiSpawnPreview Preview,
        IVanillaGroundCombatWorld1458 World, VanillaNpcBehaviorContext Context,
        VanillaNpcTargetCandidate[] Candidates, NpcRawPlayerSlotSnapshot1458[] Players,
        bool Day, bool Eclipse, bool Expert, bool Good, bool Remix, int? Invasion, double Surface, double Width);

    internal void Configure(RuntimeNpcStore owner, IVanillaGroundCombatEnvironment1458 world, IVanillaNpcRandom source)
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
        VanillaNpcBehaviorContext context, VanillaGroundFighterNpcBehaviorStrategy strategy,
        INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        // Publication observers may mutate stores, but cannot begin another
        // combat plan before this phase's notification and disposal finish.
        if (completing) { next = default; return false; }
        Cancel();
        completedForce = completedInactive = false;
        next = default;
        if (store is null || random is null || environment is null ||
            !VanillaGroundCombatCohort1458.IsSupported(before.TypeIdentity, before.NetIdentity) ||
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
            if (!world.TryStageStatus(detached, out var staged) ||
                !strategy.TryStepGroundCombatPreview(in staged, in definition, context, inner, detached, world,
                    shots, out int count, out var ai) ||
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
                !plan.World.TryFinishMotion(in before, plan.Ai, in target, out var motion) ||
                !VanillaNpcDefinitionCatalog.TryGet(before.TypeIdentity, before.NetIdentity, out var definition) ||
                !definition.TryResolveHitbox(motion.Simulation, out var body)) return default;
            Span<VanillaNpcRawPlayer1458> players = stackalloc VanillaNpcRawPlayer1458[plan.Players.Length];
            int count = 0;
            foreach (var player in plan.Players) if (player.Facts.Active) players[count++] = player.Facts;
            if (!VanillaOrdinarySlimeCheckActive1458.TryStep(motion.PositionX, motion.PositionY, body.Width, body.Height,
                    motion.Simulation.TimeLeft, players[..count], out int timeLeft, out bool inactive)) return default;
            var final = motion with { Simulation = motion.Simulation with { TimeLeft = timeLeft } };
            if (!InputsCurrent(plan) || !plan.Preview.TryAdopt(in accepted, in final,
                    () => InputsCurrent(plan), out var completed)) return default;
            if (!plan.World.TryAdoptStatus(in completed))
                throw new InvalidOperationException("An accepted ground combat phase lost a prepared owner without a callback.");
            completedBefore = before; completedAfter = completed;
            completedForce = plan.Ai.RequiresImmediateSync || motion.RequiresImmediateSync;
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
}
