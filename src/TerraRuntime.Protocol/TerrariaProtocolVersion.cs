namespace TerraRuntime.Protocol;

public static class TerrariaProtocolVersion
{
    public const int CurrentRelease = 326;
    public const string CurrentVersionString = "Terraria326";
    public const int MaximumVersionBannerByteLength = 32;

    public static bool IsCurrent(int release) => release == CurrentRelease;
}
