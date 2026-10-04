using TerraRuntime.World;

namespace TerraRuntime.Application;

// Saved scalar facts plus the owner-thread progression journal; no weather/remote-zone defaults.
public readonly record struct RuntimeTownSocialWorld1458
{
    internal WorldFileRuntimeMetadata Metadata { get; init; }
    internal RuntimeWorldProgressionMutationSnapshot Progression { get; init; }
    internal bool ExpertMode { get; init; }
    internal bool DayTime { get; init; }
    internal bool BloodMoon { get; init; }
    internal double Time { get; init; }

    public static RuntimeTownSocialWorld1458 FromMetadata(WorldFileRuntimeMetadata metadata, bool expertMode)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        // Main.Difficulty promotes Classic to Expert in Good World; this affects pirate emote candidates.
        return new() { Metadata = metadata, ExpertMode = expertMode || metadata.GetGoodWorld, DayTime = metadata.DayTime,
            BloodMoon = metadata.BloodMoon, Time = metadata.Time };
    }

    internal bool Done(VanillaWorldProgressionId milestone, bool saved) => saved || Progression.IsCompleted(milestone);
    internal bool HardMode => Done(VanillaWorldProgressionId.Hardmode, Metadata.HardMode);
}
