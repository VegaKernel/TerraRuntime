namespace TerraRuntime.Contracts.Runtime;

public enum ServerPlayerMovementIntentKind : byte
{
    Stop = 1,
    MoveTo = 2,
    FollowPlayer = 3
}

/// <summary>
/// Bounded policy for a runtime-owned player controller. It selects button-like intent only; vanilla acceleration,
/// jump velocity, gravity, collision and final position remain runtime-owned.
/// </summary>
public readonly record struct ServerPlayerMovementOptions(
    float StopDistance,
    float JumpVerticalThreshold,
    float MaximumDistance)
{
    public static ServerPlayerMovementOptions Default => new(
        StopDistance: 12f,
        JumpVerticalThreshold: 24f,
        MaximumDistance: 0f);

    public bool IsValid =>
        float.IsFinite(StopDistance) && StopDistance is >= 0f and <= 1_024f &&
        float.IsFinite(JumpVerticalThreshold) && JumpVerticalThreshold is >= 0f and <= 1_024f &&
        float.IsFinite(MaximumDistance) && MaximumDistance is >= 0f and <= 65_536f;
}

/// <summary>High-level fake-player target intent; it never carries a requested velocity or final position.</summary>
public readonly record struct ServerPlayerMovementIntent(
    ServerPlayerMovementIntentKind Kind,
    float TargetX,
    float TargetY,
    PlayerHandle TargetPlayer,
    ServerPlayerMovementOptions Options)
{
    public static ServerPlayerMovementIntent Stop(ServerPlayerMovementOptions? options = null) =>
        new(ServerPlayerMovementIntentKind.Stop, 0f, 0f, default, options ?? ServerPlayerMovementOptions.Default);

    public static ServerPlayerMovementIntent MoveTo(
        float targetX,
        float targetY,
        ServerPlayerMovementOptions? options = null) =>
        new(
            ServerPlayerMovementIntentKind.MoveTo,
            targetX,
            targetY,
            default,
            options ?? ServerPlayerMovementOptions.Default);

    public static ServerPlayerMovementIntent FollowPlayer(
        PlayerHandle target,
        ServerPlayerMovementOptions? options = null) =>
        new(
            ServerPlayerMovementIntentKind.FollowPlayer,
            0f,
            0f,
            target,
            options ?? ServerPlayerMovementOptions.Default);

    public bool IsValid =>
        Options.IsValid &&
        Kind switch
        {
            ServerPlayerMovementIntentKind.Stop => true,
            ServerPlayerMovementIntentKind.MoveTo => float.IsFinite(TargetX) && float.IsFinite(TargetY),
            ServerPlayerMovementIntentKind.FollowPlayer => TargetPlayer.IsAssigned,
            _ => false
        };
}
