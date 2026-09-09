using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application.Bots;

internal enum RuntimeBotActionKind : byte
{
    Idle, Follow, Guard, Attack, PickupUsefulItem, UseConsumable, RecoverToTarget, Mining, Collect, ReturnToPlayer
}

internal enum RuntimeBotActionStatus : byte { Pending, Success, Failure, Cancelled }
internal enum RuntimeBotActionFailureCode : byte
{
    None, TargetUnavailable, TargetDead, TargetChanged, WorldChanged, StaleDecision,
    UnsupportedAction, PermissionDenied, ItemUnavailable, InventoryFull, Stuck, TimedOut,
    Cancelled, InternalInvariantViolation
}
internal enum RuntimeBotActionCancelReason : byte
{
    Requested, Replaced, Reconfigured, Despawned, TargetChanged, WorldChanged, TimedOut, Stuck, BotUnavailable
}

internal readonly record struct RuntimeBotActionResult(
    RuntimeBotActionStatus Status,
    RuntimeBotActionFailureCode FailureCode = RuntimeBotActionFailureCode.None,
    float? Progress = null)
{
    public static RuntimeBotActionResult Pending(float? progress = null) => new(RuntimeBotActionStatus.Pending, Progress: progress);
    public static RuntimeBotActionResult Success => new(RuntimeBotActionStatus.Success);
    public static RuntimeBotActionResult Failure(RuntimeBotActionFailureCode code) => new(RuntimeBotActionStatus.Failure, code);
    public bool IsValid => Enum.IsDefined(Status) && Enum.IsDefined(FailureCode) &&
        (Progress is null || float.IsFinite(Progress.Value)) &&
        ((Status is RuntimeBotActionStatus.Pending or RuntimeBotActionStatus.Success)
            ? FailureCode == RuntimeBotActionFailureCode.None : FailureCode != RuntimeBotActionFailureCode.None);
}

// Scheduling policy, not Terraria item-use timing. Zero disables the corresponding deadline for continuous goals.
internal readonly record struct RuntimeBotActionLimits(long TimeoutTicks, long StagnationTicks)
{
    public bool IsValid => TimeoutTicks >= 0 && StagnationTicks >= 0;
}

internal interface IRuntimeBotAction
{
    RuntimeBotActionKind Kind { get; }
    RuntimeBotActionLimits Limits { get; }
    void Enter(RuntimeBotActionContext context);
    RuntimeBotActionResult Tick(RuntimeBotActionContext context);
    void Cancel(RuntimeBotActionContext context, RuntimeBotActionCancelReason reason);
    void Exit(RuntimeBotActionContext context, RuntimeBotActionResult result) { }
}

internal readonly record struct RuntimeBotIntent(RuntimeBotActionKind Kind, PlayerHandle Target = default,
    NpcHandle Npc = default, WorldItemHandle Item = default);
internal readonly record struct RuntimeBotDecision(RuntimeBotIntent Intent);
internal readonly record struct RuntimeBotBrainState(RuntimeBotActionKind? CurrentAction, RuntimeBotActionResult? RecentResult);
internal readonly record struct RuntimeBotDecisionEnvelope(
    int BotId, PlayerHandle BotGeneration, WorldRuntimeIdentity World,
    ulong ObservationRevision, ulong GoalGeneration, RuntimeBotDecision Decision)
{
    public RuntimeBotActionFailureCode Validate(in RuntimeBotObservationSnapshot observation)
    {
        if (!World.IsAssigned || World != observation.World) return RuntimeBotActionFailureCode.WorldChanged;
        if (BotId <= 0 || BotId != observation.BotId || !BotGeneration.IsAssigned || BotGeneration != observation.Self.Player ||
            ObservationRevision == 0 || ObservationRevision != observation.ObservationRevision ||
            GoalGeneration == 0 || GoalGeneration != observation.GoalGeneration)
            return RuntimeBotActionFailureCode.StaleDecision;
        if (!Enum.IsDefined(Decision.Intent.Kind)) return RuntimeBotActionFailureCode.UnsupportedAction;
        if (Decision.Intent.Target.IsAssigned && Decision.Intent.Target != observation.Configuration.Target.Player)
            return RuntimeBotActionFailureCode.TargetChanged;
        if (Decision.Intent.Npc.IsAssigned && Decision.Intent.Npc != observation.GuardTarget?.Npc ||
            Decision.Intent.Item.IsAssigned && Decision.Intent.Item != observation.UsefulItem?.Handle)
            return RuntimeBotActionFailureCode.TargetChanged;
        return RuntimeBotActionFailureCode.None;
    }
}

internal interface IRuntimeBotBrain
{
    RuntimeBotDecision Decide(in RuntimeBotObservationSnapshot observation, in RuntimeBotBrainState state);
}

// These are execution boundaries, not gameplay authorities. Implementations revalidate normal authority state.
internal interface IRuntimeBotNavigation
{
    void Stop();
    RuntimeBotActionResult Follow(in RuntimeBotObservationSnapshot observation);
    RuntimeBotActionResult Engage(in RuntimeBotObservationSnapshot observation);
    RuntimeBotActionResult Recover(in RuntimeBotObservationSnapshot observation);
    RuntimeBotActionResult Return(in RuntimeBotObservationSnapshot observation);
    RuntimeBotActionResult Collect(in RuntimeBotObservationSnapshot observation);
    void CancelRecovery();
}
internal interface IRuntimeBotCombat
{
    RuntimeBotActionResult Attack(in RuntimeBotObservationSnapshot observation);
}
internal interface IRuntimeBotInventory
{
    RuntimeBotActionResult Pickup(in RuntimeBotObservationSnapshot observation, WorldItemHandle item = default);
    RuntimeBotActionResult UseConsumables(in RuntimeBotObservationSnapshot observation);
    void UseCombatBuffs(in RuntimeBotObservationSnapshot observation);
}
internal interface IRuntimeBotWorldInteraction
{
    RuntimeBotActionResult AssistMining(in RuntimeBotObservationSnapshot observation);
    RuntimeBotActionResult Mine(in RuntimeBotObservationSnapshot observation) => RuntimeBotActionResult.Failure(RuntimeBotActionFailureCode.UnsupportedAction);
    void FinishMining(bool failed) { }
}

internal sealed class RuntimeBotActionContext(
    RuntimeBotObservationSnapshot observation,
    IRuntimeBotNavigation navigation,
    IRuntimeBotCombat combat,
    IRuntimeBotInventory inventory,
    IRuntimeBotWorldInteraction worldInteraction)
{
    public RuntimeBotObservationSnapshot Observation { get; } = observation;
    public IRuntimeBotNavigation Navigation { get; } = navigation;
    public IRuntimeBotCombat Combat { get; } = combat;
    public IRuntimeBotInventory Inventory { get; } = inventory;
    public IRuntimeBotWorldInteraction WorldInteraction { get; } = worldInteraction;
}

internal readonly record struct RuntimeBotObservationSnapshot(
    int BotId, WorldRuntimeIdentity World, ulong ObservationRevision, ulong GoalGeneration, long Tick,
    PlayerStateSnapshot Self, RuntimeBotConfiguration Configuration, PlayerStateSnapshot? TargetPlayer,
    BotGuardTarget? GuardTarget, WorldItemSnapshot? UsefulItem, bool RecoveryRequired,
    RuntimeBotActionKind? CurrentAction, RuntimeBotActionResult? RecentActionResult)
{
    public RuntimeBotInventorySummary Inventory { get; init; }
    public WorldItemHandle LastPickedItem { get; init; }
    public bool? Underground { get; init; }
    public RuntimeBotMiningTarget? MiningTarget { get; init; }
}

internal readonly record struct RuntimeBotInventorySummary(int HealingPotions, int ManaPotions, int Arrows, int Bullets,
    bool HasMirror, bool HasPickaxe);

internal static class RuntimeBotObservationScope
{
    public static bool IsCurrent(BotState bot, WorldRuntimeIdentity world, in RuntimeBotObservationSnapshot observation) =>
        observation.BotId == bot.Id && observation.Self.Player == bot.Player && observation.World == world &&
        observation.GoalGeneration == bot.GoalGeneration && observation.ObservationRevision == bot.ObservationRevision &&
        observation.Tick == bot.CurrentTick && observation.Self.HasHealth && observation.Self.Life > 0 && !observation.Self.IsDead;
}
