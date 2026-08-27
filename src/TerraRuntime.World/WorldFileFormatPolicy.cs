namespace TerraRuntime.World;

/// <summary>
/// Version policy is intentionally separate from byte parsing. A structurally readable future world must not
/// become writable merely because its prefix still looks familiar.
/// </summary>
public static class WorldFileFormatPolicy
{
    public const int MinimumVerifiedVersion = 279;

    // Terraria 1.4.5.8 WorldFile.SaveFileFormatHeader writes 326.
    public const int MaximumVerifiedVersion = 326;

    public static WorldFormatCompatibility Assess(int formatVersion)
    {
        if (formatVersion < MinimumVerifiedVersion)
        {
            return WorldFormatCompatibility.TooOld;
        }

        return formatVersion <= MaximumVerifiedVersion
            ? WorldFormatCompatibility.Verified
            : WorldFormatCompatibility.NewerUnverified;
    }
}
