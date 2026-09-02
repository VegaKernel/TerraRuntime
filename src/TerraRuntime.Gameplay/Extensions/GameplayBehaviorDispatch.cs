using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Extensions;

/// <summary>Execution stage for an explicitly registered gameplay behavior around the vanilla/runtime step.</summary>
public enum GameplayBehaviorStage : byte
{
    Pre = 0,
    Replacement = 1,
    Post = 2
}

/// <summary>One stable extension behavior bound into an immutable dispatch plan.</summary>
public readonly record struct GameplayBehaviorBinding<TBehavior>(
    GameplayExtensionId Id,
    int Order,
    TBehavior Behavior)
    where TBehavior : class;

/// <summary>
/// Immutable per-target dispatch plan. Arrays are exposed as read-only memories so authoritative hot paths can
/// enumerate without locks or allocation while runtime registries remain responsible for publication/revision state.
/// </summary>
public sealed class GameplayBehaviorDispatchPlan<TBehavior>
    where TBehavior : class
{
    private readonly GameplayBehaviorBinding<TBehavior>[] pre;
    private readonly GameplayBehaviorBinding<TBehavior>[] post;

    public GameplayBehaviorDispatchPlan(
        GameplayBehaviorBinding<TBehavior>[] pre,
        bool hasReplacement,
        GameplayBehaviorBinding<TBehavior> replacement,
        GameplayBehaviorBinding<TBehavior>[] post)
    {
        ArgumentNullException.ThrowIfNull(pre);
        ArgumentNullException.ThrowIfNull(post);
        this.pre = pre;
        HasReplacement = hasReplacement;
        Replacement = replacement;
        this.post = post;
    }

    public ReadOnlyMemory<GameplayBehaviorBinding<TBehavior>> Pre => pre;

    public bool HasReplacement { get; }

    public GameplayBehaviorBinding<TBehavior> Replacement { get; }

    public ReadOnlyMemory<GameplayBehaviorBinding<TBehavior>> Post => post;
}
