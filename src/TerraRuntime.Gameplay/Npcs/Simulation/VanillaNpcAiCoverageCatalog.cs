using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

[Flags]
public enum VanillaNpcAiCapability : ulong
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
    WormChainSpawnSlice = 1u << 18,
    WormSplitRepairSlice = 1u << 19,
    BossExpertPhaseOneSlice = 1u << 20,
    KingSlimeDifficultySeedSlice = 1u << 21,
    BossExpertTransformationSlice = 1u << 22,
    BossExpertPhaseTwoDeterministicSlice = 1u << 23,
    BossExpertRapidDashSlice = 1u << 24,
    FlyingEyeLifecycleStateSlice = 1u << 25,
    FlyerProjectileSideEffectSlice = 1u << 26,
    BrainTeleportStateSlice = 1u << 27,
    BrainCreeperLifecycleSlice = 1u << 28,
    BrainBossStateSlice = 1u << 29,
    BrainCreeperStateSlice = 1u << 30,
    VultureMotionSlice = 1ul << 31,
    SpikeBallMotionSlice = 1ul << 32,
    BlazingWheelMotionSlice = 1ul << 33,
    SkeletronHeadStateSlice = 1ul << 34,
    SkeletronHandStateSlice = 1ul << 35,
    SkeletronSkullProjectileSlice = 1ul << 36,
    BossDeathLootProgressionSlice = 1ul << 37,
    QueenBeeStateSlice = 1ul << 38,
    QueenBeeMinionSpawnSlice = 1ul << 39,
    QueenBeeStingerProjectileSlice = 1ul << 40,
    DeerclopsStateSlice = 1ul << 41,
    DeerclopsProjectileSlice = 1ul << 42
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
            13 +
            VanillaSlimeNpcCatalog.DefinitionCount +
            VanillaFlyingEyeNpcCatalog.DefinitionCount +
            VanillaFlyerNpcCatalog.DefinitionCount +
            VanillaWormNpcCatalog.Count +
            VanillaNpcAi17_20_21Catalog1458.DefinitionCount];
        entries[0] = Partial(
            VanillaNpcIds.BlueSlime,
            OrdinaryCore |
            VanillaNpcAiCapability.SlimeTimerProfileSlice |
            VanillaNpcAiCapability.NegativeNetVariantDefaults);
        entries[1] = Partial(
            VanillaNpcIds.DemonEye,
            OrdinaryCore |
            VanillaNpcAiCapability.FlyingEyeSteeringProfileSlice |
            VanillaNpcAiCapability.FlyingEyeLifecycleStateSlice |
            VanillaNpcAiCapability.NegativeNetVariantDefaults);
        entries[2] = Partial(
            VanillaNpcIds.Zombie,
            OrdinaryCore |
            VanillaNpcAiCapability.CheckActiveSlice |
            VanillaNpcAiCapability.GroundFighterTraversalSlice |
            VanillaNpcAiCapability.GroundFighterDoorPressureSlice);
        entries[3] = Partial(
            VanillaNpcIds.EyeOfCthulhu,
            OrdinaryCore |
            VanillaNpcAiCapability.ChildSpawnSlice |
            VanillaNpcAiCapability.BossExpertPhaseOneSlice |
            VanillaNpcAiCapability.BossExpertTransformationSlice |
            VanillaNpcAiCapability.BossExpertPhaseTwoDeterministicSlice |
            VanillaNpcAiCapability.BossExpertRapidDashSlice);
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
            VanillaNpcAiCapability.TeleportEnvironmentSlice |
            VanillaNpcAiCapability.KingSlimeDifficultySeedSlice);
        entries[7] = Partial(
            VanillaNpcIds.BrainOfCthulhu,
            VanillaNpcAiCapability.BrainBossStateSlice |
            OrdinaryCore |
            VanillaNpcAiCapability.ChildSpawnSlice |
            VanillaNpcAiCapability.TeleportEnvironmentSlice |
            VanillaNpcAiCapability.BrainTeleportStateSlice);
        entries[8] = Partial(
            VanillaNpcIds.BrainCreeper,
            VanillaNpcAiCapability.BrainCreeperStateSlice |
            OrdinaryCore | VanillaNpcAiCapability.BrainCreeperLifecycleSlice);
        entries[9] = Partial(
            VanillaNpcIds.SkeletronHead,
            OrdinaryCore |
            VanillaNpcAiCapability.ChildSpawnSlice |
            VanillaNpcAiCapability.SkeletronHeadStateSlice |
            VanillaNpcAiCapability.SkeletronSkullProjectileSlice |
            VanillaNpcAiCapability.BossDeathLootProgressionSlice);
        entries[10] = Partial(
            VanillaNpcIds.SkeletronHand,
            OrdinaryCore |
            VanillaNpcAiCapability.SkeletronHandStateSlice);
        entries[11] = Partial(
            VanillaNpcIds.QueenBee,
            OrdinaryCore |
            VanillaNpcAiCapability.ChildSpawnSlice |
            VanillaNpcAiCapability.QueenBeeStateSlice |
            VanillaNpcAiCapability.QueenBeeMinionSpawnSlice |
            VanillaNpcAiCapability.QueenBeeStingerProjectileSlice |
            VanillaNpcAiCapability.BossDeathLootProgressionSlice);
        entries[12] = Partial(
            VanillaNpcIds.Deerclops,
            OrdinaryCore |
            VanillaNpcAiCapability.DeerclopsStateSlice |
            VanillaNpcAiCapability.DeerclopsProjectileSlice |
            VanillaNpcAiCapability.BossDeathLootProgressionSlice);

        int index = 13;
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
                OrdinaryCore |
                VanillaNpcAiCapability.FlyingEyeSteeringProfileSlice |
                VanillaNpcAiCapability.FlyingEyeLifecycleStateSlice;
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
            if (definition.Type == VanillaNpcIds.Probe || definition.Type == VanillaNpcIds.BloodSquid)
                capabilities |= VanillaNpcAiCapability.FlyerProjectileSideEffectSlice;

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
                    capabilities |= VanillaNpcAiCapability.WormChainSpawnSlice;
            }

            if (worm.Role == VanillaWormSegmentRole.Body &&
                VanillaWormNpcCatalog.HasChainProfile(worm.HeadType))
                capabilities |= VanillaNpcAiCapability.WormChainSpawnSlice;

            if (worm.HeadType == VanillaNpcIds.EaterOfWorldsHead)
                capabilities |= VanillaNpcAiCapability.WormSplitRepairSlice;

            entries[index++] = Partial(worm.Definition.Type, capabilities);
        }

        foreach (VanillaNpcDefinition definition in VanillaNpcAi17_20_21Catalog1458.AllDefinitions)
        {
            VanillaNpcAiCapability slice = definition.BehaviorFamily switch
            {
                VanillaNpcBehaviorFamily.Vulture => VanillaNpcAiCapability.VultureMotionSlice,
                VanillaNpcBehaviorFamily.SpikeBall => VanillaNpcAiCapability.SpikeBallMotionSlice,
                VanillaNpcBehaviorFamily.BlazingWheel => VanillaNpcAiCapability.BlazingWheelMotionSlice,
                _ => throw new InvalidOperationException("Unexpected AI_017/020/021 behavior family.")
            };
            entries[index++] = Partial(definition.Type, OrdinaryCore | slice);
        }

        if (index != entries.Length)
            throw new InvalidOperationException("Vanilla NPC coverage catalog count drifted.");

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
