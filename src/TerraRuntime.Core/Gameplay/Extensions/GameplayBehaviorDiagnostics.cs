using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Core.Extensions;

/// <summary>
/// Receives faults raised by host-provided gameplay behavior. Diagnostics must remain observational; callers of
/// the behavior pipeline do not depend on the sink succeeding.
/// </summary>
public interface IGameplayBehaviorFaultSink
{
    void BehaviorFaulted(GameplayExtensionId id, GameplayBehaviorStage stage, Exception exception);
}
