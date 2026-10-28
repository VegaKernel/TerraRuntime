using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

internal interface IVanillaOrdinaryMeleeWorld1458
{
    bool IsCurrent { get; }
    bool TryStageStatus(IVanillaNpcRandom random, out NpcSnapshot staged);
    bool TryFinishMotion(in NpcSnapshot before, in NpcStateUpdate ai,
        in VanillaNpcRawPlayer1458 rawTarget, out NpcStateUpdate final);
    bool TryAdoptStatus(in NpcSnapshot accepted);
    void PublishStatus();
}

internal interface IVanillaOrdinaryMeleeEnvironment1458
{
    bool TryCapture(in NpcSnapshot source, VanillaNpcBehaviorContext context,
        out IVanillaOrdinaryMeleeWorld1458 world);
}

// Reuses the NPC store's generation/RNG transaction. The ordinary fighter strategy and
// application collision owner run once on this preview, before any live entropy is adopted.
internal sealed class VanillaOrdinaryMeleeAccepted1458
{
    private RuntimeNpcStore? store;
    private SystemVanillaNpcRandom? random;
    private IVanillaOrdinaryMeleeEnvironment1458? environment;
    private Plan? pending;
    private NpcSnapshot completedBefore, completedAfter;
    private bool completedForce, completedInactive;

    private sealed record Plan(NpcSnapshot Before, NpcStateUpdate Ai, RuntimeNpcStore.AiSpawnPreview Preview,
        IVanillaOrdinaryMeleeWorld1458 World, VanillaNpcBehaviorContext Context,
        VanillaNpcTargetCandidate[] Candidates, NpcRawPlayerSlotSnapshot1458[] Players,
        bool Day, bool Eclipse, bool Expert, bool Good, bool Remix, int? Invasion, double Surface);

    internal void Configure(RuntimeNpcStore owner, IVanillaOrdinaryMeleeEnvironment1458 world, IVanillaNpcRandom source)
    { store = owner; environment = world; random = source as SystemVanillaNpcRandom; }

    internal bool HasPlan(in NpcSnapshot before) => pending?.Before == before;
    internal void Cancel() => pending = null;

    internal bool TryStep(in NpcSnapshot before, in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context, VanillaGroundFighterNpcBehaviorStrategy strategy,
        INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        pending = null;
        completedForce = completedInactive = false;
        next = default;
        if (store is null || random is null || environment is null || before.Type != before.NetId ||
            !VanillaGroundFighterNpcCatalog.IsOrdinaryMeleeCohort1458(before.TypeIdentity) ||
            before.Simulation.Life <= 0 || before.Simulation.LifeMax <= 0 ||
            before.Simulation.ShimmerTransparency != 0f || before.Simulation.LiquidContact == NpcLiquidContactKind.Shimmer ||
            !context.HasRawPlayerSlots ||
            !store.TryCreateAiSpawnPreview(in before, random.SourceRandom, out var preview) ||
            !environment.TryCapture(in before, context, out var world) || !world.IsCurrent)
            return false;

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
        if (!world.TryStageStatus(detached, out var staged) ||
            !strategy.TryStepOrdinaryMeleePreview(in staged, in definition, context, inner, detached, out var ai) ||
            !RuntimeNpcStore.IsValid(in ai)) return false;
        var plan = new Plan(before, ai, preview, world, context, context.Candidates.ToArray(), players.ToArray(),
            context.DayTime, context.EclipseActive, context.ExpertMode, context.GoodWorld, context.RemixWorld,
            context.InvasionType, context.WorldSurfacePixels);
        if (!InputsCurrent(plan) || !preview.IsBeforeCurrent()) return false;
        pending = plan;
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

    internal NpcSnapshot Complete(in NpcSnapshot before, in NpcSnapshot accepted)
    {
        VanillaNpcRawPlayer1458 target = default;
        if (pending is { } captured)
            foreach (var player in captured.Players)
                if (player.Facts.Slot == captured.Ai.Target) { target = player.Facts; break; }
        if (pending is not { } plan || plan.Before != before || !InputsCurrent(plan) ||
            !target.IsValid || !plan.Preview.IsCurrent(in accepted) ||
            !plan.World.TryFinishMotion(in before, plan.Ai, in target, out var motion) ||
            !VanillaNpcDefinitionCatalog.TryGet(before.TypeIdentity, before.NetIdentity, out var definition) ||
            !definition.TryResolveHitbox(motion.Simulation, out var body))
        { pending = null; return default; }
        Span<VanillaNpcRawPlayer1458> players = stackalloc VanillaNpcRawPlayer1458[plan.Players.Length];
        int count = 0;
        foreach (var player in plan.Players) if (player.Facts.Active) players[count++] = player.Facts;
        if (!VanillaOrdinarySlimeCheckActive1458.TryStep(motion.PositionX, motion.PositionY, body.Width, body.Height,
                motion.Simulation.TimeLeft, players[..count], out int timeLeft, out bool inactive))
        { pending = null; return default; }
        var final = motion with { Simulation = motion.Simulation with { TimeLeft = timeLeft } };
        if (!InputsCurrent(plan) || !plan.Preview.TryAdopt(in accepted, in final, out var completed))
        { pending = null; return default; }
        if (!plan.World.TryAdoptStatus(in completed))
            throw new InvalidOperationException("An accepted melee phase lost its status owner without a callback.");
        pending = null;
        completedBefore = before; completedAfter = completed;
        completedForce = plan.Ai.RequiresImmediateSync || motion.RequiresImmediateSync;
        completedInactive = inactive;
        plan.World.PublishStatus();
        return completed;
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
            !plan.Context.Candidates.SequenceEqual(plan.Candidates)) return false;
        foreach (var player in plan.Players) if (!plan.Context.IsRawPlayerCurrent(in player)) return false;
        return plan.World.IsCurrent;
    }

    private static NpcStateUpdate State(in NpcSnapshot source) => new(source.Type, source.NetId,
        source.PositionX, source.PositionY, source.VelocityX, source.VelocityY, source.Target, source.Ai, source.Simulation);
}
