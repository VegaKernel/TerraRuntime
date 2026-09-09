using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

/// <summary>
/// Runtime-owned state for the ordinary dry, unmounted TerrariaServer 1.4.5.8 jump path. RemainingTicks mirrors the
/// vanilla jump counter; ReleaseReady mirrors releaseJump. It is deliberately not exposed through HostContracts.
/// </summary>
internal readonly record struct VanillaServerPlayerJumpState(int RemainingTicks, bool ReleaseReady, int WingTime = 180)
{
    public static VanillaServerPlayerJumpState Initial => new(0, true);

    public bool IsValid =>
        RemainingTicks is >= 0 and <= VanillaServerPlayerJumpControl.MaximumSupportedJumpHeight &&
        (RemainingTicks == 0 || !ReleaseReady) && WingTime is >= 0 and <= 180;
}

/// <summary>
/// Source-backed ordinary jump control for an unmounted player. The caller supplies the dry/water/honey/shimmer
/// profile selected by Player.Update. Accessories, mounts, grapples, auto-jump and extra jumps remain outside it.
/// </summary>
internal static class VanillaServerPlayerJumpControl
{
    internal const float JumpSpeed = 5.01f;
    internal const int JumpHeight = 15;
    internal const int MaximumSupportedJumpHeight = 30;

    public static bool TryApply(
        float velocityY,
        ServerPlayerJumpIntent intent,
        in VanillaServerPlayerJumpState state,
        out float nextVelocityY,
        out VanillaServerPlayerJumpState nextState) =>
        TryApply(
            velocityY,
            intent,
            in state,
            JumpSpeed,
            JumpHeight,
            out nextVelocityY,
            out nextState);

    public static bool TryApply(
        float velocityY,
        ServerPlayerJumpIntent intent,
        in VanillaServerPlayerJumpState state,
        float jumpSpeed,
        int jumpHeight,
        out float nextVelocityY,
        out VanillaServerPlayerJumpState nextState)
    {
        if (!float.IsFinite(velocityY) ||
            !float.IsFinite(jumpSpeed) ||
            jumpSpeed <= 0f ||
            jumpHeight is <= 0 or > MaximumSupportedJumpHeight ||
            !state.IsValid ||
            state.RemainingTicks > jumpHeight ||
            intent is not ServerPlayerJumpIntent.Released and not ServerPlayerJumpIntent.Held)
        {
            nextVelocityY = default;
            nextState = default;
            return false;
        }

        nextVelocityY = velocityY;
        if (intent == ServerPlayerJumpIntent.Released)
        {
            nextState = state with { RemainingTicks = 0, ReleaseReady = true, WingTime = velocityY == 0f ? 180 : state.WingTime };
            return true;
        }

        if (state.RemainingTicks > 0)
        {
            if (velocityY == 0f)
            {
                nextState = state with { RemainingTicks = 0, ReleaseReady = false };
                return true;
            }

            nextVelocityY = -jumpSpeed;
            nextState = state with { RemainingTicks = state.RemainingTicks - 1, ReleaseReady = false };
            return true;
        }

        if (velocityY == 0f && state.ReleaseReady)
        {
            nextVelocityY = -jumpSpeed;
            nextState = state with { RemainingTicks = jumpHeight, ReleaseReady = false };
            return true;
        }

        nextState = state;
        return true;
    }
}
