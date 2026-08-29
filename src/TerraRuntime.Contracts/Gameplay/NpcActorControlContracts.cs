using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Contracts.Gameplay;

/// <summary>
/// Stable host-defined identity for one actor controller. This identity is control-plane metadata and is never
/// serialized into Terraria protocol fields.
/// </summary>
public readonly record struct ActorControllerId : IComparable<ActorControllerId>
{
    public const int MaxLength = 128;

    public ActorControllerId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > MaxLength)
            throw new ArgumentOutOfRangeException(nameof(value));

        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (char.IsWhiteSpace(character) || char.IsControl(character))
                throw new ArgumentException("Actor controller IDs cannot contain whitespace or control characters.", nameof(value));
        }

        Value = value;
    }

    public string? Value { get; }

    public bool IsAssigned => Value is not null;

    public int CompareTo(ActorControllerId other) =>
        string.Compare(Value, other.Value, StringComparison.Ordinal);

    public override string ToString() => Value ?? string.Empty;
}

public enum NpcActorIntentKind : byte
{
    Stop = 1,
    MoveTo = 2,
    FollowPlayer = 3
}

/// <summary>
/// Bounded movement policy supplied by a host. The policy controls desired horizontal motion only; final position,
/// gravity, step-up, liquids and tile collision remain owned by the runtime world-motion path.
/// MaximumDistance == 0 means no distance cutoff.
/// </summary>
public readonly record struct NpcActorMotionOptions(
    float StopDistance,
    float MaximumHorizontalSpeed,
    float HorizontalAcceleration,
    float MaximumDistance)
{
    public static NpcActorMotionOptions Default => new(
        StopDistance: 24f,
        MaximumHorizontalSpeed: 1.5f,
        HorizontalAcceleration: 0.08f,
        MaximumDistance: 0f);

    public bool IsValid =>
        float.IsFinite(StopDistance) && StopDistance >= 0f && StopDistance <= 1_024f &&
        float.IsFinite(MaximumHorizontalSpeed) && MaximumHorizontalSpeed > 0f && MaximumHorizontalSpeed <= 16f &&
        float.IsFinite(HorizontalAcceleration) && HorizontalAcceleration > 0f && HorizontalAcceleration <= 4f &&
        float.IsFinite(MaximumDistance) && MaximumDistance >= 0f && MaximumDistance <= 65_536f;
}

/// <summary>
/// Runtime actor intent. MoveTo/FollowPlayer express a target; they do not authorize direct position writes.
/// </summary>
public readonly record struct NpcActorIntent(
    NpcActorIntentKind Kind,
    float TargetX,
    float TargetY,
    PlayerHandle TargetPlayer,
    NpcActorMotionOptions Motion)
{
    public static NpcActorIntent Stop(NpcActorMotionOptions? motion = null) =>
        new(NpcActorIntentKind.Stop, 0f, 0f, default, motion ?? NpcActorMotionOptions.Default);

    public static NpcActorIntent MoveTo(
        float targetX,
        float targetY,
        NpcActorMotionOptions? motion = null) =>
        new(NpcActorIntentKind.MoveTo, targetX, targetY, default, motion ?? NpcActorMotionOptions.Default);

    public static NpcActorIntent FollowPlayer(
        PlayerHandle target,
        NpcActorMotionOptions? motion = null) =>
        new(NpcActorIntentKind.FollowPlayer, 0f, 0f, target, motion ?? NpcActorMotionOptions.Default);

    public bool IsValid =>
        Motion.IsValid &&
        Kind switch
        {
            NpcActorIntentKind.Stop => true,
            NpcActorIntentKind.MoveTo => float.IsFinite(TargetX) && float.IsFinite(TargetY),
            NpcActorIntentKind.FollowPlayer => TargetPlayer.IsAssigned,
            _ => false
        };
}
