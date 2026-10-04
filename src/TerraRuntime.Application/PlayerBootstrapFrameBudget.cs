using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

/// <summary>
/// Hard upper bounds for the packet-8 tile bootstrap before packet 49 hands the connection over to normal
/// gameplay. The source banner/bestiary baseline precedes that handoff; other runtime entities retain their existing phase.
/// </summary>
internal static class PlayerBootstrapFrameBudget
{
    public const int FixedFramesBeforeEnterWorld = 2; // repeated packet 7 + packet 9

    public const int MaximumTileSectionFrames =
        InitialSectionBootstrapPlanner.MaximumBaseSectionCount +
        InitialSectionBootstrapPlanner.MaximumRequestedSectionCount +
        InitialSectionBootstrapPlanner.MaximumTeamSpawnSectionCount;

    // These limits still bound cached/detached bootstrap sources, but production no longer emits them between the
    // final packet 10 and packet 49.
    public const int MaximumGlobalPostSectionFrames = WorldGlobalTownNpcBootstrapPacketEncoder.MaximumFrames;

    public const int MaximumWorldItemSlots = RuntimeWorldItemStore.VanillaCapacity;
    public const int MaximumDynamicEntityFrames =
        MaximumWorldItemSlots * WorldItemBootstrapPacketEncoder.FramesPerItem;

    public const int MaximumDeathPreludeFrames = 1 + VanillaNpcBestiaryNetCatalog1458.MaximumKnownNetIdentities * 3;

    public const int MaximumFramesBeforeEnterWorld =
        FixedFramesBeforeEnterWorld + MaximumTileSectionFrames + MaximumDeathPreludeFrames;

    // The live probe counts packet-10 frames while waiting for packet 49. Leave a small emergency margin above the
    // current structural ceiling so CI catches accidental bootstrap growth long before outbound backpressure.
    public const int LiveProbeFrameBudget = 96;
}
