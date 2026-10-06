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

    // The probe starts after repeated WorldInfo/status and counts sections, the bounded source
    // banner/bestiary baseline, and packet 49 itself. Do not retain the former tile-only ceiling.
    public const int LiveProbeFrameBudget = MaximumFramesBeforeEnterWorld - FixedFramesBeforeEnterWorld + 1;
}
