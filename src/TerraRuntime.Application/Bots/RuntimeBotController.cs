using TerraRuntime.Contracts.Runtime;
using TerraRuntime.HostContracts;
using static TerraRuntime.Application.Bots.BotPolicy;

namespace TerraRuntime.Application.Bots;

/// <summary>Schedules invariant self-care and one replaceable goal executor. Does not implement gameplay mechanics.</summary>
internal sealed class RuntimeBotController
{
    private readonly BotState bot;
    private readonly ServerPlayerAuthority players;
    private readonly RuntimeBotPerception perception;
    private readonly IRuntimeBotBrain brain;
    private readonly RuntimeBotActionExecutor executor;
    private readonly RuntimeBotNavigation navigation;
    private readonly RuntimeBotCombat combat;
    private readonly RuntimeBotInventory inventory;
    private readonly RuntimeBotWorldInteraction worldInteraction;
    private RuntimeBotActionContext? lastContext;
    public RuntimeBotActionKind? Current => executor.Current;
    public RuntimeBotActionResult? RecentResult => executor.RecentResult;
    public RuntimeBotObservationSnapshot? Observation => lastContext?.Observation;

    public RuntimeBotController(BotState bot, ServerPlayerAuthority players, RuntimeBotPerception perception,
        IRuntimeBotBrain brain, RuntimeBotActionExecutor executor, RuntimeBotNavigation navigation,
        RuntimeBotCombat combat, RuntimeBotInventory inventory, RuntimeBotWorldInteraction worldInteraction)
    {
        this.bot = bot;
        this.players = players;
        this.perception = perception;
        this.brain = brain;
        this.executor = executor;
        this.navigation = navigation;
        this.combat = combat;
        this.inventory = inventory;
        this.worldInteraction = worldInteraction;
    }

    public void Cancel(RuntimeBotActionCancelReason reason)
    {
        if (lastContext is not null) executor.Cancel(lastContext, reason);
        navigation.CancelRecovery();
    }

    public void Tick(long tick)
    {
        bot.CurrentTick = tick;
        ExpireBuffs(bot, tick);
        if (!players.TryGet(bot.Player, out var self) || self.IsDead)
        {
            Cancel(RuntimeBotActionCancelReason.BotUnavailable);
            bot.TargetAvailable = false;
            bot.PvpEnabled = self.Hostile;
            bot.IsStuck = false;
            bot.IsDead = true;
            return;
        }
        bot.IsDead = false;
        if (tick >= bot.UseItemUntilTick && (self.ControlFlags & ControlUseItemFlag) != 0)
            _ = players.SetHeldItem(bot.ServerPlayerId, self.SelectedItem, useItem: false);

        // Invariant, instantaneous maintenance is deliberately not a competing long-lived goal.
        // It runs before perception/decision so they see post-pickup and post-healing vitals.
        bot.LastPickedItem = default;
        var maintenance = perception.Capture(tick, executor, forDecision: false);
        _ = inventory.Pickup(maintenance);
        _ = inventory.UseConsumables(maintenance);
        navigation.FinishRecovery(tick);
        var observation = perception.Capture(tick, executor);
        var context = new RuntimeBotActionContext(observation, navigation, combat, inventory, worldInteraction);
        lastContext = context;
        bot.TargetAvailable = observation.TargetPlayer is not null;
        if (observation.Self.Hostile != bot.PvpEnabled)
            _ = players.SetHostile(bot.ServerPlayerId, bot.PvpEnabled);
        if (!bot.TargetAvailable && bot.Configuration.Mode is not (RuntimeBotMode.Collect or RuntimeBotMode.Mining))
        {
            bool wasRecovering = bot.MirrorStartedAtTick >= 0;
            navigation.CancelRecovery();
            _ = players.SetHeldItem(bot.ServerPlayerId,
                wasRecovering ? MeleeWeaponSlot : observation.Self.SelectedItem, useItem: false);
            bot.IsStuck = false;
            bot.LastDistance = float.PositiveInfinity;
            bot.LastProgressTick = tick;
            bot.FlightDecisionUntilTick = bot.TraversalUntilTick = bot.NextTraversalSearchTick = 0;
            bot.LockedGuardNpc = default;
            bot.LockedGuardPlayer = default;
            bot.GuardTargetLockUntilTick = 0;
        }
        var state = new RuntimeBotBrainState(executor.Current, executor.RecentResult);
        var decision = brain.Decide(observation, state);
        if (executor.Current != decision.Intent.Kind)
        {
            var envelope = new RuntimeBotDecisionEnvelope(bot.Id, bot.Player, observation.World,
                observation.ObservationRevision, observation.GoalGeneration, decision);
            _ = executor.Start(RuntimeBotAction.Create(decision.Intent.Kind), envelope, context);
        }
        _ = executor.Tick(context);
    }
}
