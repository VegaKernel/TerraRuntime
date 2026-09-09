namespace TerraRuntime.Application.Bots;

/// <summary>Pure goal selection. No authority references, RNG, I/O or actor mutation.</summary>
internal sealed class DeterministicRuntimeBotBrain : IRuntimeBotBrain
{
    public RuntimeBotDecision Decide(in RuntimeBotObservationSnapshot observation, in RuntimeBotBrainState state)
    {
        if (observation.Self.IsDead) return new(new(RuntimeBotActionKind.Idle));
        var mode = observation.Configuration.Mode;
        if (mode == RuntimeBotMode.Idle) return new(new(RuntimeBotActionKind.Idle));
        if (mode == RuntimeBotMode.Collect) return new(new(RuntimeBotActionKind.Collect));
        if (mode == RuntimeBotMode.Mining) return new(new(observation.UsefulItem is not null &&
            (state.CurrentAction != RuntimeBotActionKind.Mining || observation.MiningTarget is null)
            ? RuntimeBotActionKind.Collect : RuntimeBotActionKind.Mining));
        if (observation.TargetPlayer is null) return new(new(RuntimeBotActionKind.Idle));
        var kind = observation.RecoveryRequired ? RuntimeBotActionKind.RecoverToTarget : mode switch
        {
            RuntimeBotMode.Follow => RuntimeBotActionKind.Follow,
            RuntimeBotMode.Guard => RuntimeBotActionKind.Guard,
            RuntimeBotMode.ReturnToPlayer => RuntimeBotActionKind.ReturnToPlayer,
            _ => (RuntimeBotActionKind)byte.MaxValue
        };
        return new(new(kind, observation.Configuration.Target.Player));
    }
}
