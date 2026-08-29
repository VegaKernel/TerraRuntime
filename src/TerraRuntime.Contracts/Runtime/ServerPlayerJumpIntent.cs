namespace TerraRuntime.Contracts.Runtime;

/// <summary>
/// Semantic jump-button state for a runtime-owned server player. The host reports whether jump is currently held;
/// TerraRuntime owns jump speed, duration, release gating, gravity and collision response.
/// </summary>
public enum ServerPlayerJumpIntent : byte
{
    Released = 0,
    Held = 1
}
