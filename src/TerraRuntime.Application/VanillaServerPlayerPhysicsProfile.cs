using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Ordinary unmounted player constants selected near the start of TerrariaServer 1.4.5.8 Player.Update.
/// Liquid flags are from the preceding contact pass; the current pass is detected later, before collision movement.
/// </summary>
internal static class VanillaServerPlayerPhysicsProfile
{
    internal const float FallSpeedNudge = 0.01f;

    public static VanillaServerPlayerPhysicsParameters Resolve(
        in VanillaLiquidContactState previousContacts)
    {
        if (previousContacts.Shimmer)
        {
            return new VanillaServerPlayerPhysicsParameters(
                Gravity: 0.15f,
                MaximumFallSpeed: VanillaServerPlayerDryPhysicsStepper.MaximumFallSpeed + FallSpeedNudge,
                JumpSpeed: 5.51f,
                JumpHeight: 23);
        }

        if (previousContacts.Wet && previousContacts.Honey)
        {
            return new VanillaServerPlayerPhysicsParameters(
                Gravity: 0.1f,
                MaximumFallSpeed: 3f + FallSpeedNudge,
                JumpSpeed: VanillaServerPlayerJumpControl.JumpSpeed,
                JumpHeight: VanillaServerPlayerJumpControl.JumpHeight);
        }

        if (previousContacts.Wet)
        {
            return new VanillaServerPlayerPhysicsParameters(
                Gravity: 0.2f,
                MaximumFallSpeed: 5f + FallSpeedNudge,
                JumpSpeed: 6.01f,
                JumpHeight: 30);
        }

        return new VanillaServerPlayerPhysicsParameters(
            VanillaServerPlayerDryPhysicsStepper.Gravity,
            VanillaServerPlayerDryPhysicsStepper.MaximumFallSpeed + FallSpeedNudge,
            VanillaServerPlayerJumpControl.JumpSpeed,
            VanillaServerPlayerJumpControl.JumpHeight);
    }
}

internal readonly record struct VanillaServerPlayerPhysicsParameters(
    float Gravity,
    float MaximumFallSpeed,
    float JumpSpeed,
    int JumpHeight);
