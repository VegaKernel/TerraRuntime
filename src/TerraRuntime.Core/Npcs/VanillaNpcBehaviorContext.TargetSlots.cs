namespace TerraRuntime.Core;

/// <summary>
/// Keeps ushort NPC target slots at the behavior boundary while preserving the runtime's byte-addressed player table.
/// Invalid/sentinel values fail closed instead of relying on an unchecked narrowing conversion.
/// </summary>
internal static class VanillaNpcBehaviorContextTargetSlotExtensions
{
    public static bool TryFindCandidate(
        this VanillaNpcBehaviorContext context,
        ushort slot,
        out VanillaNpcTargetCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (slot >= byte.MaxValue)
        {
            candidate = default;
            return false;
        }

        return context.TryFindCandidate((byte)slot, out candidate);
    }
}
