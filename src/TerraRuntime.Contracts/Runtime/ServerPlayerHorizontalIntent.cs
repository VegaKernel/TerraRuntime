namespace TerraRuntime.Contracts.Runtime;

/// <summary>
/// Semantic horizontal control intent for a runtime-owned player. The runtime, not the caller, owns acceleration,
/// slowdown, collision and final velocity.
/// </summary>
public enum ServerPlayerHorizontalIntent : sbyte
{
    Left = -1,
    Stop = 0,
    Right = 1
}
