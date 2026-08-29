using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime;

/// <summary>
/// Runtime-owned state for the ordinary dry, unmounted TerrariaServer 1.4.5.8 jump path. RemainingTicks mirrors the
/// vanilla jump counter; ReleaseReady mirrors releaseJump. It is deliberately not exposed through HostContracts.
/// </summary>
internal readonly record struct VanillaServerPlayerJumpState(int RemainingTicks, bool ReleaseReady)
{
    public static VanillaServerPlayerJumpState Initial => new(0, true);

    public bool IsValid =>
        RemainingTicks is >= 0 and <= VanillaServerPlayerJumpControl.JumpHeight &&
        (RemainingTicks == 0 || !ReleaseReady);
}

/// <summary>
/// Source-backed ordinary jump control for an unmounted, normal-gravity, dry player. Accessories, mounts, liquids,
/// grapples, auto-jump and extra-jump families remain outside this slice.
/// </summary>
internal static class VanillaServerPlayerJumpControl
{
    internal const float JumpSpeed = 5.01f;
    internal const int JumpHeight = 15;

    public static bool TryApply(
        float velocityY,
        ServerPlayerJumpIntent intent,
        in VanillaServerPlayerJumpState state,
        out float nextVelocityY,
        out VanillaServerPlayerJumpState nextState)
    {
        if (!float.IsFinite(velocityY) ||
            !state.IsValid ||
            intent is not ServerPlayerJumpIntent.Released and not ServerPlayerJumpIntent.Held)
        {
            nextVelocityY = default;
            nextState = default;
            return false;
        }

        nextVelocityY = velocityY;
        if (intent == ServerPlayerJumpIntent.Released)
        {
            nextState = VanillaServerPlayerJumpState.Initial;
            return true;
        }

        if (state.RemainingTicks > 0)
        {
            if (velocityY == 0f)
            {
                nextState = new VanillaServerPlayerJumpState(0, false);
                return true;
            }

            nextVelocityY = -JumpSpeed;
            nextState = new VanillaServerPlayerJumpState(state.RemainingTicks - 1, false);
            return true;
        }

        if (velocityY == 0f && state.ReleaseReady)
        {
            nextVelocityY = -JumpSpeed;
            nextState = new VanillaServerPlayerJumpState(JumpHeight, false);
            return true;
        }

        nextState = state;
        return true;
    }
}
