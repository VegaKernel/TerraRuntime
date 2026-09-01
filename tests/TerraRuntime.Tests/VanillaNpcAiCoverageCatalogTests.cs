using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaNpcAiCoverageCatalogTests
{
    [Fact]
    public void Every_coverage_entry_has_an_explicit_definition_and_behavior_family()
    {
        int expected = 9 +
            VanillaSlimeNpcCatalog.DefinitionCount +
            VanillaFlyingEyeNpcCatalog.DefinitionCount +
            VanillaFlyerNpcCatalog.DefinitionCount +
            VanillaWormNpcCatalog.Count;
        Assert.Equal(expected, VanillaNpcAiCoverageCatalog.Count);

        foreach (VanillaNpcAiCoverage coverage in VanillaNpcAiCoverageCatalog.All)
        {
            Assert.True(VanillaNpcDefinitionCatalog.TryGet(coverage.Type, out VanillaNpcDefinition definition));
            Assert.NotEqual(VanillaNpcBehaviorFamily.None, definition.BehaviorFamily);
            Assert.NotEqual(VanillaNpcPhysicsFamily.None, definition.PhysicsFamily);
            Assert.True(coverage.Has(VanillaNpcAiCapability.DefinitionDefaults));
            Assert.True(coverage.Has(VanillaNpcAiCapability.PacketSync));
            Assert.False(coverage.FullVanillaAiParity);
        }
    }

    [Fact]
    public void Specialized_capabilities_are_claimed_only_for_their_tested_slices()
    {
        Assert.True(VanillaNpcAiCoverageCatalog.TryGet(
            VanillaNpcIds.KingSlime,
            out VanillaNpcAiCoverage kingSlime));
        Assert.True(kingSlime.Has(VanillaNpcAiCapability.ChildSpawnSlice));
        Assert.True(kingSlime.Has(VanillaNpcAiCapability.TeleportEnvironmentSlice));
        Assert.True(kingSlime.Has(VanillaNpcAiCapability.KingSlimeDifficultySeedSlice));
        Assert.False(kingSlime.FullVanillaAiParity);

        Assert.True(VanillaNpcAiCoverageCatalog.TryGet(
            VanillaNpcIds.BrainOfCthulhu,
            out VanillaNpcAiCoverage brain));
        Assert.True(brain.Has(VanillaNpcAiCapability.ChildSpawnSlice));
        Assert.True(brain.Has(VanillaNpcAiCapability.BrainBossStateSlice));
        Assert.False(brain.FullVanillaAiParity);

        Assert.True(VanillaNpcAiCoverageCatalog.TryGet(
            VanillaNpcIds.BrainCreeper,
            out VanillaNpcAiCoverage creeper));
        Assert.True(creeper.Has(VanillaNpcAiCapability.BrainCreeperStateSlice));
        Assert.False(creeper.FullVanillaAiParity);

        Assert.True(VanillaNpcAiCoverageCatalog.TryGet(
            VanillaNpcIds.EyeOfCthulhu,
            out VanillaNpcAiCoverage eyeOfCthulhu));
        Assert.True(eyeOfCthulhu.Has(VanillaNpcAiCapability.ChildSpawnSlice));
        Assert.True(eyeOfCthulhu.Has(VanillaNpcAiCapability.BossExpertPhaseOneSlice));
        Assert.True(eyeOfCthulhu.Has(VanillaNpcAiCapability.BossExpertTransformationSlice));
        Assert.True(eyeOfCthulhu.Has(VanillaNpcAiCapability.BossExpertPhaseTwoDeterministicSlice));
        Assert.True(eyeOfCthulhu.Has(VanillaNpcAiCapability.BossExpertRapidDashSlice));
        Assert.False(eyeOfCthulhu.FullVanillaAiParity);

        Assert.True(VanillaNpcAiCoverageCatalog.TryGet(
            VanillaNpcIds.Skeleton,
            out VanillaNpcAiCoverage skeleton));
        Assert.True(skeleton.Has(VanillaNpcAiCapability.CheckActiveSlice));
        Assert.True(skeleton.Has(VanillaNpcAiCapability.GroundFighterTraversalSlice));
        Assert.True(skeleton.Has(VanillaNpcAiCapability.GroundFighterDoorPressureSlice));
        Assert.False(skeleton.Has(VanillaNpcAiCapability.ChildSpawnSlice));

        Assert.True(VanillaNpcAiCoverageCatalog.TryGet(
            VanillaNpcIds.Zombie,
            out VanillaNpcAiCoverage zombie));
        Assert.True(zombie.Has(VanillaNpcAiCapability.GroundFighterDoorPressureSlice));
    }

    [Fact]
    public void Unadmitted_npc_has_no_coverage_claim()
    {
        // 6 = EaterOfSouls is now admitted via VanillaFlyerNpcCatalog; 900 is outside any catalog.
        Assert.False(VanillaNpcAiCoverageCatalog.TryGet(new NpcTypeId(900), out _));
    }
}
