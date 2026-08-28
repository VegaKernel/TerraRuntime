using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Hard upper bounds for the initial packet-8 bootstrap before packet 49 hands the connection
/// over to normal gameplay. Keep this below the connection outbound queue capacity so a valid
/// world cannot fill the queue merely by joining a player.
/// </summary>
internal static class PlayerBootstrapFrameBudget
{
    public const int FixedFramesBeforeEnterWorld = 2; // packet 7 + packet 9

    public const int MaximumTileSectionFrames =
        InitialSectionBootstrapPlanner.MaximumBaseSectionCount +
        InitialSectionBootstrapPlanner.MaximumRequestedSectionCount +
        InitialSectionBootstrapPlanner.MaximumTeamSpawnSectionCount;

    public const int MaximumGlobalPostSectionFrames = WorldGlobalTownNpcBootstrapPacketEncoder.MaximumFrames;

    public const int MaximumWorldItemSlots = RuntimeWorldItemStore.VanillaCapacity;
    public const int MaximumDynamicEntityFrames =
        MaximumWorldItemSlots * WorldItemBootstrapPacketEncoder.FramesPerItem;

    public const int MaximumFramesBeforeEnterWorld =
        FixedFramesBeforeEnterWorld +
        MaximumTileSectionFrames +
        MaximumGlobalPostSectionFrames +
        MaximumDynamicEntityFrames;

    // Keep the live probe slightly above the structural maximum but well below the
    // production connection queue depth of 4096 frames.
    public const int LiveProbeFrameBudget = 1_536;
}
