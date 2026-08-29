using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Previous-tick liquid contact state used by TerrariaServer 1.4.5.8 when selecting the next player movement profile.
/// Contact is refreshed later in Player.Update, after jump/gravity processing and before collision dispatch, so the
/// profile for a tick intentionally lags the collision/displacement liquid contact by one authoritative tick.
/// </summary>
internal readonly record struct VanillaServerPlayerLiquidState(
    bool Wet,
    bool Lava,
    bool Honey,
    bool Shimmer)
{
    public static VanillaServerPlayerLiquidState Dry => default;

    public bool IsValid => Wet || (!Lava && !Honey && !Shimmer);

    public static VanillaServerPlayerLiquidState FromContacts(in VanillaLiquidContactState contacts) =>
        new(contacts.Wet, contacts.Lava, contacts.Honey, contacts.Shimmer);
}

internal readonly record struct VanillaServerPlayerMotionProfile(
    float Gravity,
    float MaximumFallSpeed,
    float JumpSpeed,
    int JumpHeight);

/// <summary>
/// Source-backed ordinary, unmounted, normal-gravity player liquid profile from TerrariaServer 1.4.5.8 Player.Update.
/// Accessories, merman/trident movement, floating equipment, mounts, grapples and shimmer transformation are outside
/// this profile. Vanilla adds 0.01 to maxFallSpeed after selecting the medium-specific baseline.
/// </summary>
internal static class VanillaServerPlayerLiquidPhysics
{
    internal const float MaximumFallSpeedEpsilon = 0.01f;

    internal const float DryGravity = 0.4f;
    internal const float DryMaximumFallSpeedBase = 10f;
    internal const float DryJumpSpeed = 5.01f;
    internal const int DryJumpHeight = 15;

    internal const float WaterGravity = 0.2f;
    internal const float WaterMaximumFallSpeedBase = 5f;
    internal const float WaterJumpSpeed = 6.01f;
    internal const int WaterJumpHeight = 30;

    internal const float HoneyGravity = 0.1f;
    internal const float HoneyMaximumFallSpeedBase = 3f;

    internal const float ShimmerGravity = 0.15f;
    internal const float ShimmerJumpSpeed = 5.51f;
    internal const int ShimmerJumpHeight = 23;

    public static VanillaServerPlayerMotionProfile ResolveMotionProfile(
        in VanillaServerPlayerLiquidState previousLiquidState)
    {
        if (!previousLiquidState.IsValid)
            throw new ArgumentException("Liquid state contains a specialized liquid flag without Wet.", nameof(previousLiquidState));

        if (previousLiquidState.Shimmer)
        {
            return new VanillaServerPlayerMotionProfile(
                ShimmerGravity,
                DryMaximumFallSpeedBase + MaximumFallSpeedEpsilon,
                ShimmerJumpSpeed,
                ShimmerJumpHeight);
        }

        if (previousLiquidState.Wet)
        {
            if (previousLiquidState.Honey)
            {
                return new VanillaServerPlayerMotionProfile(
                    HoneyGravity,
                    HoneyMaximumFallSpeedBase + MaximumFallSpeedEpsilon,
                    DryJumpSpeed,
                    DryJumpHeight);
            }

            return new VanillaServerPlayerMotionProfile(
                WaterGravity,
                WaterMaximumFallSpeedBase + MaximumFallSpeedEpsilon,
                WaterJumpSpeed,
                WaterJumpHeight);
        }

        return new VanillaServerPlayerMotionProfile(
            DryGravity,
            DryMaximumFallSpeedBase + MaximumFallSpeedEpsilon,
            DryJumpSpeed,
            DryJumpHeight);
    }

    public static int ClampRemainingJumpOnLiquidExit(
        int remainingJumpTicks,
        in VanillaServerPlayerLiquidState previousLiquidState,
        in VanillaServerPlayerLiquidState currentLiquidState,
        int activeJumpHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(remainingJumpTicks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(activeJumpHeight);

        if (previousLiquidState.Wet && !currentLiquidState.Wet)
            return Math.Min(remainingJumpTicks, activeJumpHeight / 5);

        return remainingJumpTicks;
    }
}
