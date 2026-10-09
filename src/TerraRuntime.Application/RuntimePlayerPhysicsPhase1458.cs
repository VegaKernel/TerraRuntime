using TerraRuntime.World;

namespace TerraRuntime.Application;

internal readonly record struct RuntimePlayerPhysicsPhase1458(
    VanillaServerPlayerJumpState Jump, VanillaLiquidContactState Contacts)
{
    internal float GravityDirection { get; init; } = 1f;
    // Player's constructor has not observed a released jump input yet.
    internal static RuntimePlayerPhysicsPhase1458 Constructor => new(new(0, false, 0), default);
}
