using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.HostContracts.WorldGeneration;

namespace TerraRuntime.Application;

/// <summary>
/// Deterministic startup view over host-registered world generators. Keeping this adapter in the runtime bootstrap
/// avoids exposing the CoreCLR loader itself to menu/CLI code and gives interactive selection one stable ordering.
/// </summary>
internal static class StartupWorldGeneratorCatalog
{
    public static WorldGeneratorId[] Capture(ITerraRuntimeWorldGeneratorSource? source)
    {
        if (source is null)
            return [];

        WorldGeneratorId[] ids = source.CaptureWorldGeneratorIds().ToArray();
        Array.Sort(ids);
        return ids;
    }
}
