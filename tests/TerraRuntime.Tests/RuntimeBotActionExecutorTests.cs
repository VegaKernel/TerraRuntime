using TerraRuntime.Application.Bots;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Tests;

public sealed class RuntimeBotActionExecutorTests
{
    [Fact]
    public void Executor_cannot_be_rebound_to_another_bot_with_an_otherwise_valid_envelope()
    {
        var executor = new RuntimeBotActionExecutor(new());
        var observation = Observation();
        var first = new ProbeAction();
        executor.Start(first, Envelope(observation), Context(observation));
        var foreign = observation with { BotId = 2 };
        var next = new ProbeAction();
        Assert.Equal(RuntimeBotActionFailureCode.StaleDecision, executor.Start(next, Envelope(foreign), Context(foreign)).FailureCode);
        Assert.Equal(0, next.Entries);
        Assert.Equal(0, first.Cancellations);
    }

    [Fact]
    public void New_tick_with_a_regressing_observation_revision_is_rejected()
    {
        var executor = new RuntimeBotActionExecutor(new());
        var observation = Observation();
        var action = new ProbeAction { TerminalAt = 100 };
        executor.Start(action, Envelope(observation), Context(observation));
        executor.Tick(Context(observation with { ObservationRevision = 5 }));
        var result = executor.Tick(Context(observation with { Tick = observation.Tick + 1, ObservationRevision = 4 }));
        Assert.Equal(RuntimeBotActionFailureCode.StaleDecision, result.FailureCode);
        Assert.Equal(1, action.Ticks);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Faulted_callback_releases_leases_without_hiding_invariant_exception(int phase)
    {
        var leases = new RuntimeBotResourceLeases();
        var executor = new RuntimeBotActionExecutor(leases);
        var observation = Observation();
        var action = new FaultedAction(phase, () => Claim(leases, observation));
        var context = Context(observation);
        if (phase == 0) Assert.Throws<InvalidOperationException>(() => executor.Start(action, Envelope(observation), context));
        else
        {
            executor.Start(action, Envelope(observation), context);
            if (phase == 3) Assert.Throws<InvalidOperationException>(() => executor.Cancel(context, RuntimeBotActionCancelReason.Requested));
            else Assert.Throws<InvalidOperationException>(() => executor.Tick(context));
        }
        Assert.Equal(0, leases.Count);
        Assert.Null(executor.Current);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Terminal_result_releases_resources_and_exits_once(int terminal)
    {
        var leases = new RuntimeBotResourceLeases();
        var executor = new RuntimeBotActionExecutor(leases);
        var observation = Observation();
        var action = new ProbeAction { TerminalAt = 1, Terminal = (RuntimeBotActionStatus)terminal };
        executor.Start(action, Envelope(observation), Context(observation));
        Claim(leases, observation);
        executor.Tick(Context(observation));
        executor.Tick(Context(Next(observation, 1)));
        Assert.Equal(1, action.Exits);
        Assert.Equal(0, action.Cancellations);
        Assert.Equal(0, leases.Count);
    }

    [Fact]
    public void Capability_scope_refuses_detached_old_generation_revision_tick_and_world()
    {
        var observation = Observation();
        var bot = new BotState(observation.BotId, new("bot:1"), "bot", observation.Configuration,
            default, default, default, observation.Tick)
        {
            Player = observation.Self.Player, ObservationRevision = observation.ObservationRevision,
            CurrentTick = observation.Tick
        };
        Assert.True(RuntimeBotObservationScope.IsCurrent(bot, observation.World, observation));
        Assert.False(RuntimeBotObservationScope.IsCurrent(bot, observation.World, observation with { GoalGeneration = 2 }));
        Assert.False(RuntimeBotObservationScope.IsCurrent(bot, observation.World, observation with { ObservationRevision = 2 }));
        Assert.False(RuntimeBotObservationScope.IsCurrent(bot, observation.World, observation with { Tick = observation.Tick + 1 }));
        Assert.False(RuntimeBotObservationScope.IsCurrent(bot, observation.World, observation with { World = default }));
        bot.Player = new(new PlayerSlotId(1), new PlayerSessionGeneration(2));
        Assert.False(RuntimeBotObservationScope.IsCurrent(bot, observation.World, observation));
    }

    [Fact]
    public void Npc_and_item_generation_changes_invalidate_envelope()
    {
        var observation = Observation();
        var npc = new NpcHandle(1, new NpcGeneration(1));
        var envelope = Envelope(observation) with { Decision = new(new(RuntimeBotActionKind.Attack, Npc: npc)) };
        Assert.Equal(RuntimeBotActionFailureCode.TargetChanged, envelope.Validate(observation));
        observation = observation with { GuardTarget = new(npc, default, 0, 0, 0, 0, 20, 20) };
        Assert.Equal(RuntimeBotActionFailureCode.None, envelope.Validate(observation));
        observation = observation with { GuardTarget = observation.GuardTarget.Value with { Npc = new(1, new NpcGeneration(2)) } };
        Assert.Equal(RuntimeBotActionFailureCode.TargetChanged, envelope.Validate(observation));
        envelope = Envelope(observation) with { Decision = new(new(RuntimeBotActionKind.PickupUsefulItem, Item: new(0, new WorldItemGeneration(1)))) };
        Assert.Equal(RuntimeBotActionFailureCode.TargetChanged, envelope.Validate(observation));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Enter_once_multiple_ticks_and_terminal_result(int terminal)
    {
        var executor = new RuntimeBotActionExecutor(new());
        var action = new ProbeAction { Terminal = (RuntimeBotActionStatus)terminal };
        var observation = Observation();
        Assert.Equal(RuntimeBotActionStatus.Pending, executor.Start(action, Envelope(observation), Context(observation)).Status);
        Assert.Equal(RuntimeBotActionStatus.Pending, executor.Tick(Context(observation)).Status);
        Assert.Equal(RuntimeBotActionStatus.Pending, executor.Tick(Context(Next(observation, 1))).Status);
        Assert.Equal((RuntimeBotActionStatus)terminal, executor.Tick(Context(Next(observation, 2))).Status);
        Assert.Equal(1, action.Entries);
        Assert.Equal(3, action.Ticks);
        Assert.Null(executor.Current);
        executor.Tick(Context(Next(observation, 3)));
        Assert.Equal(3, action.Ticks);
    }

    [Fact]
    public void Switch_and_explicit_cancel_call_cancel_once_and_release_leases()
    {
        var leases = new RuntimeBotResourceLeases();
        var executor = new RuntimeBotActionExecutor(leases);
        var first = new ProbeAction();
        var second = new ProbeAction();
        var observation = Observation();
        executor.Start(first, Envelope(observation), Context(observation));
        Claim(leases, observation);
        executor.Start(second, Envelope(observation), Context(observation));
        Assert.Equal(1, first.Cancellations);
        Assert.Equal(0, leases.Count);
        Assert.Equal(1, second.Entries);
        Claim(leases, observation);
        executor.Cancel(Context(observation), RuntimeBotActionCancelReason.Requested);
        executor.Cancel(Context(observation), RuntimeBotActionCancelReason.Requested);
        Assert.Equal(1, second.Cancellations);
        Assert.Equal(RuntimeBotActionStatus.Cancelled, executor.RecentResult!.Value.Status);
        Assert.Equal(0, leases.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Timeout_or_action_defined_stagnation_cancels_and_releases(bool stagnation)
    {
        var leases = new RuntimeBotResourceLeases();
        var executor = new RuntimeBotActionExecutor(leases);
        var action = new ProbeAction { LimitsValue = stagnation ? new(0, 2) : new(2, 0), TerminalAt = 100 };
        var observation = Observation();
        executor.Start(action, Envelope(observation), Context(observation));
        Claim(leases, observation);
        executor.Tick(Context(observation));
        executor.Tick(Context(Next(observation, 1)));
        var result = executor.Tick(Context(Next(observation, 2)));
        Assert.Equal(stagnation ? RuntimeBotActionFailureCode.Stuck : RuntimeBotActionFailureCode.TimedOut, result.FailureCode);
        Assert.Equal(1, action.Cancellations);
        Assert.Equal(0, leases.Count);
    }

    [Fact]
    public void Semantic_progress_can_be_stationary_and_keeps_stagnation_alive()
    {
        var executor = new RuntimeBotActionExecutor(new());
        var action = new ProbeAction { LimitsValue = new(0, 2), TerminalAt = 100, Progress = true };
        var observation = Observation();
        executor.Start(action, Envelope(observation), Context(observation));
        for (int i = 0; i < 20; i++)
            Assert.Equal(RuntimeBotActionStatus.Pending, executor.Tick(Context(Next(observation, i))).Status);
        Assert.Equal(0, action.Cancellations);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Stale_envelope_never_enters_or_displaces_current_action(int mismatch)
    {
        var executor = new RuntimeBotActionExecutor(new());
        var observation = Observation();
        var first = new ProbeAction();
        executor.Start(first, Envelope(observation), Context(observation));
        var envelope = Envelope(observation);
        envelope = mismatch switch
        {
            0 => envelope with { BotId = 2 },
            1 => envelope with { BotGeneration = new(new PlayerSlotId(1), new PlayerSessionGeneration(2)) },
            2 => envelope with { GoalGeneration = 2 },
            3 => envelope with { ObservationRevision = 2 },
            _ => envelope with { World = new(WorldRuntimeId.CreateNew(), WorldSessionId.CreateNew()) }
        };
        var next = new ProbeAction();
        Assert.Equal(RuntimeBotActionStatus.Failure, executor.Start(next, envelope, Context(observation)).Status);
        Assert.Equal(0, next.Entries);
        Assert.Equal(0, first.Cancellations);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Running_action_rejects_changed_world_bot_goal_or_target_before_tick(int mismatch)
    {
        var executor = new RuntimeBotActionExecutor(new());
        var observation = Observation();
        var target = new PlayerHandle(new PlayerSlotId(2), new PlayerSessionGeneration(1));
        observation = observation with
        {
            Configuration = observation.Configuration with { Target = new(target, "target") },
            TargetPlayer = default(PlayerStateSnapshot) with { Player = target, HasHealth = true, Life = 100 }
        };
        var action = new ProbeAction();
        var envelope = Envelope(observation) with { Decision = new(new(RuntimeBotActionKind.Follow, target)) };
        executor.Start(action, envelope, Context(observation));
        observation = mismatch switch
        {
            0 => observation with { World = new(WorldRuntimeId.CreateNew(), WorldSessionId.CreateNew()) },
            1 => observation with { Self = observation.Self with { Player = target } },
            2 => observation with { GoalGeneration = 2 },
            _ => observation with { TargetPlayer = null }
        };
        Assert.Equal(RuntimeBotActionStatus.Failure, executor.Tick(Context(observation)).Status);
        Assert.Equal(0, action.Ticks);
        Assert.Equal(1, action.Cancellations);
        Assert.Equal(1, action.CancelledBotId);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 7)]
    [InlineData(4, 8)]
    [InlineData(5, 9)]
    public void Deterministic_brain_maps_modes_without_mutation(int mode, int kind)
    {
        var observation = Observation();
        observation = observation with
        {
            Configuration = observation.Configuration with { Mode = (RuntimeBotMode)mode },
            TargetPlayer = observation.Self
        };
        var brain = new DeterministicRuntimeBotBrain();
        Assert.Equal((RuntimeBotActionKind)kind, brain.Decide(observation, default).Intent.Kind);
        Assert.Equal(brain.Decide(observation, default), brain.Decide(observation, default));
    }

    [Fact]
    public void Brain_prioritizes_recovery_and_fails_to_idle_on_lost_target()
    {
        var observation = Observation() with { RecoveryRequired = true };
        var brain = new DeterministicRuntimeBotBrain();
        Assert.Equal(RuntimeBotActionKind.Idle, brain.Decide(observation, default).Intent.Kind);
        observation = observation with { TargetPlayer = observation.Self };
        Assert.Equal(RuntimeBotActionKind.RecoverToTarget, brain.Decide(observation, default).Intent.Kind);
    }

    [Fact]
    public void Lease_competition_expiry_generations_and_despawn_cleanup()
    {
        var leases = new RuntimeBotResourceLeases();
        var observation = Observation();
        var owner = new RuntimeBotLeaseOwner(1, observation.Self.Player, observation.World);
        var other = owner with { BotId = 2 };
        var resource = RuntimeBotResourceKey.ForItem(observation.World, new(1, new WorldItemGeneration(1)));
        Assert.True(leases.TryAcquire(owner, resource, 10, 5));
        Assert.False(leases.TryAcquire(other, resource, 14, 5));
        Assert.False(leases.Release(other, resource));
        Assert.True(leases.TryAcquire(other, resource, 15, 5));
        leases.ReleaseBot(owner);
        Assert.Equal(1, leases.Count);
        leases.ReleaseBot(other);
        Assert.Equal(0, leases.Count);
        Assert.False(leases.TryAcquire(default, resource, 20));
        Assert.False(leases.TryAcquire(owner, resource, 20, 0));
        Assert.True(leases.TryAcquire(owner, resource, 20, 5));
        Assert.True(leases.TryAcquire(other, resource with { Item = new(1, new WorldItemGeneration(2)) }, 20, 5));
        leases.Cleanup(25);
        Assert.Equal(0, leases.Count);
    }

    private static RuntimeBotObservationSnapshot Observation() => new(1,
        new(WorldRuntimeId.CreateNew(), WorldSessionId.CreateNew()), 1, 1, 10,
        default(PlayerStateSnapshot) with { Player = new(new PlayerSlotId(1), new PlayerSessionGeneration(1)), HasHealth = true, Life = 100 },
        new(RuntimeBotMode.Follow, default), null, null, null, false, null, null);
    private static RuntimeBotObservationSnapshot Next(RuntimeBotObservationSnapshot observation, int delta) =>
        observation with { Tick = observation.Tick + delta, ObservationRevision = observation.ObservationRevision + (ulong)delta };
    private static RuntimeBotDecisionEnvelope Envelope(RuntimeBotObservationSnapshot observation) =>
        new(observation.BotId, observation.Self.Player, observation.World, observation.ObservationRevision,
            observation.GoalGeneration, new(new(RuntimeBotActionKind.Follow)));
    private static RuntimeBotActionContext Context(RuntimeBotObservationSnapshot observation) => new(observation, null!, null!, null!, null!);
    private static void Claim(RuntimeBotResourceLeases leases, RuntimeBotObservationSnapshot observation) => Assert.True(leases.TryAcquire(
        new(observation.BotId, observation.Self.Player, observation.World),
        RuntimeBotResourceKey.ForItem(observation.World, new(0, new WorldItemGeneration(1))), observation.Tick));

    private sealed class ProbeAction : IRuntimeBotAction
    {
        public RuntimeBotActionKind Kind => RuntimeBotActionKind.Follow;
        public RuntimeBotActionLimits LimitsValue;
        public RuntimeBotActionLimits Limits => LimitsValue;
        public RuntimeBotActionStatus Terminal = RuntimeBotActionStatus.Success;
        public int TerminalAt = 3;
        public bool Progress;
        public int Entries, Ticks, Cancellations, Exits, CancelledBotId;
        public void Enter(RuntimeBotActionContext context) => Entries++;
        public RuntimeBotActionResult Tick(RuntimeBotActionContext context)
        {
            Ticks++;
            return Ticks < TerminalAt ? RuntimeBotActionResult.Pending(Progress ? Ticks : 0) :
                new(Terminal, Terminal == RuntimeBotActionStatus.Failure ? RuntimeBotActionFailureCode.UnsupportedAction : RuntimeBotActionFailureCode.None);
        }
        public void Cancel(RuntimeBotActionContext context, RuntimeBotActionCancelReason reason)
        {
            Cancellations++;
            CancelledBotId = context.Observation.BotId;
        }
        public void Exit(RuntimeBotActionContext context, RuntimeBotActionResult result) => Exits++;
    }

    private sealed class FaultedAction(int phase, Action claim) : IRuntimeBotAction
    {
        public RuntimeBotActionKind Kind => RuntimeBotActionKind.Follow;
        public RuntimeBotActionLimits Limits => default;
        public void Enter(RuntimeBotActionContext context)
        {
            claim();
            if (phase == 0) throw new InvalidOperationException("fixture Enter invariant");
        }
        public RuntimeBotActionResult Tick(RuntimeBotActionContext context) => phase == 1
            ? throw new InvalidOperationException("fixture Tick invariant") : RuntimeBotActionResult.Success;
        public void Exit(RuntimeBotActionContext context, RuntimeBotActionResult result)
        {
            if (phase == 2) throw new InvalidOperationException("fixture Exit invariant");
        }
        public void Cancel(RuntimeBotActionContext context, RuntimeBotActionCancelReason reason)
        {
            if (phase == 3) throw new InvalidOperationException("fixture Cancel invariant");
        }
    }
}
