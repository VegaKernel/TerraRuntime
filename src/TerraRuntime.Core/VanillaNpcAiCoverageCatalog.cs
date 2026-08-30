using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Core;

[Flags]
public enum VanillaNpcAiCapability : ushort
{
    None = 0,
    DefinitionDefaults = 1 << 0,
    TargetingSlice = 1 << 1,
    StateTransitionSlice = 1 << 2,
    WorldPhysicsSlice = 1 << 3,
    CheckActiveSlice = 1 << 4,
    ChildSpawnSlice = 1 << 5,
    TeleportEnvironmentSlice = 1 << 6,
    PacketSync = 1 << 7,
    GroundFighterTraversalSlice = 1 << 8
}

/// <summary>
/// Executable support claim for one admitted TerrariaServer 1.4.5.8 NPC. Capability flags mean a tested
/// authoritative slice exists; they do not imply every type-specific branch or side effect is implemented.
/// </summary>
public readonly record struct VanillaNpcAiCoverage(
    NpcTypeId Type,
    VanillaNpcAiCapability Capabilities,
    bool FullVanillaAiParity)
{
    public bool Has(VanillaNpcAiCapability capability) =>
        capability != VanillaNpcAiCapability.None &&
        (Capabilities & capability) == capability;
}

public static class VanillaNpcAiCoverageCatalog
{
    private const VanillaNpcAiCapability OrdinaryCore =
        VanillaNpcAiCapability.DefinitionDefaults |
        VanillaNpcAiCapability.TargetingSlice |
        VanillaNpcAiCapability.StateTransitionSlice |
        VanillaNpcAiCapability.WorldPhysicsSlice |
        VanillaNpcAiCapability.PacketSync;

    private static readonly VanillaNpcAiCoverage[] Entries =
    [
        Partial(VanillaNpcIds.BlueSlime, OrdinaryCore),
        Partial(VanillaNpcIds.DemonEye, OrdinaryCore),
        Partial(
            VanillaNpcIds.Zombie,
            OrdinaryCore |
            VanillaNpcAiCapability.CheckActiveSlice |
            VanillaNpcAiCapability.GroundFighterTraversalSlice),
        Partial(
            VanillaNpcIds.EyeOfCthulhu,
            OrdinaryCore | VanillaNpcAiCapability.ChildSpawnSlice),
        Partial(VanillaNpcIds.ServantOfCthulhu, OrdinaryCore),
        Partial(
            VanillaNpcIds.Skeleton,
            OrdinaryCore |
            VanillaNpcAiCapability.CheckActiveSlice |
            VanillaNpcAiCapability.GroundFighterTraversalSlice),
        Partial(
            VanillaNpcIds.KingSlime,
            OrdinaryCore |
            VanillaNpcAiCapability.ChildSpawnSlice |
            VanillaNpcAiCapability.TeleportEnvironmentSlice)
    ];

    public static int Count => Entries.Length;

    public static ReadOnlySpan<VanillaNpcAiCoverage> All => Entries;

    public static bool TryGet(NpcTypeId type, out VanillaNpcAiCoverage coverage)
    {
        foreach (VanillaNpcAiCoverage candidate in Entries)
        {
            if (candidate.Type == type)
            {
                coverage = candidate;
                return true;
            }
        }

        coverage = default;
        return false;
    }

    private static VanillaNpcAiCoverage Partial(
        NpcTypeId type,
        VanillaNpcAiCapability capabilities) =>
        new(type, capabilities, FullVanillaAiParity: false);
}
