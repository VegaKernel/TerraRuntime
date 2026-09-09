namespace TerraRuntime.Application.Bots;

internal abstract class RuntimeBotAction : IRuntimeBotAction
{
    public abstract RuntimeBotActionKind Kind { get; }
    public virtual RuntimeBotActionLimits Limits => default;
    public virtual void Enter(RuntimeBotActionContext context) { }
    public abstract RuntimeBotActionResult Tick(RuntimeBotActionContext context);
    public virtual void Cancel(RuntimeBotActionContext context, RuntimeBotActionCancelReason reason) => context.Navigation.Stop();
    public virtual void Exit(RuntimeBotActionContext context, RuntimeBotActionResult result) => context.Navigation.Stop();

    public static IRuntimeBotAction Create(RuntimeBotActionKind kind) => kind switch
    {
        RuntimeBotActionKind.Idle => new RuntimeBotIdleAction(),
        RuntimeBotActionKind.Follow => new RuntimeBotFollowAction(),
        RuntimeBotActionKind.Guard => new RuntimeBotGuardAction(),
        RuntimeBotActionKind.Attack => new RuntimeBotAttackAction(),
        RuntimeBotActionKind.PickupUsefulItem => new RuntimeBotPickupUsefulItemAction(),
        RuntimeBotActionKind.UseConsumable => new RuntimeBotUseConsumableAction(),
        RuntimeBotActionKind.RecoverToTarget => new RuntimeBotRecoverToTargetAction(),
        RuntimeBotActionKind.Mining => new RuntimeBotMiningAction(),
        RuntimeBotActionKind.Collect => new RuntimeBotCollectAction(),
        RuntimeBotActionKind.ReturnToPlayer => new RuntimeBotReturnToPlayerAction(),
        _ => new RuntimeBotUnsupportedAction()
    };
}

internal sealed class RuntimeBotIdleAction : RuntimeBotAction
{
    public override RuntimeBotActionKind Kind => RuntimeBotActionKind.Idle;
    public override RuntimeBotActionResult Tick(RuntimeBotActionContext context)
    {
        context.Navigation.Stop();
        return RuntimeBotActionResult.Pending();
    }
}
internal sealed class RuntimeBotFollowAction : RuntimeBotAction
{
    public override RuntimeBotActionKind Kind => RuntimeBotActionKind.Follow;
    public override RuntimeBotActionResult Tick(RuntimeBotActionContext context)
    {
        _ = context.WorldInteraction.AssistMining(context.Observation);
        return context.Navigation.Follow(context.Observation);
    }
}
internal sealed class RuntimeBotGuardAction : RuntimeBotAction
{
    public override RuntimeBotActionKind Kind => RuntimeBotActionKind.Guard;
    public override RuntimeBotActionResult Tick(RuntimeBotActionContext context)
    {
        var observation = context.Observation;
        if (observation.GuardTarget is null)
        {
            _ = context.WorldInteraction.AssistMining(observation);
            _ = context.Navigation.Follow(observation);
        }
        else _ = context.Navigation.Engage(observation);
        context.Inventory.UseCombatBuffs(observation);
        if (observation.GuardTarget is not null) _ = context.Combat.Attack(observation);
        return RuntimeBotActionResult.Pending();
    }
}
internal sealed class RuntimeBotAttackAction : RuntimeBotAction
{
    public override RuntimeBotActionKind Kind => RuntimeBotActionKind.Attack;
    public override RuntimeBotActionLimits Limits => BotPolicy.AttackActionLimits;
    public override RuntimeBotActionResult Tick(RuntimeBotActionContext context) => context.Combat.Attack(context.Observation);
}
internal sealed class RuntimeBotPickupUsefulItemAction : RuntimeBotAction
{
    public override RuntimeBotActionKind Kind => RuntimeBotActionKind.PickupUsefulItem;
    public override RuntimeBotActionResult Tick(RuntimeBotActionContext context) => context.Observation.UsefulItem is { } item
        ? context.Inventory.Pickup(context.Observation, item.Handle)
        : RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.ItemUnavailable);
}
internal sealed class RuntimeBotUseConsumableAction : RuntimeBotAction
{
    public override RuntimeBotActionKind Kind => RuntimeBotActionKind.UseConsumable;
    public override RuntimeBotActionResult Tick(RuntimeBotActionContext context) => context.Inventory.UseConsumables(context.Observation);
}
internal sealed class RuntimeBotRecoverToTargetAction : RuntimeBotAction
{
    public override RuntimeBotActionKind Kind => RuntimeBotActionKind.RecoverToTarget;
    // Mirror is source 90 ticks; this is an execution watchdog, not a changed use time.
    public override RuntimeBotActionLimits Limits => BotPolicy.RecoveryActionLimits;
    public override RuntimeBotActionResult Tick(RuntimeBotActionContext context) => context.Navigation.Recover(context.Observation);
    public override void Cancel(RuntimeBotActionContext context, RuntimeBotActionCancelReason reason)
    {
        context.Navigation.CancelRecovery();
        base.Cancel(context, reason);
    }
}
internal sealed class RuntimeBotMiningAction : RuntimeBotAction
{
    public override RuntimeBotActionKind Kind => RuntimeBotActionKind.Mining;
    public override RuntimeBotActionLimits Limits => BotPolicy.MiningActionLimits;
    public override RuntimeBotActionResult Tick(RuntimeBotActionContext context) => context.WorldInteraction.Mine(context.Observation);
    public override void Exit(RuntimeBotActionContext context, RuntimeBotActionResult result)
    {
        context.WorldInteraction.FinishMining(result.Status != RuntimeBotActionStatus.Success);
        base.Exit(context, result);
    }
    public override void Cancel(RuntimeBotActionContext context, RuntimeBotActionCancelReason reason)
    {
        context.WorldInteraction.FinishMining(true);
        base.Cancel(context, reason);
    }
}
internal sealed class RuntimeBotCollectAction : RuntimeBotAction
{
    private TerraRuntime.Contracts.Runtime.WorldItemHandle item;
    public override RuntimeBotActionKind Kind => RuntimeBotActionKind.Collect;
    public override RuntimeBotActionLimits Limits => BotPolicy.CollectionActionLimits;
    public override void Enter(RuntimeBotActionContext context) => item = context.Observation.UsefulItem?.Handle ?? default;
    public override RuntimeBotActionResult Tick(RuntimeBotActionContext context)
    {
        if (item.IsAssigned && item == context.Observation.LastPickedItem) return RuntimeBotActionResult.Success;
        if (item.IsAssigned && item != context.Observation.UsefulItem?.Handle)
            return RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.TargetChanged);
        return context.Navigation.Collect(context.Observation);
    }
}
internal sealed class RuntimeBotReturnToPlayerAction : RuntimeBotAction
{
    public override RuntimeBotActionKind Kind => RuntimeBotActionKind.ReturnToPlayer;
    public override RuntimeBotActionResult Tick(RuntimeBotActionContext context) => context.Navigation.Return(context.Observation);
}
internal sealed class RuntimeBotUnsupportedAction : RuntimeBotAction
{
    public override RuntimeBotActionKind Kind => (RuntimeBotActionKind)byte.MaxValue;
    public override RuntimeBotActionResult Tick(RuntimeBotActionContext context) => RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.UnsupportedAction);
}
