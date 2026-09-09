using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

/// <summary>
/// Source-backed ordinary-player horizontal movement slice from TerrariaServer 1.4.5.8 Player.HorizontalMovement.
/// It deliberately models the unmounted, no-track-boost baseline: left/right acceleration below max run speed and
/// the ordinary grounded/airborne slowdown fallback. Dash, mounts, wind, sandstorm, wrong-ground and portal branches
/// remain outside this slice.
/// </summary>
internal static class VanillaServerPlayerHorizontalControl
{
    internal const float MaximumRunSpeed = 3f;
    internal const float RunAcceleration = 0.08f;
    internal const float RunSlowdown = 0.2f;
    internal const float AirborneRunSlowdown = RunSlowdown * 0.5f;

    public static float Apply(
        float velocityX,
        float velocityY,
        ServerPlayerHorizontalIntent intent) =>
        Apply(velocityX, velocityY, intent, VanillaServerPlayerHorizontalProfile1458.Baseline);

    public static float Apply(
        float velocityX,
        float velocityY,
        ServerPlayerHorizontalIntent intent,
        in VanillaServerPlayerHorizontalProfile1458 profile)
    {
        if (!float.IsFinite(velocityX) || !float.IsFinite(velocityY) || !profile.IsValid)
            return velocityX;

        if (intent == ServerPlayerHorizontalIntent.Left && velocityX > -profile.MaximumRunSpeed)
        {
            if (velocityX > profile.RunSlowdown)
                velocityX -= profile.RunSlowdown;
            return velocityX - profile.RunAcceleration;
        }

        if (intent == ServerPlayerHorizontalIntent.Right && velocityX < profile.MaximumRunSpeed)
        {
            if (velocityX < -profile.RunSlowdown)
                velocityX += profile.RunSlowdown;
            return velocityX + profile.RunAcceleration;
        }

        if (intent == ServerPlayerHorizontalIntent.Left && velocityX > -profile.AcceleratedRunSpeed &&
            (velocityY == 0f || profile.WingHorizontalAcceleration))
        {
            float acceleration = profile.RunAcceleration * 0.2f;
            if (profile.WingHorizontalAcceleration)
                acceleration *= 2f;
            return velocityX - acceleration;
        }

        if (intent == ServerPlayerHorizontalIntent.Right && velocityX < profile.AcceleratedRunSpeed &&
            (velocityY == 0f || profile.WingHorizontalAcceleration))
        {
            float acceleration = profile.RunAcceleration * 0.2f;
            if (profile.WingHorizontalAcceleration)
                acceleration *= 2f;
            return velocityX + acceleration;
        }

        if (intent is not ServerPlayerHorizontalIntent.Left and
            not ServerPlayerHorizontalIntent.Stop and
            not ServerPlayerHorizontalIntent.Right)
        {
            throw new ArgumentOutOfRangeException(nameof(intent), intent, "Unknown server-player horizontal intent.");
        }

        float slowdown = velocityY == 0f ? profile.RunSlowdown : profile.RunSlowdown * 0.5f;
        if (velocityX > slowdown)
            return velocityX - slowdown;
        if (velocityX < -slowdown)
            return velocityX + slowdown;
        return 0f;
    }
}

/// <summary>
/// Verified TerrariaServer 1.4.5.8 horizontal parameters after the admitted functional-accessory slice. Terraspark
/// Boots set <c>accRunSpeed=6.75</c> and add <c>0.08</c> move speed; grounded Magiluminescence multiplies run
/// acceleration by <c>1.75</c> and both run-speed caps by <c>1.15</c>. No unmodelled dash is granted.
/// </summary>
internal readonly record struct VanillaServerPlayerHorizontalProfile1458(
    float MaximumRunSpeed,
    float AcceleratedRunSpeed,
    float RunAcceleration,
    float RunSlowdown,
    bool WingHorizontalAcceleration,
    bool SoaringInsignia = false)
{
    public static VanillaServerPlayerHorizontalProfile1458 Baseline => new(
        VanillaServerPlayerHorizontalControl.MaximumRunSpeed,
        VanillaServerPlayerHorizontalControl.MaximumRunSpeed,
        VanillaServerPlayerHorizontalControl.RunAcceleration,
        VanillaServerPlayerHorizontalControl.RunSlowdown,
        WingHorizontalAcceleration: false);

    public static VanillaServerPlayerHorizontalProfile1458 ResolveBotMobility(
        bool terrasparkBoots,
        bool magiluminescence,
        bool fishronWings,
        bool grounded,
        bool soaringInsignia = false)
    {
        if (!terrasparkBoots && (!magiluminescence || !grounded) && !fishronWings && !soaringInsignia)
            return Baseline;

        float moveSpeed = 1f + (terrasparkBoots ? 0.08f : 0f) + (soaringInsignia ? 0.075f : 0f);
        float maximumRunSpeed = VanillaServerPlayerHorizontalControl.MaximumRunSpeed * moveSpeed;
        float acceleratedRunSpeed = terrasparkBoots
            ? 6.75f
            : VanillaServerPlayerHorizontalControl.MaximumRunSpeed;
        float runAcceleration = VanillaServerPlayerHorizontalControl.RunAcceleration * moveSpeed;
        float runSlowdown = VanillaServerPlayerHorizontalControl.RunSlowdown;
        // WingStatsInitializer[26], then Player.Update's WingAirLogicTweaks / empressBrooch order.
        if (fishronWings && !grounded)
        {
            acceleratedRunSpeed = Math.Max(acceleratedRunSpeed, 8f);
            runAcceleration *= 2f;
        }
        if (soaringInsignia)
            runAcceleration *= 1.75f;
        if (magiluminescence && grounded)
        {
            maximumRunSpeed *= 1.15f;
            acceleratedRunSpeed *= 1.15f;
            runAcceleration *= 1.75f;
            runSlowdown *= 1.75f;
        }

        return new VanillaServerPlayerHorizontalProfile1458(
            maximumRunSpeed,
            acceleratedRunSpeed,
            runAcceleration,
            runSlowdown,
            WingHorizontalAcceleration: fishronWings,
            SoaringInsignia: soaringInsignia);
    }

    public bool IsValid =>
        float.IsFinite(MaximumRunSpeed) && MaximumRunSpeed > 0f &&
        // Move-speed equipment can raise maxRunSpeed above the unchanged ordinary accRunSpeed=3.
        float.IsFinite(AcceleratedRunSpeed) && AcceleratedRunSpeed > 0f &&
        float.IsFinite(RunAcceleration) && RunAcceleration > 0f &&
        float.IsFinite(RunSlowdown) && RunSlowdown > 0f;
}
