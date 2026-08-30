using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Core;

[Flags]
public enum VanillaNpcAiCapability : uint
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
    GroundFighterTraversalSlice = 1 << 8,
    GroundFighterDoorPressureSlice = 1 << 9,
    SlimeTimerProfileSlice = 1 << 10,
    NegativeNetVariantDefaults = 1 << 11,
    FlyingEyeSteeringProfileSlice = 1 << 12,
    FlyerPursuitProfileSlice = 1 << 13,
    WormRelationshipCatalog = 1 << 14,
    WormMotionPrimitive = 1u << 15,
    WormSegmentFollowSlice = 1u << 16,
    WormHeadWorldSteeringSlice = 1u << 17,
    WormChainSpawnSlice = 1u << 18
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

    private static readonly VanillaNpcAiCoverage[] Entries = CreateEntries();

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

    private static VanillaNpcAiCoverage[] CreateEntries()
    {
        var entries = new VanillaNpcAiCoverage[
            7 +
            VanillaSlimeNpcCatalog.DefinitionCount +
            VanillaFlyingEyeNpcCatalog.DefinitionCount +
            VanillaFlyerNpcCatalog.DefinitionCount +
            VanillaWormNpcCatalog.Count];
        entries[0] = Partial(
            VanillaNpcIds.BlueSlime,
            OrdinaryCore |
            VanillaNpcAiCapability.SlimeTimerProfileSlice |
            VanillaNpcAiCapability.NegativeNetVariantDefaults);
        entries[1] = Partial(
            VanillaNpcIds.DemonEye,
            OrdinaryCore |
            VanillaNpcAiCapability.FlyingEyeSteeringProfileSlice |
            VanillaNpcAiCapability.NegativeNetVariantDefaults);
        entries[2] = Partial(
            VanillaNpcIds.Zombie,
            OrdinaryCore |
            VanillaNpcAiCapability.CheckActiveSlice |
            VanillaNpcAiCapability.GroundFighterTraversalSlice |
            VanillaNpcAiCapability.GroundFighterDoorPressureSlice);
        entries[3] = Partial(
            VanillaNpcIds.EyeOfCthulhu,
            OrdinaryCore | VanillaNpcAiCapability.ChildSpawnSlice);
        entries[4] = Partial(
            VanillaNpcIds.ServantOfCthulhu,
            OrdinaryCore | VanillaNpcAiCapability.FlyerPursuitProfileSlice);
        entries[5] = Partial(
            VanillaNpcIds.Skeleton,
            OrdinaryCore |
            VanillaNpcAiCapability.CheckActiveSlice |
            VanillaNpcAiCapability.GroundFighterTraversalSlice |
            VanillaNpcAiCapability.GroundFighterDoorPressureSlice);
        entries[6] = Partial(
            VanillaNpcIds.KingSlime,
            OrdinaryCore |
            VanillaNpcAiCapability.ChildSpawnSlice |
            VanillaNpcAiCapability.TeleportEnvironmentSlice);

        int index = 7;
        foreach (VanillaNpcDefinition definition in VanillaSlimeNpcCatalog.AllDefinitions)
        {
            VanillaNpcAiCapability capabilities =
                OrdinaryCore | VanillaNpcAiCapability.SlimeTimerProfileSlice;
            if (definition.Type == VanillaNpcIds.CorruptSlime)
                capabilities |= VanillaNpcAiCapability.NegativeNetVariantDefaults;

            entries[index++] = Partial(definition.Type, capabilities);
        }

        foreach (VanillaNpcDefinition definition in VanillaFlyingEyeNpcCatalog.AllDefinitions)
        {
            VanillaNpcAiCapability capabilities =
                OrdinaryCore | VanillaNpcAiCapability.FlyingEyeSteeringProfileSlice;
            if (HasNegativeNetVariant(definition.Type))
                capabilities |= VanillaNpcAiCapability.NegativeNetVariantDefaults;

            entries[index++] = Partial(definition.Type, capabilities);
        }

        foreach (VanillaNpcDefinition definition in VanillaFlyerNpcCatalog.AllDefinitions)
        {
            VanillaNpcAiCapability capabilities =
                OrdinaryCore | VanillaNpcAiCapability.FlyerPursuitProfileSlice;
            if (HasNegativeNetVariant(definition.Type))
                capabilities |= VanillaNpcAiCapability.NegativeNetVariantDefaults;

            entries[index++] = Partial(definition.Type, capabilities);
        }

        foreach (VanillaWormNpcEntry worm in VanillaWormNpcCatalog.All)
        {
            VanillaNpcAiCapability capabilities =
                VanillaNpcAiCapability.DefinitionDefaults |
                VanillaNpcAiCapability.PacketSync |
                VanillaNpcAiCapability.WormRelationshipCatalog |
                VanillaNpcAiCapability.WormMotionPrimitive;
            if (worm.Role != VanillaWormSegmentRole.Head)
            {
                capabilities |=
                    VanillaNpcAiCapability.StateTransitionSlice |
                    VanillaNpcAiCapability.WorldPhysicsSlice |
                    VanillaNpcAiCapability.WormSegmentFollowSlice;
            }
            else
            {
                capabilities |=
                    VanillaNpcAiCapability.TargetingSlice |
                    VanillaNpcAiCapability.StateTransitionSlice |
                    VanillaNpcAiCapability.WorldPhysicsSlice |
                    VanillaNpcAiCapability.WormHeadWorldSteeringSlice;
                if (VanillaWormNpcCatalog.HasChainProfile(worm.Definition.Type))
                {
                    capabilities |= VanillaNpcAiCapability.WormChainSpawnSlice;
                }
            }

            if (worm.Role == VanillaWormSegmentRole.Body &&
                VanillaWormNpcCatalog.HasChainProfile(worm.HeadType))
            {
                capabilities |= VanillaNpcAiCapability.WormChainSpawnSlice;
            }

            entries[index++] = Partial(
                worm.Definition.Type,
                capabilities);
        }

        return entries;
    }

    private static bool HasNegativeNetVariant(NpcTypeId type)
    {
        foreach (VanillaNpcNetVariantDefinition variant in VanillaNpcNetVariantCatalog.All)
        {
            if (variant.Type == type)
                return true;
        }

        return false;
    }
}
