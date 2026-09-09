namespace TerraRuntime.Application.Bots;

/// <summary>Owns execution, never goal selection. Observation revisions advance; bound entity/goal/world generations do not.</summary>
internal sealed class RuntimeBotActionExecutor(RuntimeBotResourceLeases leases)
{
    private IRuntimeBotAction? action;
    private RuntimeBotDecisionEnvelope binding;
    private RuntimeBotActionContext? ownedContext;
    private ulong lastObservationRevision;
    private long startedAt, lastTick, lastProgressAt;
    private float? bestProgress;
    public RuntimeBotActionKind? Current => action?.Kind;
    public RuntimeBotActionResult? RecentResult { get; private set; }

    public RuntimeBotActionResult Start(IRuntimeBotAction next, in RuntimeBotDecisionEnvelope envelope,
        RuntimeBotActionContext context)
    {
        var failure = envelope.Validate(context.Observation);
        if (failure != RuntimeBotActionFailureCode.None) return RuntimeBotActionResult.Failure(failure);
        if (binding.BotId != 0 && (binding.BotId != envelope.BotId || binding.BotGeneration != envelope.BotGeneration ||
            binding.World != envelope.World))
            return RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.StaleDecision);
        if (next.Kind != envelope.Decision.Intent.Kind || !next.Limits.IsValid)
            return RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.UnsupportedAction);
        Cancel(context, RuntimeBotActionCancelReason.Replaced);
        binding = envelope;
        ownedContext = context;
        lastObservationRevision = envelope.ObservationRevision - 1;
        action = next;
        startedAt = lastProgressAt = context.Observation.Tick;
        lastTick = startedAt - 1;
        bestProgress = null;
        try { action.Enter(context); }
        catch
        {
            Finish(RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.InternalInvariantViolation));
            throw;
        }
        return RuntimeBotActionResult.Pending();
    }

    public RuntimeBotActionResult Tick(RuntimeBotActionContext context)
    {
        if (action is null) return RecentResult ?? RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.UnsupportedAction);
        var observation = context.Observation;
        if (observation.World != binding.World)
            return Fail(context, RuntimeBotActionCancelReason.WorldChanged, RuntimeBotActionFailureCode.WorldChanged);
        if (observation.BotId != binding.BotId || observation.Self.Player != binding.BotGeneration ||
            observation.GoalGeneration != binding.GoalGeneration || observation.ObservationRevision <= lastObservationRevision)
            return Fail(context, RuntimeBotActionCancelReason.Reconfigured, RuntimeBotActionFailureCode.StaleDecision);
        if (binding.Decision.Intent.Target.IsAssigned &&
            (observation.TargetPlayer?.Player != binding.Decision.Intent.Target ||
             observation.Configuration.Target.Player != binding.Decision.Intent.Target))
            return Fail(context, RuntimeBotActionCancelReason.TargetChanged, RuntimeBotActionFailureCode.TargetChanged);
        if (observation.Self.IsDead || !observation.Self.Player.IsAssigned)
            return Fail(context, RuntimeBotActionCancelReason.BotUnavailable, RuntimeBotActionFailureCode.TargetDead);
        if (binding.Decision.Intent.Npc.IsAssigned && observation.GuardTarget?.Npc != binding.Decision.Intent.Npc ||
            binding.Decision.Intent.Item.IsAssigned && observation.UsefulItem?.Handle != binding.Decision.Intent.Item)
            return Fail(context, RuntimeBotActionCancelReason.TargetChanged, RuntimeBotActionFailureCode.TargetChanged);
        if (observation.Tick <= lastTick)
            return Fail(context, RuntimeBotActionCancelReason.Requested, RuntimeBotActionFailureCode.StaleDecision);
        lastTick = observation.Tick;
        lastObservationRevision = observation.ObservationRevision;
        ownedContext = context;
        if (action.Limits.TimeoutTicks > 0 && observation.Tick - startedAt >= action.Limits.TimeoutTicks)
            return Fail(context, RuntimeBotActionCancelReason.TimedOut, RuntimeBotActionFailureCode.TimedOut);
        RuntimeBotActionResult result;
        try { result = action.Tick(context); }
        catch
        {
            Finish(RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.InternalInvariantViolation));
            throw;
        }
        if (!result.IsValid)
            return Fail(context, RuntimeBotActionCancelReason.Requested, RuntimeBotActionFailureCode.InternalInvariantViolation);
        if (result.Status != RuntimeBotActionStatus.Pending)
        {
            try { action.Exit(context, result); }
            catch
            {
                Finish(RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.InternalInvariantViolation));
                throw;
            }
            return Finish(result);
        }
        // The action supplies a monotonically improving semantic metric, not necessarily a position/distance.
        if (result.Progress is float progress && (bestProgress is null || progress > bestProgress))
        {
            bestProgress = progress;
            lastProgressAt = observation.Tick;
        }
        if (action.Limits.StagnationTicks > 0 && observation.Tick - lastProgressAt >= action.Limits.StagnationTicks)
            return Fail(context, RuntimeBotActionCancelReason.Stuck, RuntimeBotActionFailureCode.Stuck);
        return result;
    }

    public void Cancel(RuntimeBotActionContext context, RuntimeBotActionCancelReason reason)
    {
        if (action is null) return;
        try { action.Cancel(CleanupContext(context), reason); }
        finally { Finish(new(RuntimeBotActionStatus.Cancelled, RuntimeBotActionFailureCode.Cancelled)); }
    }

    private RuntimeBotActionResult Fail(RuntimeBotActionContext context, RuntimeBotActionCancelReason reason,
        RuntimeBotActionFailureCode code)
    {
        var result = RuntimeBotActionResult.Failure(code);
        try { action!.Cancel(CleanupContext(context), reason); }
        finally { Finish(result); }
        return result;
    }

    private RuntimeBotActionResult Finish(RuntimeBotActionResult result)
    {
        leases.ReleaseBot(new(binding.BotId, binding.BotGeneration, binding.World));
        action = null;
        ownedContext = null;
        RecentResult = result;
        return result;
    }

    private RuntimeBotActionContext CleanupContext(RuntimeBotActionContext context) =>
        context.Observation.BotId == binding.BotId && context.Observation.Self.Player == binding.BotGeneration &&
        context.Observation.World == binding.World ? context : ownedContext!;
}
